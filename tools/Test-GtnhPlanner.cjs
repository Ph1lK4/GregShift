/* Bounded Planner transfer regression checks. No dependency on a real website or network.
   This controlled DOM is not a substitute for the real WebView2 roundtrip check. */
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const { File } = require('node:buffer');
const script = fs.readFileSync(path.join(__dirname, '../src/EML.App/Resources/GtnhPlanner/planner-transfer.js'), 'utf8');
const plan = '{"schemaVersion":1,"id":"test","name":"中文 Iron Ingot","recipes":[],"nodes":[],"edges":[],"fuelProfiles":[]}';
const tests = [];
const test = (name, body) => tests.push([name, body]);

function environment(options = {}) {
  const state = { tabs: [{ id: 'old', textContent: 'Existing plan' }], rows: [], timers: new Map(), observers: [], imports: [], clicks: 0, nextTimer: 0 };
  const nativeCreate = () => 'blob:controlled';
  const visible = { getClientRects: () => options.hidden ? [] : [{}] };
  const input = { files: [], dispatchEvent(event) { state.imports.push({ files: [...this.files], event }); options.onImport?.(state); } };
  const context = {
    Blob, File, TextEncoder, Event,
    location: { origin: options.origin || 'https://gtnhplanner.com' },
    URL: { createObjectURL: nativeCreate },
    setTimeout(callback, delay) { const id = ++state.nextTimer; state.timers.set(id, { callback, delay }); return id; },
    clearTimeout(id) { state.timers.delete(id); },
    MutationObserver: class {
      constructor(callback) { this.callback = callback; state.observers.push(this); }
      observe() { this.active = true; }
      disconnect() { this.active = false; }
    },
    DataTransfer: class {
      constructor() { this.files = []; this.items = { add: file => this.files.push(file) }; }
    },
    document: {
      body: {},
      querySelector(selector) {
        if (selector.includes('data-plan-file-input')) return options.noInput ? null : input;
        if (selector.includes('data-mb="share"')) return options.noShare ? null : share;
        return null;
      },
      querySelectorAll(selector) {
        if (selector.includes('Open plans')) return state.tabs;
        if (selector.includes('menuitem')) return state.rows;
        return [];
      }
    }
  };
  const row = { ...visible, dataset: {}, textContent: '', querySelector: () => ({ dataset: options.translated ? { gregshiftOriginalText: 'Export plan file' } : {}, textContent: options.translated ? '导出方案文件' : 'Export plan file' }), click() { state.clicks++; options.onExport?.(context, state); } };
  const share = { ...visible, click() { if (options.syncMenu) state.rows = [row]; options.onShare?.(context, state); } };
  const window = {};
  window.top = options.frame ? {} : window;
  context.window = window;
  vm.createContext(context);
  const installed = vm.runInContext(script, context);
  return { state, context, nativeCreate, api: window.__gregShiftPlanTransfer, installed,
    openMenu() { state.rows = [row]; for (const observer of state.observers) if (observer.active) observer.callback(); },
    tick(delay) { for (const [id, timer] of state.timers) if (timer.delay === delay) { state.timers.delete(id); timer.callback(); } }
  };
}
const settle = async () => { await new Promise(resolve => setImmediate(resolve)); };

test('exact top-frame/origin guard', () => {
  for (const options of [{ origin: 'https://gtnhplanner.com.evil.example' }, { origin: 'http://gtnhplanner.com' }, { origin: 'https://gtnhplanner.com:444' }, { frame: true }]) {
    const env = environment(options); assert.equal(env.installed, false); assert.equal(env.api, undefined);
  }
});
test('installation is idempotent', () => { const env = environment(); const api = env.api; vm.runInContext(script, env.context); assert.equal(env.api, api); });
test('import sends a UTF8 JSON file through the public input', async () => {
  const env = environment({ onImport: state => state.tabs.push({ id: 'new', textContent: '中文 Iron Ingot' }) });
  env.api.beginImport('one', plan); assert.equal(env.api.poll('one').state, 'ready');
  assert.equal(env.state.imports.length, 1); const entry = env.state.imports[0];
  assert.equal(entry.files[0].name, 'gregshift-plan.json'); assert.equal(entry.files[0].type, 'application/json');
  assert.equal(await entry.files[0].text(), plan); assert.equal(entry.event.type, 'change'); assert.equal(entry.event.bubbles, true);
});
test('import waits for a newly opened plan', () => {
  const env = environment(); env.api.beginImport('one', plan); assert.equal(env.api.poll('one').state, 'pending');
  env.state.tabs.push({ id: 'unrelated', textContent: 'Some other plan' }); assert.equal(env.api.poll('one').state, 'pending');
  env.state.tabs.push({ id: 'new', textContent: '中文 Iron Ingot' }); assert.equal(env.api.poll('one').state, 'ready');
  assert.equal(env.state.timers.size, 0);
});
test('invalid import control is explicit', () => { const env = environment({ noInput: true }); assert.throws(() => env.api.beginImport('one', plan), /未提供方案导入入口/); });
test('import applies the UTF8 byte limit, not character count', () => {
  const env = environment(); assert.throws(() => env.api.beginImport('one', '中'.repeat(4 * 1024 * 1024)), /10 MiB/); assert.equal(env.state.imports.length, 0);
});
test('import timeout is explicit and cancellation stops its timer', () => {
  const env = environment(); env.api.beginImport('one', plan); env.tick(45000); assert.match(env.api.poll('one').error, /尚未确认导入/); env.api.cancel(); assert.equal(env.state.timers.size, 0);
});
test('tokens and cancellation isolate polling', () => {
  const env = environment(); env.api.beginImport('one', plan); assert.equal(env.api.poll('different').state, 'error'); env.api.cancel(); assert.equal(env.api.poll('one').state, 'error');
});
test('export uses the translated public menu and returns exact JSON', async () => {
  const env = environment({ translated: true, onExport: context => context.URL.createObjectURL(new Blob([plan], { type: 'application/json' })) });
  env.openMenu(); env.api.beginExport('one'); await settle(); assert.equal(env.api.poll('one').state, 'ready'); assert.equal(env.api.poll('one').json, plan); assert.equal(env.context.URL.createObjectURL, env.nativeCreate);
});
test('async menu opening is observed and disconnected', async () => {
  const env = environment({ onExport: context => context.URL.createObjectURL(new Blob([plan], { type: 'application/json' })) });
  env.api.beginExport('one'); env.openMenu(); await settle(); assert.equal(env.api.poll('one').state, 'ready'); assert.equal(env.state.clicks, 1); assert.ok(env.state.observers.every(observer => !observer.active));
});
test('sync menu opening exports without relying on later mutations', async () => {
  const env = environment({ syncMenu: true, onExport: context => context.URL.createObjectURL(new Blob([plan], { type: 'application/json' })) });
  env.api.beginExport('one'); await settle(); assert.equal(env.api.poll('one').state, 'ready');
});
test('export ignores non-JSON blobs', () => {
  const env = environment({ onExport: context => context.URL.createObjectURL(new Blob(['png'], { type: 'image/png' })) });
  env.openMenu(); env.api.beginExport('one'); assert.equal(env.api.poll('one').state, 'pending'); env.api.cancel();
});
test('oversized export fails before reading its content', () => {
  const env = environment({ onExport: context => context.URL.createObjectURL(new Blob(['a'.repeat(10 * 1024 * 1024 + 1)], { type: 'application/json' })) });
  env.openMenu(); env.api.beginExport('one'); assert.match(env.api.poll('one').error, /10 MiB/); assert.equal(env.context.URL.createObjectURL, env.nativeCreate);
});
test('missing export menu fails explicitly', () => {
  const env = environment({ noShare: true }); env.api.beginExport('one'); assert.match(env.api.poll('one').error, /未提供方案导出菜单/); assert.equal(env.context.URL.createObjectURL, env.nativeCreate);
});
test('export timeout restores the hook and disconnects observers', () => {
  const env = environment(); env.api.beginExport('one'); env.tick(12000); assert.match(env.api.poll('one').error, /导出超时/); assert.equal(env.context.URL.createObjectURL, env.nativeCreate); assert.ok(env.state.observers.every(observer => !observer.active));
});
test('cancelled export cannot publish a late result to the next transfer', async () => {
  let resolveText; const blob = new Blob([], { type: 'application/json' }); blob.text = () => new Promise(resolve => { resolveText = resolve; });
  const env = environment({ onExport: context => context.URL.createObjectURL(blob) }); env.openMenu(); env.api.beginExport('one'); env.api.cancel(); env.api.beginImport('two', plan); resolveText(plan); await settle(); assert.equal(env.api.poll('two').state, 'pending');
});
test('cancelled export cannot publish a late read error to the next transfer', async () => {
  let rejectText; const blob = new Blob([], { type: 'application/json' }); blob.text = () => new Promise((_, reject) => { rejectText = reject; });
  const env = environment({ onExport: context => context.URL.createObjectURL(blob) }); env.openMenu(); env.api.beginExport('one'); env.api.cancel(); env.api.beginImport('two', plan); rejectText(new Error('first operation failed')); await settle(); assert.equal(env.api.poll('two').state, 'pending');
});
test('cancel restores hooks and removes all pending timers/observers', () => {
  const env = environment(); env.api.beginExport('one'); env.api.cancel(); assert.equal(env.context.URL.createObjectURL, env.nativeCreate); assert.equal(env.state.timers.size, 0); assert.ok(env.state.observers.every(observer => !observer.active));
});
test('importing a duplicate name waits for an additional matching tab', () => {
  const env = environment(); env.state.tabs[0].textContent = '中文 Iron Ingot'; env.api.beginImport('one', plan);
  assert.equal(env.api.poll('one').state, 'pending'); env.state.tabs.push({ id: 'new', textContent: '中文 Iron Ingot' }); assert.equal(env.api.poll('one').state, 'ready');
});
test('import confirmation follows the sites normalized surrounding whitespace', () => {
  const env = environment({ onImport: state => state.tabs.push({ id: 'new', textContent: '中文 Iron Ingot' }) });
  env.api.beginImport('one', plan.replace('中文 Iron Ingot', '  中文 Iron Ingot  ')); assert.equal(env.api.poll('one').state, 'ready');
});
test('terminal export result stays ready without an armed timeout', async () => {
  const env = environment({ onExport: context => context.URL.createObjectURL(new Blob([plan], { type: 'application/json' })) });
  env.openMenu(); env.api.beginExport('one'); await settle(); assert.equal(env.api.poll('one').state, 'ready'); assert.equal(env.state.timers.size, 0);
  env.tick(12000); assert.equal(env.api.poll('one').state, 'ready');
});
test('terminal import result stays ready without an armed timeout', () => {
  const env = environment({ onImport: state => state.tabs.push({ id: 'new', textContent: '中文 Iron Ingot' }) }); env.api.beginImport('one', plan);
  assert.equal(env.api.poll('one').state, 'ready'); assert.equal(env.state.timers.size, 0); env.tick(45000); assert.equal(env.api.poll('one').state, 'ready');
});

(async () => {
  let failed = 0;
  for (const [name, body] of tests) {
    try { await body(); console.log('PASS ' + name); }
    catch (error) { failed++; console.error('FAIL ' + name + ': ' + error.message); }
  }
  console.log(`${tests.length - failed} passed, ${failed} failed`);
  process.exitCode = failed ? 1 : 0;
})();
