using System.IO;
using System.Text;
using EML.App.Controls;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.Tests;

/// <summary>只读取内置资源与测试临时文件，不初始化网页或使用玩家方案。</summary>
public sealed class GtnhPlannerTests
{
    private const string Plan = "{\"schemaVersion\":1,\"id\":\"test-plan\",\"name\":\"测试方案\",\"recipes\":[],\"nodes\":[],\"edges\":[],\"fuelProfiles\":[]}";

    [Theory]
    [InlineData("铁锭")]
    [InlineData("Iron Ingot")]
    [InlineData("  iron ingot  ")]
    public void ItemSearchReturnsBothChineseAndOriginalEnglishNames(string query)
    {
        var results = GtnhPlannerResources.Search(query);
        var iron = Assert.Single(results, item => item.English == "Iron Ingot");
        Assert.Equal("铁锭", iron.Chinese);
        Assert.Equal("铁锭 · Iron Ingot", iron.Label);
        Assert.Same(iron, results[0]);
    }

    [Fact]
    public void BroadSearchIsBoundedAndEmptyOrOversizedQueriesAreRejected()
    {
        Assert.Equal(60, GtnhPlannerResources.Search("a").Count);
        Assert.Empty(GtnhPlannerResources.Search(""));
        Assert.Empty(GtnhPlannerResources.Search(" \t\r\n"));
        Assert.Empty(GtnhPlannerResources.Search(new string('a', 129)));
        Assert.Empty(GtnhPlannerResources.Search("不会存在的测试物品编号-73246265"));
    }

    [Fact]
    public void BundledLocalizationKeepsAnExactOriginAndTopFrameGuard()
    {
        var script = GtnhPlannerResources.Script;
        Assert.Contains("window.top !== window.self", script);
        Assert.Contains("location.origin !== \"https://gtnhplanner.com\"", script);
        Assert.Contains("api.setLanguage", script);
        Assert.Contains("api.search", script);
        Assert.DoesNotContain("/*__NAMES__*/{}", script);
        Assert.DoesNotContain("/*__UI__*/{}", script);
        Assert.Contains("Iron Ingot", script);
        Assert.Contains("MIT License", GtnhPlannerResources.Read("LICENSE.txt"));
    }

    [Fact]
    public void BundledTransfersAreBoundedAndUseTheSitesPublicImportAndExportControls()
    {
        var script = GtnhPlannerResources.Read("planner-transfer.js");
        Assert.Contains("window !== window.top", script);
        Assert.Contains("location.origin !== 'https://gtnhplanner.com'", script);
        Assert.Contains("10 * 1024 * 1024", script);
        Assert.Contains("data-plan-file-input", script);
        Assert.Contains("beginImport(token, json)", script);
        Assert.Contains("beginExport(token)", script);
        Assert.Contains("poll(token)", script);
        Assert.DoesNotContain("localStorage", script);
        Assert.DoesNotContain("hostObjects", script);
        Assert.DoesNotContain("postMessage", script);
    }

    [Fact]
    public async Task ReadingAPlanReturnsTheExactOriginalBytes()
    {
        using var files = new TemporaryPlans();
        var bytes = Encoding.UTF8.GetBytes(Plan);
        await File.WriteAllBytesAsync(files.PathFor("中文方案.json"), bytes);
        Assert.Equal(bytes, await GtnhPlannerPane.ReadPlanAsync(files.PathFor("中文方案.json")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GtnhPlannerPlan.MaximumBytes + 1)]
    public async Task ReadingRejectsEmptyAndOversizedFilesBeforeAllocatingTheirContent(long size)
    {
        using var files = new TemporaryPlans();
        var path = files.PathFor("bounded.json");
        using (var stream = File.Create(path)) stream.SetLength(size);
        await Assert.ThrowsAsync<InvalidDataException>(() => GtnhPlannerPane.ReadPlanAsync(path));
    }

    [Fact]
    public async Task SavingAValidPlanRoundTripsAndLeavesNoTemporaryFile()
    {
        using var files = new TemporaryPlans();
        var path = files.PathFor("plan.json");
        var bytes = Encoding.UTF8.GetBytes(Plan);
        await GtnhPlannerPane.SavePlanAsync(path, bytes);
        Assert.Equal(bytes, await GtnhPlannerPane.ReadPlanAsync(path));
        var updated = Encoding.UTF8.GetBytes(Plan.Replace("测试方案", "更新后的方案", StringComparison.Ordinal));
        await GtnhPlannerPane.SavePlanAsync(path, updated);
        Assert.Equal(updated, await GtnhPlannerPane.ReadPlanAsync(path));
        Assert.Equal([path], Directory.GetFiles(files.Directory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":999,\"id\":\"test-plan\",\"name\":\"bad\",\"recipes\":[],\"nodes\":[],\"edges\":[],\"fuelProfiles\":[]}")]
    public async Task InvalidJsonNeverOverwritesAnExistingPlan(string invalid)
    {
        using var files = new TemporaryPlans();
        var path = files.PathFor("existing.json");
        var original = Encoding.UTF8.GetBytes(Plan);
        await File.WriteAllBytesAsync(path, original);
        await Assert.ThrowsAsync<InvalidDataException>(() => GtnhPlannerPane.SavePlanAsync(path, Encoding.UTF8.GetBytes(invalid)));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(files.Directory));
    }

    private sealed class TemporaryPlans : IDisposable
    {
        internal string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EML-planner-tests-" + Guid.NewGuid().ToString("N"));
        internal TemporaryPlans() => System.IO.Directory.CreateDirectory(Directory);
        internal string PathFor(string name) => System.IO.Path.Combine(Directory, name);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
