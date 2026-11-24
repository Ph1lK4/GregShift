using EML.App.Controls;

namespace EML.App.Tests;

/// <summary>持久回归约束；只读取内嵌脚本，不启动网页、不依赖 Node 或玩家数据。</summary>
public sealed class GtnhPlannerTransferContractTests
{
    [Theory]
    [InlineData("new TextEncoder().encode(json).length > limit")]
    [InlineData("blob.size > limit")]
    [InlineData("blob.type.split(';')[0] === 'application/json'")]
    [InlineData("transfer.token !== token")]
    [InlineData("transfer === operation")]
    [InlineData("cancel: stop")]
    [InlineData("clearTimeout(transfer.timer)")]
    [InlineData("observer.disconnect()")]
    [InlineData("URL.createObjectURL = previous")]
    public void TransferKeepsBoundsTokenIsolationAndCleanup(string requirement) =>
        Assert.Contains(requirement, GtnhPlannerResources.Read("planner-transfer.js"));

    [Theory]
    [InlineData("fetch(")]
    [InlineData("XMLHttpRequest")]
    [InlineData("indexedDB")]
    [InlineData("localStorage")]
    [InlineData("sessionStorage")]
    [InlineData("document.cookie")]
    [InlineData("chrome.webview")]
    [InlineData("useFactoryStore")]
    [InlineData("useDesignStore")]
    [InlineData("showOpenFilePicker")]
    [InlineData("showSaveFilePicker")]
    public void AdaptationDoesNotAddNetworkingHostFileAccessOrPrivateStoreInspection(string forbidden) =>
        Assert.DoesNotContain(forbidden, GtnhPlannerResources.Read("planner-transfer.js"));

    [Fact]
    public void JsonComesFromThePublicFileControlsAndTranslatedMenusKeepOriginalLabels()
    {
        var script = GtnhPlannerResources.Read("planner-transfer.js");
        Assert.Contains("[data-mb=\"share\"][role=\"button\"]", script);
        Assert.Contains("data-plan-file-input", script);
        Assert.Contains("gregshiftOriginalText", script);
        Assert.Contains("files.items.add(new File([json]", script);
        Assert.Contains("input.dispatchEvent(new Event('change'", script);
        Assert.Contains("'Export plan file'", script);
    }
}
