/* Controlled DOM regression checks for the focused online handbook. No network,
   browser profile or player files are used. Real WebView layout is checked separately. */
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');

const source = fs.readFileSync(path.join(__dirname, '../src/EML.App/Controls/GtnhToolBrowser.Handbook.cs'), 'utf8');
const match = source.match(/internal const string Script = """\r?\n([\s\S]*?)\r?\n\s*""";/);
assert.ok(match, 'The production handbook script must be present.');
const script = match[1];
const tests = [];
const test = (name, body) => tests.push([name, body]);

function environment(options = {}) {
  const state = { observers: [], timers: new Map(), styles: [], events: [], listeners: new Map(), nextTimer: 0 };
  const element = (tag, parent = null) => {
    const attributes = new Map();
    const node = { tagName: tag, parentElement: parent, children: [], attributes,
      getAttribute: name => attributes.get(name) ?? null,
      setAttribute: (name, value) => attributes.set(name, value),
      querySelector: () => null,
      appendChild(child) { child.parentElement = this; this.children.push(child); if (child.tagName === 'style') state.styles.push(child); return child; }
    };
    if (parent) parent.children.push(node);
    return node;
  };
  const html = element('html');
  const head = element('head', html);
  const body = element('body', html);
  const article = element('article', body);
  const content = element('div', article);
  const root = element('div', content);
  const layout = element('div', root);
  const sidebar = element('aside', layout);
  const viewport = element('div', layout);
  const svg = element('svg', viewport);
  const tooltip = element('div', body);
  root.setAttribute('data-fcd3-state', options.ready === false ? 'loading' : 'ready');
  root.querySelector = selector => selector === '.qbd3-layout' ? (options.noLayout ? null : layout) :
    selector === 'svg.tmd3-svg' ? (options.noSvg ? null : svg) : null;
  state.rootPresent = options.rootPresent !== false;
  const document = {
    documentElement: html, head, body,
    querySelector: selector => selector === '.gtnh-thaumonomicon-d3' && state.rootPresent ? root : null,
    createElement: tag => element(tag)
  };
  const window = {
    addEventListener(type, callback, options) { state.listeners.set(type, { callback, once: options?.once === true }); },
    removeEventListener(type, callback) { if (state.listeners.get(type)?.callback === callback) state.listeners.delete(type); },
    dispatchEvent(event) {
      state.events.push(event.type);
      const entry = state.listeners.get(event.type);
      if (entry) { if (entry.once) state.listeners.delete(event.type); entry.callback(event); }
    }
  };
  window.top = options.frame ? {} : window;
  const context = {
    document, window,
    location: { origin: options.origin ?? 'https://gtnh.huijiwiki.com', pathname: options.pathname ?? '/wiki/研究' },
    Event: class { constructor(type) { this.type = type; } },
    MutationObserver: class {
      constructor(callback) { this.callback = callback; state.observers.push(this); }
      observe(target, options) { this.active = true; this.target = target; this.options = options; }
      disconnect() { this.active = false; }
    },
    setTimeout(callback, delay) { const id = ++state.nextTimer; state.timers.set(id, { callback, delay }); return id; },
    clearTimeout(id) { state.timers.delete(id); }
  };
  vm.createContext(context);
  return { state, context, html, body, article, content, root, layout, sidebar, viewport, svg, tooltip,
    run: () => vm.runInContext(script, context),
    notify() { for (const observer of state.observers) if (observer.active) observer.callback(); },
    tick(delay) { for (const [id, timer] of [...state.timers]) if (timer.delay === delay) { state.timers.delete(id); timer.callback(); } },
    pagehide() { window.dispatchEvent(new context.Event('pagehide')); }
  };
}

test('only the exact research page and top frame are adapted', () => {
  for (const options of [
    { origin: 'https://gtnh.huijiwiki.com.evil.example' },
    { origin: 'http://gtnh.huijiwiki.com' },
    { origin: 'https://gtnh.huijiwiki.com:444' },
    { frame: true },
    { pathname: '/wiki/研究助手' },
    { pathname: '/wiki/模板:FlowChart' },
    { pathname: '/wiki/%25E7%25A0%2594%25E7%25A9%25B6' },
    { pathname: '/wiki/%broken' }
  ]) {
    const env = environment(options);
    assert.equal(env.run(), 'unsupported');
    assert.equal(env.context.window.__gregShiftHandbookFocus, undefined);
    assert.equal(env.state.styles.length, 0);
    assert.equal(env.state.observers.length, 0);
  }
});

test('encoded research paths and trailing slashes are supported', () => {
  for (const pathname of ['/wiki/%E7%A0%94%E7%A9%B6', '/wiki/研究/']) {
    const env = environment({ pathname });
    assert.equal(env.run(), 'ready');
  }
});

test('ready components are focused without replacing interactive nodes', () => {
  const env = environment();
  const original = [env.root, env.layout, env.sidebar, env.viewport, env.svg, env.tooltip];
  assert.equal(env.run(), 'ready');
  assert.deepEqual([env.root, env.layout, env.sidebar, env.viewport, env.svg, env.tooltip], original);
  assert.equal(env.root.parentElement, env.content);
  assert.equal(env.sidebar.parentElement, env.layout);
  assert.equal(env.svg.parentElement, env.viewport);
  assert.equal(env.tooltip.parentElement, env.body);
  assert.equal(env.root.getAttribute('data-gregshift-handbook-root'), '');
  for (const ancestor of [env.content, env.article, env.body, env.html])
    assert.equal(ancestor.getAttribute('data-gregshift-handbook-ancestor'), '');
  assert.equal(env.state.styles.length, 1);
  assert.deepEqual(env.state.events, ['resize']);
  assert.ok(env.state.observers.every(observer => !observer.active));
  assert.equal(env.state.timers.size, 0);
  assert.equal(env.state.listeners.size, 0);
});

test('focus CSS keeps external research detail layers and full-height canvas', () => {
  const env = environment(); env.run();
  const css = env.state.styles[0].textContent;
  assert.match(css, /:not\(\.qbd3-tooltip\)/);
  assert.match(css, /body > \.qbd3-tooltip\s*\{\s*z-index:/);
  assert.match(css, /\[data-gregshift-handbook-root\] > \.qbd3-layout/);
  assert.match(css, /svg\.tmd3-svg[\s\S]*height: 100% !important/);
  assert.equal(env.tooltip.getAttribute('hidden'), null);
});

test('late component insertion is observed and then cleaned up', () => {
  const env = environment({ rootPresent: false });
  assert.equal(env.run(), 'pending');
  assert.equal(env.state.styles.length, 0);
  assert.equal(env.state.observers[0].target, env.html);
  assert.equal(env.state.timers.size, 1);
  env.state.rootPresent = true; env.notify();
  assert.equal(env.context.window.__gregShiftHandbookFocus.state, 'ready');
  assert.equal(env.state.styles.length, 1);
  assert.equal(env.state.timers.size, 0);
  assert.ok(env.state.observers.every(observer => !observer.active));
});

test('component readiness attribute changes are observed', () => {
  const env = environment({ ready: false });
  assert.equal(env.run(), 'pending');
  assert.deepEqual(Array.from(env.state.observers[0].options.attributeFilter), ['data-fcd3-state']);
  env.notify(); assert.equal(env.state.styles.length, 0);
  env.root.setAttribute('data-fcd3-state', 'ready'); env.notify();
  assert.equal(env.context.window.__gregShiftHandbookFocus.state, 'ready');
});

test('partial components are not hidden or marked as ready', () => {
  for (const options of [{ noLayout: true }, { noSvg: true }]) {
    const env = environment(options);
    assert.equal(env.run(), 'pending');
    assert.equal(env.state.styles.length, 0);
    assert.equal(env.root.getAttribute('data-gregshift-handbook-root'), null);
    assert.equal(env.html.getAttribute('data-gregshift-handbook-ancestor'), null);
    env.tick(30000);
    assert.equal(env.context.window.__gregShiftHandbookFocus.state, 'unavailable');
  }
});

test('missing component times out without hiding the verification or original page', () => {
  const env = environment({ rootPresent: false }); env.run();
  env.tick(30000);
  assert.equal(env.context.window.__gregShiftHandbookFocus.state, 'unavailable');
  assert.equal(env.state.styles.length, 0);
  assert.equal(env.html.getAttribute('data-gregshift-handbook-ancestor'), null);
  assert.equal(env.state.timers.size, 0);
  assert.equal(env.state.listeners.size, 0);
  assert.ok(env.state.observers.every(observer => !observer.active));
  env.state.rootPresent = true; env.notify();
  assert.equal(env.state.styles.length, 0);
});

test('leaving the page stops observation and its deadline', () => {
  const env = environment({ rootPresent: false }); env.run(); env.pagehide();
  assert.equal(env.context.window.__gregShiftHandbookFocus.state, 'unavailable');
  assert.equal(env.state.timers.size, 0);
  assert.equal(env.state.listeners.size, 0);
  assert.ok(env.state.observers.every(observer => !observer.active));
  env.state.rootPresent = true; env.notify();
  assert.equal(env.state.styles.length, 0);
});

test('repeated installation is idempotent while pending and after success', () => {
  const env = environment({ rootPresent: false });
  assert.equal(env.run(), 'pending');
  const state = env.context.window.__gregShiftHandbookFocus;
  assert.equal(env.run(), 'pending');
  assert.equal(env.context.window.__gregShiftHandbookFocus, state);
  assert.equal(env.state.observers.length, 1);
  env.state.rootPresent = true; env.notify();
  assert.equal(env.run(), 'ready');
  assert.equal(env.context.window.__gregShiftHandbookFocus, state);
  assert.equal(env.state.styles.length, 1);
  assert.deepEqual(env.state.events, ['resize']);
});

let failed = 0;
for (const [name, body] of tests) {
  try { body(); console.log(`PASS ${name}`); }
  catch (error) { failed++; console.error(`FAIL ${name}\n${error.stack}`); }
}
console.log(`${tests.length - failed}/${tests.length} handbook script checks passed.`);
if (failed) process.exitCode = 1;
