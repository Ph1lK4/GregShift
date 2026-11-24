using EML.Core.Minecraft.Gtnh.Tools;
using Microsoft.Web.WebView2.Core;

namespace EML.App.Controls;

public sealed partial class GtnhToolBrowser
{
    private async Task FocusHandbookAsync(CoreWebView2 core, ulong navigation)
    {
        var generation = _readGeneration;
        bool IsCurrent() => !_disposed && IsActive && IsLoaded && !_loading && !_hasError &&
            navigation == _navigationId && generation == _readGeneration &&
            ReferenceEquals(core, _browser?.CoreWebView2) &&
            GtnhHandbookFocus.IsPage(RequestedAddress) && GtnhHandbookFocus.IsPage(core.Source);

        try
        {
            if (!IsCurrent()) return;
            var result = await core.ExecuteScriptAsync(GtnhHandbookFocus.Script);
            // 原站组件可能晚于 NavigationCompleted 初始化；只等待本次页面，切页后不再更新状态。
            for (var attempt = 0; attempt < 128 && IsCurrent(); attempt++)
            {
                if (result == "\"ready\"")
                {
                    Report("在线魔导手册：搜索研究、切换分类，点击节点可查看详情。内容由 GTNH 中文维基在线提供。");
                    return;
                }
                if (result != "\"pending\"") break;
                await Task.Delay(250);
                if (!IsCurrent()) return;
                result = await core.ExecuteScriptAsync("window.__gregShiftHandbookFocus?.state ?? 'unavailable'");
            }
            if (IsCurrent()) Report("原站暂未提供可显示的研究图。若页面要求验证，请先自行完成，再重新加载；未隐藏原页面。");
        }
        catch (Exception ex)
        {
            if (IsCurrent()) Report("魔导手册精简显示暂未就绪，请重新加载：" + ex.Message);
        }
    }
}

/// <summary>只聚焦原站研究组件，不复制节点、不改变研究数据或浏览器权限。</summary>
internal static class GtnhHandbookFocus
{
    internal static bool IsPage(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && GtnhToolNavigationPolicy.IsAllowed(uri) &&
        uri.IdnHost == "gtnh.huijiwiki.com" &&
        Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/') == "/wiki/研究";

    internal const string Script = """
        (() => {
          if (window.top !== window || location.origin !== 'https://gtnh.huijiwiki.com') return 'unsupported';
          try {
            if (decodeURIComponent(location.pathname).replace(/\/+$/, '') !== '/wiki/研究') return 'unsupported';
          } catch { return 'unsupported'; }
          if (window.__gregShiftHandbookFocus) return window.__gregShiftHandbookFocus.state;
          const state = { state: 'pending' };
          window.__gregShiftHandbookFocus = state;
          let observer = null, timer = null;
          const stop = () => {
            observer?.disconnect();
            clearTimeout(timer);
            window.removeEventListener('pagehide', cancel);
          };
          const cancel = () => { state.state = 'unavailable'; stop(); };
          const focus = () => {
            if (state.state !== 'pending') return;
            const root = document.querySelector('.gtnh-thaumonomicon-d3');
            if (!root || root.getAttribute('data-fcd3-state') !== 'ready' ||
                !root.querySelector('.qbd3-layout') || !root.querySelector('svg.tmd3-svg')) return;
            // 只在真正的交互组件就绪后精简；验证页或站点改版不会被遮成空白。
            const style = document.createElement('style');
            style.id = 'gregshift-handbook-focus-style';
            style.textContent = `
              [data-gregshift-handbook-ancestor] {
                position: static !important; transform: none !important; filter: none !important;
                perspective: none !important; contain: none !important; overflow: visible !important;
                display: block !important; min-width: 0 !important; max-width: none !important;
                min-height: 0 !important; height: auto !important; width: 100% !important;
                margin: 0 !important; padding: 0 !important; border: 0 !important; z-index: auto !important;
              }
              [data-gregshift-handbook-ancestor] > :not([data-gregshift-handbook-ancestor]):not([data-gregshift-handbook-root]):not(.qbd3-tooltip):not(script):not(style):not(link) {
                display: none !important;
              }
              html[data-gregshift-handbook-ancestor], html[data-gregshift-handbook-ancestor] > body {
                height: 100% !important; overflow: hidden !important;
              }
              [data-gregshift-handbook-root] {
                position: fixed !important; inset: 0 !important; box-sizing: border-box !important;
                width: 100% !important; height: 100% !important; min-width: 0 !important;
                min-height: 0 !important; max-width: none !important; max-height: none !important;
                margin: 0 !important; z-index: 1 !important;
              }
              [data-gregshift-handbook-root] > .qbd3-layout,
              [data-gregshift-handbook-root] .qbd3-viewport,
              [data-gregshift-handbook-root] svg.tmd3-svg {
                height: 100% !important; min-height: 0 !important;
              }
              body > .qbd3-tooltip { z-index: 1600 !important; }
            `;
            document.head.appendChild(style);
            root.setAttribute('data-gregshift-handbook-root', '');
            for (let parent = root.parentElement; parent; parent = parent.parentElement)
              parent.setAttribute('data-gregshift-handbook-ancestor', '');
            state.state = 'ready';
            stop();
            window.dispatchEvent(new Event('resize'));
          };
          observer = new MutationObserver(focus);
          observer.observe(document.documentElement, {
            childList: true, subtree: true, attributes: true, attributeFilter: ['data-fcd3-state']
          });
          timer = setTimeout(cancel, 30000);
          window.addEventListener('pagehide', cancel, { once: true });
          focus();
          return state.state;
        })()
        """;
}
