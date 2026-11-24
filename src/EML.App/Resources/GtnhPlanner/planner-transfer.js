/* GregShift: bounded, user-initiated JSON transfers through the site's public UI.
   No store inspection, host objects, messages, credentials or arbitrary file access. */
(() => {
  'use strict';
  if (window !== window.top || location.origin !== 'https://gtnhplanner.com') return false;
  if (window.__gregShiftPlanTransfer) return true;
  const limit = 10 * 1024 * 1024;
  let transfer = null;
  const visible = element => element.getClientRects().length > 0;
  const text = element => element.dataset.gregshiftOriginalText || element.textContent.trim();
  const tabs = () => Array.from(document.querySelectorAll('[role="tablist"][aria-label="Open plans"] [role="tab"]'));
  const stop = () => {
    if (transfer?.restore) transfer.restore();
    if (transfer?.timer) clearTimeout(transfer.timer);
    transfer = null;
  };
  const fail = (operation, message) => {
    if (transfer !== operation) return;
    operation.error = message;
    clearTimeout(operation.timer);
    operation.restore?.();
  };
  const complete = operation => {
    if (transfer !== operation) return;
    operation.state = 'ready';
    clearTimeout(operation.timer);
    operation.restore?.();
  };
  const exportRow = () => Array.from(document.querySelectorAll('[role="menuitem"]')).find(element => {
    const label = element.querySelector('.mm-label') || element;
    return visible(element) && ['Export plan file', 'Export plan JSON', 'Export as JSON'].includes(text(label));
  });
  window.__gregShiftPlanTransfer = Object.freeze({
    cancel: stop,
    beginExport(token) {
      stop();
      transfer = { token, state: 'pending' };
      const operation = transfer;
      operation.timer = setTimeout(() => fail(operation, '等待原站导出超时，请重试。'), 12000);
      const previous = URL.createObjectURL;
      const capture = function(blob) {
        const address = previous.call(URL, blob);
        if (transfer === operation && blob instanceof Blob && blob.type.split(';')[0] === 'application/json') {
          if (blob.size > limit) fail(operation, '方案超过 10 MiB，未读取或保存。');
          else blob.text().then(json => {
            if (transfer === operation) { operation.json = json; complete(operation); }
          }).catch(() => fail(operation, '原站未能读取导出的方案。'));
        }
        return address;
      };
      URL.createObjectURL = capture;
      transfer.restore = () => { if (URL.createObjectURL === capture) URL.createObjectURL = previous; };
      const row = exportRow();
      if (row) row.click();
      else {
        const share = document.querySelector('[data-mb="share"][role="button"]');
        if (!share || !visible(share)) { fail(operation, '原站未提供方案导出菜单，请等待加载完成。'); return; }
        let clicked = false;
        const clickExport = () => {
          if (clicked || transfer !== operation) return;
          const option = exportRow();
          if (option) { clicked = true; observer.disconnect(); option.click(); }
        };
        const observer = new MutationObserver(clickExport);
        observer.observe(document.body, { childList: true, subtree: true });
        const originalRestore = transfer.restore;
        transfer.restore = () => { observer.disconnect(); originalRestore(); };
        share.click();
        // React 可以同步插入菜单；不依赖之后恰好还会发生一次 DOM 变更。
        clickExport();
      }
    },
    beginImport(token, json) {
      stop();
      const input = document.querySelector('input[type="file"][data-plan-file-input]');
      if (!input) throw new Error('原站未提供方案导入入口，请等待加载完成。');
      if (new TextEncoder().encode(json).length > limit) throw new Error('方案超过 10 MiB。');
      // 原站只规范化方案名的首尾空白；匹配其显示名，不改用户文件。
      const name = JSON.parse(json).name.trim();
      const matchingTabs = () => tabs().filter(tab => tab.textContent.trim() === name).length;
      const before = matchingTabs();
      transfer = { token, state: 'pending', before, matchingTabs, kind: 'import' };
      const operation = transfer;
      operation.timer = setTimeout(() => fail(operation, '原站尚未确认导入，请查看网页中的提示；原有方案仍保留。'), 45000);
      const files = new DataTransfer();
      files.items.add(new File([json], 'gregshift-plan.json', { type: 'application/json' }));
      input.files = files.files;
      input.dispatchEvent(new Event('change', { bubbles: true }));
    },
    poll(token) {
      if (!transfer || transfer.token !== token) return { state: 'error', error: '文件操作已取消。' };
      if (transfer.error) return { state: 'error', error: transfer.error };
      if (transfer.kind === 'import' && transfer.matchingTabs() > transfer.before) complete(transfer);
      return { state: transfer.state, json: transfer.json || null };
    }
  });
  return true;
})()
