using System.IO;
using System.Text;
using System.Text.Json;
using EML.Core.Minecraft.Gtnh.Tools;
using Microsoft.Web.WebView2.Core;

namespace EML.App.Controls;

public sealed partial class GtnhToolBrowser
{
    private bool _plannerTransferActive;
    private bool _plannerChinese = true;

    internal async Task InitializePlannerAsync()
    {
        var core = RequirePlanner(allowLoading: true);
        await core.ExecuteScriptAsync(GtnhPlannerResources.Script);
        await core.ExecuteScriptAsync(GtnhPlannerResources.Read("planner-transfer.js"));
        await SetPlannerLanguageAsync(_plannerChinese);
        Report("方案由规划器保存在本机独立浏览器目录；重要方案请另行导出 JSON 备份。");
    }

    internal async Task SetPlannerLanguageAsync(bool chinese)
    {
        _plannerChinese = chinese;
        if (_browser?.CoreWebView2 is not { } core || !PlannerMode || _loading) return;
        await core.ExecuteScriptAsync($"window.__gregShiftPlanner?.setLanguage({(chinese ? "true" : "false")})");
    }

    internal async Task SearchPlannerAsync(string english)
    {
        if (string.IsNullOrWhiteSpace(english) || english.Length > 128) throw new InvalidDataException("请选择有效物品。");
        var core = RequirePlanner();
        var result = await core.ExecuteScriptAsync("window.__gregShiftPlanner?.search(" + JsonSerializer.Serialize(english) + ") === true");
        if (result != "true") throw new InvalidOperationException("物品搜索框还未就绪。请等待配方数据加载，或在规划器内展开右侧物品栏。");
    }

    internal async Task ImportPlannerAsync(ReadOnlyMemory<byte> utf8)
    {
        GtnhPlannerPlan.Validate(utf8);
        var bytes = utf8.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? utf8[3..] : utf8;
        await TransferPlannerAsync("beginImport", Encoding.UTF8.GetString(bytes.Span));
    }

    internal async Task<byte[]> ExportPlannerAsync()
    {
        var json = await TransferPlannerAsync("beginExport");
        var bytes = Encoding.UTF8.GetBytes(json ?? throw new InvalidDataException("原站没有返回方案 JSON。"));
        GtnhPlannerPlan.Validate(bytes);
        return bytes;
    }

    private CoreWebView2 RequirePlanner(bool allowLoading = false)
    {
        if (_disposed || !PlannerMode || !IsActive || !IsLoaded || (!allowLoading && (_loading || _hasError)) ||
            _browser?.CoreWebView2 is not { } core || !Uri.TryCreate(core.Source, UriKind.Absolute, out var address) ||
            !GtnhToolNavigationPolicy.IsPlanner(address))
            throw new InvalidOperationException("请先打开并等待产线规划器加载完成。");
        return core;
    }

    private async Task<string?> TransferPlannerAsync(string method, string? importJson = null)
    {
        if (_plannerTransferActive) throw new InvalidOperationException("已有方案文件操作正在进行。");
        var core = RequirePlanner();
        var navigation = _navigationId;
        var generation = _readGeneration;
        var token = Guid.NewGuid().ToString("N");
        _plannerTransferActive = true;
        try
        {
            var arguments = JsonSerializer.Serialize(token) + (importJson is null ? "" : "," + JsonSerializer.Serialize(importJson));
            var started = await core.ExecuteScriptAsync("(() => { try { if (!window.__gregShiftPlanTransfer) return '规划器文件适配尚未就绪，请刷新。'; window.__gregShiftPlanTransfer." +
                method + "(" + arguments + "); return null; } catch (e) { return String(e.message); } })()");
            if (started != "null") throw new InvalidDataException(JsonSerializer.Deserialize<string>(started) ?? "文件操作未能开始。");
            for (var attempt = 0; attempt < 240; attempt++)
            {
                await Task.Delay(200);
                if (!ReferenceEquals(RequirePlanner(), core) || navigation != _navigationId || generation != _readGeneration)
                    throw new InvalidOperationException("页面已切换，方案文件操作已停止。原有方案未被覆盖。");
                var result = await core.ExecuteScriptAsync("window.__gregShiftPlanTransfer.poll(" + JsonSerializer.Serialize(token) + ")");
                // 脚本返回也可能晚于切页；不能把旧页面的成功当成本次结果。
                if (!ReferenceEquals(RequirePlanner(), core) || navigation != _navigationId || generation != _readGeneration)
                    throw new InvalidOperationException("页面已切换，方案文件操作已停止。原有方案未被覆盖。");
                if (result.Length > GtnhPlannerPlan.MaximumBytes * 6L + 1024) throw new InvalidDataException("方案数据超出传输上限。");
                using var document = JsonDocument.Parse(result);
                var root = document.RootElement;
                if (root.GetProperty("state").GetString() == "error")
                    throw new InvalidDataException(root.GetProperty("error").GetString());
                if (root.GetProperty("state").GetString() == "ready")
                    return root.TryGetProperty("json", out var data) ? data.GetString() : null;
            }
            throw new TimeoutException("原站未确认方案文件操作，请查看网页中的提示后重试。");
        }
        finally
        {
            _plannerTransferActive = false;
            if (!_disposed && ReferenceEquals(_browser?.CoreWebView2, core) && navigation == _navigationId)
            {
                try { await core.ExecuteScriptAsync("window.__gregShiftPlanTransfer?.cancel()"); }
                catch (Exception) { /* 浏览器关闭或导航不能阻塞文件操作的结束。 */ }
            }
        }
    }
}
