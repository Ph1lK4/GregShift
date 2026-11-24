using System.Text.Json;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.Tests;

/// <summary>内嵌工具只使用目录元数据；未创建的页面和外部网页边界不得因刷新而消失。</summary>
public sealed class GtnhToolsCatalogTests
{
    [Fact]
    public void SnapshotKeepsCreatedAndMissingPagesInSourceOrder()
    {
        var charts = GtnhToolsCatalog.FlowCharts;
        Assert.Equal(104, charts.Count);
        Assert.Equal(85, charts.Count(chart => chart.IsCreated));
        Assert.Equal(19, charts.Count(chart => !chart.IsCreated));
        Assert.Equal("轻燃油", charts[0].Name);
        Assert.True(charts[0].IsCreated);
        Assert.Equal(charts.Count, charts.Select(chart => chart.Id).Distinct().Count());
        Assert.All(charts, chart => Assert.True(GtnhToolNavigationPolicy.IsFlowChartPage(chart.Address)));
        Assert.All(charts, chart => Assert.False(string.IsNullOrWhiteSpace(chart.Section)));
        Assert.Equal("未创建，等待更新", Assert.Single(charts, chart => chart.Name == "纳米萃取树脂").StatusText);
        Assert.Contains(charts, chart => chart.Name == "乙基黄原酸钾" && !chart.IsCreated);
        Assert.Contains(charts, chart => chart.Name == "钢" && !chart.IsCreated);
        Assert.Contains(charts, chart => chart.Name == "石油化工概览" && chart.IsCreated);
        Assert.Equal("模板:FlowChart/独居石/铪锆", Assert.Single(charts, chart => chart.Name == "独居石/铪锆").PageTitle);
        Assert.True(charts.ToList().FindIndex(chart => chart.Name == "钢") > charts.ToList().FindIndex(chart => chart.Name == "永恒"));
    }

    [Theory]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/轻燃油")]
    [InlineData("https://gtnhquestsbook.top/")]
    [InlineData("https://gtnhquestsbook.top:443/?version=2.9.0#quest")]
    [InlineData("https://gtnhplanner.com/")]
    public void ConfiguredHttpsSitesMayOpenInsideReader(string address) =>
        Assert.True(GtnhToolNavigationPolicy.IsAllowed(new Uri(address)));

    [Theory]
    [InlineData("http://gtnh.huijiwiki.com/wiki/模板:FlowChart/轻燃油")]
    [InlineData("https://gtnh.huijiwiki.com.evil.example/wiki/模板:FlowChart/轻燃油")]
    [InlineData("https://gtnhquestsbook.top.evil.example/")]
    [InlineData("https://gtnhplanner.com.evil.example/")]
    [InlineData("https://gtnhquestsbook.top@evil.example/")]
    [InlineData("https://evil.example@gtnhquestsbook.top/")]
    [InlineData("https://gtnhquestsbook.top:8443/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("file:///C:/Users/player/options.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("ms-appinstaller:?source=https://example.com/app.msix")]
    public void UntrustedNavigationIsRejected(string address) =>
        Assert.False(GtnhToolNavigationPolicy.IsAllowed(new Uri(address)));

    [Fact]
    public void NullAndRelativeNavigationIsRejected()
    {
        Assert.False(GtnhToolNavigationPolicy.IsAllowed(null));
        Assert.False(GtnhToolNavigationPolicy.IsAllowed(new Uri("/wiki/模板:FlowChart/轻燃油", UriKind.Relative)));
    }

    [Theory]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart")]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/")]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/../../Other")]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/%252e%252e/Other")]
    [InlineData("https://gtnh.huijiwiki.com/wiki/Other/轻燃油")]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/轻燃油?action=edit")]
    [InlineData("https://gtnhquestsbook.top/wiki/模板:FlowChart/轻燃油")]
    public void NonChartPathsCannotEnterDirectory(string address) =>
        Assert.False(GtnhToolNavigationPolicy.IsFlowChartPage(new Uri(address)));

    [Fact]
    public void DirectoryRefreshKeepsMissingEntriesAndDeduplicatesWithoutReordering()
    {
        var json = Serialize(
            new { name = "轻燃油", section = "工艺流程图", href = "/wiki/模板:FlowChart/轻燃油", isCreated = true },
            new { name = "乙基黄原酸钾", section = "工艺流程图", href = "/wiki/模板:FlowChart/乙基黄原酸钾", isCreated = false },
            new { name = "轻燃油", section = "重复分组", href = "/wiki/模板:FlowChart/轻燃油", isCreated = true });
        Assert.True(GtnhToolsCatalog.TryReadDirectoryJson(json, out var charts));
        Assert.Collection(charts,
            first => { Assert.Equal("轻燃油", first.Name); Assert.True(first.IsCreated); },
            second => { Assert.Equal("乙基黄原酸钾", second.Name); Assert.False(second.IsCreated); });
    }

    [Fact]
    public void MissingWikiEditLinksBecomeCanonicalReadOnlyMetadata()
    {
        var json = Serialize(new
        {
            name = "钢", section = "多选择 / 配方对比",
            href = "/index.php?title=%E6%A8%A1%E6%9D%BF:FlowChart/%E9%92%A2&action=edit&redlink=1", isCreated = false
        });
        Assert.True(GtnhToolsCatalog.TryReadDirectoryJson(json, out var charts));
        var chart = Assert.Single(charts);
        Assert.Equal("模板:FlowChart/钢", chart.PageTitle);
        Assert.Empty(chart.Address.Query);
        Assert.False(chart.IsCreated);
    }

    [Theory]
    [InlineData("/index.php?title=模板:FlowChart/钢&action=edit&redlink=1", true)]
    [InlineData("/index.php?title=模板:FlowChart/钢&action=delete&redlink=1", false)]
    [InlineData("/index.php?title=模板:FlowChart/钢&action=edit&redlink=1&redirect=https://evil.example", false)]
    [InlineData("/index.php?title=模板:FlowChart/钢&title=Other&action=edit&redlink=1", false)]
    [InlineData("/index.php?title=Other&action=edit&redlink=1", false)]
    [InlineData("https://evil.example/wiki/模板:FlowChart/钢", false)]
    public void NonDirectoryEditLinksCannotBeImported(string href, bool isCreated)
    {
        Assert.False(GtnhToolsCatalog.TryReadDirectoryJson(Serialize(new { name = "钢", section = "工艺流程图", href, isCreated }), out var charts));
        Assert.Empty(charts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[{\"name\":\"钢\",\"section\":\"目录\",\"href\":\"/wiki/模板:FlowChart/钢\",\"isCreated\":\"false\"}]")]
    [InlineData("[{\"name\":\"钢\",\"href\":\"/wiki/模板:FlowChart/钢\",\"isCreated\":false}]")]
    public void InvalidRefreshDoesNotProducePartialDirectory(string? json)
    {
        Assert.False(GtnhToolsCatalog.TryReadDirectoryJson(json, out var charts));
        Assert.Empty(charts);
    }

    [Fact]
    public void OneInvalidEntryRejectsWholeRefresh()
    {
        var json = Serialize(
            new { name = "轻燃油", section = "工艺流程图", href = "/wiki/模板:FlowChart/轻燃油", isCreated = true },
            new { name = "钢", section = "工艺流程图", href = "file:///C:/Windows/win.ini", isCreated = false });
        Assert.False(GtnhToolsCatalog.TryReadDirectoryJson(json, out var charts));
        Assert.Empty(charts);
    }

    [Fact]
    public void OversizedMetadataCannotReplaceSnapshot()
    {
        Assert.False(GtnhToolsCatalog.TryReadDirectoryJson(new string(' ', GtnhToolsCatalog.MaximumDirectoryJsonLength + 1), out _));
        var entry = new { name = "轻燃油", section = "工艺流程图", href = "/wiki/模板:FlowChart/轻燃油", isCreated = true };
        Assert.False(GtnhToolsCatalog.TryReadDirectoryJson(JsonSerializer.Serialize(Enumerable.Repeat(entry, GtnhToolsCatalog.MaximumDirectoryEntries + 1)), out _));
        Assert.False(GtnhToolsCatalog.TryReadDirectoryJson(Serialize(new { name = new string('字', 161), section = "目录", href = entry.href, isCreated = true }), out _));
    }

    [Fact]
    public void FixedToolSourcesPassNavigationPolicy()
    {
        Assert.True(GtnhToolNavigationPolicy.IsAllowed(GtnhToolsCatalog.FlowChartIndexUri));
        Assert.True(GtnhToolNavigationPolicy.IsAllowed(GtnhToolsCatalog.QuestBookUri));
        Assert.True(GtnhToolNavigationPolicy.IsPlanner(GtnhToolNavigationPolicy.PlannerUri));
        Assert.False(GtnhToolNavigationPolicy.IsPlanner(new Uri("https://gtnhplanner.com.evil.example/")));
    }

    private static string Serialize(params object[] entries) => JsonSerializer.Serialize(entries);
}
