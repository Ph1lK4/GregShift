// Bundled, reviewed presentation adapter. Dictionaries: NanShang126/gtnh-planner-zh (MIT).
// Never changes a recipe, an input value, a user label, or a planner store.
(() => {
  "use strict";
  if (window.top !== window.self || location.origin !== "https://gtnhplanner.com") return false;
  if (window.__gregShiftPlanner?.localizationVersion === 1) return true;

  const names = /*__NAMES__*/{};
  const ui = Object.assign({}, /*__UI__*/{}, {
    "GTNH Planner": "GTNH 产线规划器", "Planner": "产线规划器",
    "New plan": "新建方案", "Plans": "方案库", "Your plans": "我的方案",
    "Feedback": "反馈", "Settings": "设置", "OVERVIEW": "总览",
    "Undo": "撤销", "Redo": "重做", "Arrange": "自动排列", "Fit": "适应窗口",
    "Annotations": "标注", "Non-recipe": "非配方机器", "Avg": "平均",
    "No machines yet.": "尚未添加机器。", "New from the community": "社区最新方案",
    "The community database is not responding. Try again in a few minutes.": "社区数据库暂时没有响应，请稍后重试。",
    "Draw a GregTech: New Horizons factory as a flowchart. Every card is a real recipe, every wire carries a real rate, and the board counts the machines you need.": "把 GTNH 工厂画成产线图：查看真实配方、物料流量，以及所需的机器数量。",
    "Loading recipe index...": "正在加载配方索引…", "Recipe index is not loaded yet.": "配方索引尚未加载。",
    "Nothing placed on the board yet.": "画布上尚无物品。", "No crop grows that.": "没有可产出此物品的作物。",
    "No bee makes that.": "没有可产出此物品的蜜蜂。",
    "No item has all those words. Showing the closest.": "没有同时匹配全部关键词的物品，正在显示近似结果。",
    "Search...": "搜索…", "Search…": "搜索…", "Search items and fluids": "搜索物品与流体",
    "Find a plan": "查找方案", "Search setups (#tag, @name)": "搜索社区方案（#标签、@作者）",
    "MY PLANS": "我的方案", "SHARED WITH ME": "他人分享", "COMMUNITY": "社区",
    "Any tier": "不限电压等级", "Top voted": "最多赞同", "Newest": "最新发布", "Recently updated": "最近更新",
    "Import a plan": "导入方案", "Import a plan...": "导入方案…", "Import plan file": "导入方案文件",
    "Export plan file": "导出方案文件", "Export plan JSON": "导出方案 JSON", "Export as JSON": "导出 JSON",
    "Copy plan link": "复制方案链接", "Copy plan code": "复制方案代码", "Invite people...": "邀请协作…",
    "Screenshot...": "截取画布…", "Open in the game": "在游戏中打开", "Post a copy to Community...": "发布副本至社区…",
    "Cancel": "取消", "Close": "关闭", "Done": "完成", "Apply": "应用", "Reset": "重置",
    "Delete": "删除", "Duplicate": "创建副本", "Rename": "重命名", "Name": "名称",
    "Description": "描述", "Copy": "复制", "Paste": "粘贴", "Clear": "清空",
    "Recipes": "配方", "Uses": "用途", "Recipe": "配方", "Machine": "机器",
    "Input": "输入", "Output": "输出", "Forced": "强制供给", "Any": "不限", "Set": "设置",
    "At least": "至少", "Exactly": "恰好", "At most": "至多", "Up to": "最多",
    "Reload": "重新加载", "Retry": "重试", "Create": "创建", "Save": "保存",
    "Dismiss": "关闭提示", "Not now": "暂时不用", "Take the tour": "查看入门引导",
    "New to GTNH Planner?": "第一次使用 GTNH 产线规划器？",
    "A quick tour shows you around in about three minutes.": "用大约三分钟了解规划器的基本操作。",
    "is now in the game!": "现在也能在游戏中使用！",
    "Plan your factories right from NEI, just like the site. Check out the video!": "在 NEI 中规划工厂，操作与网站相似。可查看演示视频了解详情。",
    "All tiers": "全部电压等级", "Takes": "消耗", "Makes": "产出", "Only": "仅匹配",
    "Power": "功率", "not used up": "不消耗"
  });

  // Plan tabs, library names and notes belong to the user, including when a name
  // happens to equal a dictionary key. Only known, generated UI regions opt in.
  const protectedSelector = "script,style,noscript,template,input,textarea,select,[contenteditable]," +
    "[role='textbox'],[role='tab']:not(.ml-tab),.sn,.sn-text,.mt-name,.mt-tab,.mb-tabs," +
    ".ml-name,.ml-title,.ml-row-name,.ml-body,.pn-skin section button,.annotation,.react-flow__node-note,.react-flow__node-annotation";
  const uiSelector = "button,[role='button'],[role='menuitem'],[role='menuitemcheckbox']," +
    "[role='menuitemradio'],label,h1,h2,h3,.rail-head-title,.rail-sec-title,.rail-empty," +
    ".rail-strip-label,.nei-empty,.nei-note,.ml-head,.ml-tab,.tp-label,.tp-action,.tp-note,.pn-skin header p,.pn-skin section > p";
  const resourceSelector = ".cc-pname,.cc-name,.rail-name,.nei-name,[data-resource-name]";
  const originals = new WeakMap();
  const attributes = new WeakMap();
  let chinese = true;
  let timer = 0;
  let observing = false;
  const dirty = new Set();

  const own = (dictionary, key) => Object.prototype.hasOwnProperty.call(dictionary, key);
  const isProtected = element => !element || !!element.closest(protectedSelector);
  function resourceText(element, source) {
    if (!own(names, source) || !element.closest(resourceSelector)) return null;
    if (element.closest(".cc-pname,[data-resource-name],.nei-name")) return names[source];
    // Machine headings and overview group labels can be user-authored. Require
    // an adjacent authoritative image name before translating those regions.
    const row = element.closest(".cc-name") ? element.closest(".cc-card") : element.closest(".cc-row,.cc-head,.rail-row,[role='option']");
    if (!row) return null;
    for (const image of row.querySelectorAll("img[alt]")) {
      const record = attributes.get(image)?.get("alt");
      if ((record?.source ?? image.getAttribute("alt")) === source) return names[source];
    }
    return null;
  }

  function translateText(node) {
    const element = node.parentElement;
    if (isProtected(element)) return;
    let previous = originals.get(node);
    const current = node.nodeValue || "";
    // A React update is a new source, not a new English word translated from a
    // stale snapshot. Keep the same Text object so React can still remove it.
    const source = previous && current === previous.output ? previous.source : current;
    const trimmed = source.trim();
    if (!trimmed) return;
    const key = trimmed.replace(/\s+/g, " ");
    let translated = resourceText(element, key);
    if (translated === null && own(ui, key) && element.closest(uiSelector)) {
      translated = ui[key];
    }
    const output = chinese && translated ? source.replace(trimmed, translated) : source;
    if (output !== current) node.nodeValue = output;
    // Preserve the original label for the narrowly scoped public export menu.
    if (translated && element.matches('.mm-label')) element.dataset.gregshiftOriginalText = key;
    if (translated || previous) originals.set(node, { source, output });
  }

  function translateAttributes(element) {
    if (element.closest("script,style,[contenteditable],.sn,.sn-text,[role='tab']:not(.ml-tab)")) return;
    let records = attributes.get(element);
    for (const attribute of ["placeholder", "alt", "title"]) {
      const current = element.getAttribute(attribute);
      if (current === null) continue;
      const previous = records?.get(attribute);
      const source = previous && current === previous.output ? previous.source : current;
      // Placeholder is presentation only; never translate input.value. Image
      // alt names are planner-owned resources; no general aria-label rewriting
      // so the host can continue targeting stable English control labels.
      let translated = null;
      if (attribute === "placeholder" && own(ui, source)) translated = ui[source];
      else if (attribute === "alt" && element.tagName === "IMG" && own(names, source)) translated = names[source];
      else if (attribute === "title" && own(ui, source) && element.matches("button,[role='button']")) translated = ui[source];
      const output = chinese && translated ? translated : source;
      if (current !== output) element.setAttribute(attribute, output);
      if (translated || previous) {
        if (!records) { records = new Map(); attributes.set(element, records); }
        records.set(attribute, { source, output });
      }
    }
  }

  function scan() {
    timer = 0;
    if (document.hidden || !document.body) return;
    // Disconnect while writing; own mutations must not schedule another pass.
    observer.disconnect(); observing = false;
    const roots = [...dirty]; dirty.clear();
    for (const root of roots) {
      if (!root.isConnected || roots.some(other => other !== root && other.contains(root))) continue;
      if (root.nodeType === Node.TEXT_NODE) { translateText(root); continue; }
      const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
      while (walker.nextNode()) translateText(walker.currentNode);
      if (root.nodeType === Node.ELEMENT_NODE) translateAttributes(root);
      for (const element of root.querySelectorAll("[placeholder],img[alt],button[title],[role='button'][title]")) translateAttributes(element);
    }
    observe();
  }
  function schedule() {
    if (!timer && !document.hidden) timer = window.setTimeout(scan, 80);
  }
  const observer = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === "childList") {
        for (const node of record.addedNodes) if (node.nodeType === Node.TEXT_NODE || node.nodeType === Node.ELEMENT_NODE) dirty.add(node);
      } else dirty.add(record.target);
    }
    schedule();
  });
  function observe() {
    if (!document.body || document.hidden || observing) return;
    observer.observe(document.body, { childList: true, subtree: true, characterData: true,
      attributes: true, attributeFilter: ["placeholder", "alt", "title"] });
    observing = true;
  }
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
      observer.disconnect(); observing = false;
      if (timer) window.clearTimeout(timer);
      timer = 0;
    } else { dirty.add(document.body); observe(); schedule(); }
  });

  const api = window.__gregShiftPlanner || {};
  api.localizationVersion = 1;
  api.setLanguage = enabled => { chinese = enabled === true; dirty.add(document.body); scan(); return true; };
  api.search = english => {
    if (typeof english !== "string" || !english.trim() || english.length > 128) return false;
    const input = document.querySelector("input[aria-label='Search items and fluids']");
    if (!input || input.disabled) return false;
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    if (!setter) return false;
    setter.call(input, english);
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.focus();
    return true;
  };
  window.__gregShiftPlanner = api;
  if (document.body) { dirty.add(document.body); scan(); observe(); }
  else document.addEventListener("DOMContentLoaded", () => { dirty.add(document.body); scan(); observe(); }, { once: true });
  return true;
})()
