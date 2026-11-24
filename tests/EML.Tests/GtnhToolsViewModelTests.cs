using System.Text.Json;
using EML.App.ViewModels;
using EML.Core.Minecraft.Gtnh.Tools;
using EML.Core.Minecraft.Migration;

namespace EML.Tests;

/// <summary>资料页通过内存服务测试显式读取与取消，不联网或访问用户实例。</summary>
public sealed class GtnhToolsViewModelTests
{
    [Fact]
    public void InitialDirectoryKeepsCreatedAndPendingEntriesWithoutStartingAReader()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        Assert.False(model.IsActive); Assert.True(model.IsFlowCharts); Assert.True(model.IsChartList);
        Assert.False(model.IsChartDetail); Assert.Equal(104, model.Charts.Count);
        Assert.Equal(19, model.Charts.Count(chart => !chart.IsCreated));
        Assert.Null(model.SelectedChart); Assert.Null(model.RequestedAddress); Assert.Null(model.ChartContent);
        Assert.False(model.HasBrowser); Assert.Empty(service.Requests);
    }

    [Fact]
    public void PlannerHasItsOwnPageAndNeverRequestsTheSharedWikiOrQuestReader()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.ShowQuestBookCommand.Execute(null);
        Assert.True(model.HasBrowser);
        model.ShowPlannerCommand.Execute(null);
        Assert.Equal(2, model.SelectedToolIndex); Assert.True(model.IsPlanner);
        Assert.False(model.IsFlowCharts); Assert.False(model.IsQuestBook);
        Assert.False(model.IsChartList); Assert.False(model.IsChartDetail);
        Assert.False(model.HasBrowser); Assert.Null(model.RequestedAddress);
        Assert.False(model.IsLoading); Assert.Empty(model.BrowserStatus);
        Assert.Contains("GTNH Planner", model.SourceLabel); Assert.Empty(service.Requests);
    }

    [Theory]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/轻燃油")]
    [InlineData("https://gtnhquestsbook.top/")]
    [InlineData(null)]
    public void PlannerIgnoresLateSharedReaderStatusEvents(string? address)
    {
        using var model = new GtnhToolsViewModel(new ChartService());
        model.ShowFlowChartsCommand.Execute(null);
        model.ShowQuestBookCommand.Execute(null);
        model.ShowPlannerCommand.Execute(null);
        var revision = model.NavigationRevision;
        model.UpdateBrowserState(address, true, true, true, "旧阅读器迟到的错误", true, "旧阅读器状态");
        Assert.True(model.IsPlanner); Assert.Null(model.RequestedAddress);
        Assert.False(model.HasError); Assert.False(model.RuntimeMissing); Assert.False(model.IsLoading);
        Assert.False(model.CanGoBack); Assert.False(model.CanGoForward);
        Assert.Empty(model.BrowserStatus); Assert.Equal(revision, model.NavigationRevision);
    }

    [Fact]
    public async Task ReturningFromPlannerPreservesTheExistingChartAndDirectoryState()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service) { SelectedSection = "工艺流程图", SearchText = "轻燃油" };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Assert.Single(model.Charts);
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        var content = model.ChartContent;
        model.ShowPlannerCommand.Execute(null);
        Assert.False(model.HasGraph); Assert.False(model.HasBrowser);
        model.ShowFlowChartsCommand.Execute(null);
        Assert.True(model.IsChartDetail); Assert.True(model.HasGraph);
        Assert.Same(chart, model.SelectedChart); Assert.Same(content, model.ChartContent);
        Assert.Equal("轻燃油", model.SearchText); Assert.Equal("工艺流程图", model.SelectedSection);
        Assert.Same(chart, Assert.Single(model.Charts)); Assert.Single(service.Requests);
        model.BackToChartsCommand.Execute(null); model.ShowPlannerCommand.Execute(null); model.ShowFlowChartsCommand.Execute(null);
        Assert.True(model.IsChartList); Assert.Same(chart, model.SelectedChart);
        Assert.Single(service.Requests);
    }

    [Fact]
    public void ListSelectionAndFilteringNeverReadTheSelectedChart()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var selected = Chart(model, "轻燃油"); model.SelectedChart = selected;
        var revision = model.NavigationRevision;
        model.SearchText = " 轻燃油 ";
        Assert.Same(selected, Assert.Single(model.Charts)); Assert.Same(selected, model.SelectedChart);
        Assert.True(model.IsChartList); Assert.Equal(revision, model.NavigationRevision);
        Assert.Empty(service.Requests);
    }

    [Theory]
    [InlineData("钢")]
    [InlineData("乙基黄原酸钾")]
    [InlineData("纳米萃取树脂")]
    public void PendingEntryOpensNativePlaceholderWithoutRequestingTheWikiEditor(string name)
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var pending = Chart(model, name); var position = model.Charts.IndexOf(pending);
        model.OpenDetailsCommand.Execute(pending);
        Assert.True(model.IsChartDetail); Assert.True(model.IsMissingChart);
        Assert.False(model.HasBrowser); Assert.False(model.HasGraph); Assert.Null(model.RequestedAddress);
        Assert.Equal(name, model.ReaderTitle); Assert.Equal("未创建，等待更新", model.ChartStatus);
        Assert.Same(pending, model.Charts[position]); Assert.Empty(service.Requests);
        model.ViewOriginalCommand.Execute(null); Assert.False(model.HasBrowser);
    }

    [Fact]
    public void SearchAndSectionIntersectWithoutOpeningMatchingOrMissingEntries()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service)
            { SelectedSection = "多选择 / 配方对比", SearchText = " 钢 " };
        model.ShowFlowChartsCommand.Execute(null);
        Assert.Equal("钢", Assert.Single(model.Charts).Name); Assert.False(model.Charts[0].IsCreated);
        Assert.True(model.IsChartList); Assert.False(model.IsMissingChart);
        model.SelectedSection = "工艺流程图";
        Assert.Equal("钢氧产线", Assert.Single(model.Charts).Name); Assert.True(model.Charts[0].IsCreated);
        Assert.Null(model.SelectedChart); Assert.Empty(service.Requests);
    }

    [Fact]
    public void EmptySearchResultsClearHiddenListSelectionWithoutNavigating()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.SelectedChart = Chart(model, "轻燃油");
        model.SearchText = "完全不存在的流程图目录项";
        Assert.True(model.HasNoResults); Assert.Empty(model.Charts); Assert.Null(model.SelectedChart);
        Assert.Null(model.RequestedAddress); Assert.False(model.HasBrowser); Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task ExplicitDetailsLoadsDataLocallyAndExposesItsProvenance()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        var request = Assert.Single(service.Requests);
        Assert.Same(chart, request.Chart); Assert.False(request.Refresh);
        Assert.True(model.IsChartDetail); Assert.False(model.IsChartList);
        Assert.True(model.HasGraph); Assert.True(model.ShowCanvas); Assert.False(model.HasBrowser);
        Assert.Equal(chart.Address.AbsoluteUri, model.CurrentAddress);
        Assert.Equal("原料", model.CurrentView!.Nodes[0].Label);
        Assert.Contains("原站读取", model.SourceMetadata); Assert.Contains("原站修订 123", model.SourceMetadata);
        Assert.Contains(GtnhFlowChartContent.LicenseName, model.SourceMetadata);
    }

    [Fact]
    public async Task BackToListRetainsFiltersAndSelectedEntryWithoutReloading()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service)
            { SelectedSection = "工艺流程图", SearchText = "轻燃油" };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Assert.Single(model.Charts);
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        model.BackToChartsCommand.Execute(null);
        Assert.True(model.IsChartList); Assert.False(model.IsChartDetail);
        Assert.False(model.HasGraph); Assert.False(model.HasBrowser);
        Assert.Equal("工艺流程图", model.SelectedSection); Assert.Equal("轻燃油", model.SearchText);
        Assert.Same(chart, model.SelectedChart); Assert.Same(chart, Assert.Single(model.Charts));
        Assert.Single(service.Requests);
    }

    [Fact]
    public void UnknownEntriesCannotOpenOrReadData()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(new GtnhFlowChart("unknown", "外部条目", "目录", "模板:FlowChart/外部条目", true));
        Assert.True(model.IsChartList); Assert.Null(model.SelectedChart); Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task LaterChartSelectionCancelsAnEarlierLoadAndIgnoresItsLateResult()
    {
        var first = Pending(); var second = Pending();
        var service = new ChartService { Handler = request => request.Chart.Name == "轻燃油" ? first.Task : second.Task };
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var oldChart = Chart(model, "轻燃油"); var newChart = Chart(model, "聚乙烯");
        model.OpenDetailsCommand.Execute(oldChart); var oldLoad = model.ChartLoadTask;
        model.OpenDetailsCommand.Execute(newChart); var newLoad = model.ChartLoadTask;
        Assert.True(service.Requests[0].Token.IsCancellationRequested);
        second.SetResult(Content(newChart)); await newLoad;
        first.SetResult(Content(oldChart)); await oldLoad;
        Assert.Same(newChart, model.SelectedChart);
        Assert.Equal(newChart.Address.AbsoluteUri, model.ChartContent!.SourceAddress);
        Assert.False(model.IsLoadingChart); Assert.False(model.HasError);
    }

    [Theory]
    [InlineData("back")]
    [InlineData("quest")]
    [InlineData("planner")]
    [InlineData("inactive")]
    [InlineData("dispose")]
    public async Task LeavingAChartCancelsItsLoadAndIgnoresALateFailure(string action)
    {
        var pending = Pending();
        var service = new ChartService { Handler = _ => pending.Task };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "轻燃油")); var load = model.ChartLoadTask;
        if (action == "back") model.BackToChartsCommand.Execute(null);
        else if (action == "quest") model.ShowQuestBookCommand.Execute(null);
        else if (action == "planner") model.ShowPlannerCommand.Execute(null);
        else if (action == "inactive") model.IsActive = false;
        else model.Dispose();
        Assert.True(Assert.Single(service.Requests).Token.IsCancellationRequested);
        pending.SetException(new IOException("旧请求迟到的错误")); await load;
        Assert.Null(model.ChartContent); Assert.False(model.IsLoadingChart); Assert.False(model.HasError);
        if (action == "quest") Assert.Equal(GtnhToolsCatalog.QuestBookUri.AbsoluteUri, model.RequestedAddress);
    }

    [Fact]
    public async Task ReactivationRetriesAnInterruptedChartAndKeepsItsSelection()
    {
        var pending = Pending();
        var service = new ChartService { Handler = request => request.Index == 0 ? pending.Task : Task.FromResult(Content(request.Chart)) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); var interrupted = model.ChartLoadTask;
        model.IsActive = false; model.IsActive = true; await model.ChartLoadTask;
        pending.SetResult(Content(chart)); await interrupted;
        Assert.Equal(2, service.Requests.Count); Assert.Same(chart, model.SelectedChart);
        Assert.True(model.HasGraph); Assert.False(model.IsLoadingChart);
    }

    [Fact]
    public async Task RefreshRequestsFreshDataButDoesNotDuplicateAnInProgressRefresh()
    {
        var pending = Pending();
        var service = new ChartService { Handler = request => request.Refresh ? pending.Task : Task.FromResult(Content(request.Chart)) };
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "轻燃油")); await model.ChartLoadTask;
        var original = model.ChartContent;
        model.RefreshChartCommand.Execute(null); var refresh = model.ChartLoadTask;
        model.RefreshChartCommand.Execute(null);
        Assert.Equal(2, service.Requests.Count); Assert.True(service.Requests[1].Refresh);
        Assert.True(model.IsLoadingChart); Assert.Same(original, model.ChartContent);
        pending.SetResult(Content(service.Requests[1].Chart) with { RevisionId = 456 }); await refresh;
        Assert.Equal(456L, model.ChartContent!.RevisionId); Assert.False(model.IsLoadingChart);
    }

    [Fact]
    public async Task RefreshFailureKeepsThePreviouslyLoadedGraphAndReportsTheReason()
    {
        var service = new ChartService
        {
            Handler = request => request.Refresh ? Task.FromException<GtnhFlowChartContent>(new IOException("网络暂时不可用")) :
                Task.FromResult(Content(request.Chart))
        };
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "轻燃油")); await model.ChartLoadTask;
        var original = model.ChartContent;
        model.RefreshChartCommand.Execute(null); await model.ChartLoadTask;
        Assert.Same(original, model.ChartContent); Assert.True(model.HasGraph); Assert.True(model.HasError);
        Assert.False(model.ShowChartError); Assert.Contains("网络暂时不可用", model.Error);
        Assert.Contains("上次成功读取", model.ChartStatus);
    }

    [Fact]
    public async Task InitialFailureCanBeRetriedWithoutOpeningAnExternalPage()
    {
        var service = new ChartService
        {
            Handler = request => request.Index == 0 ? Task.FromException<GtnhFlowChartContent>(new InvalidDataException("图表数据无效")) :
                Task.FromResult(Content(request.Chart))
        };
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "轻燃油")); await model.ChartLoadTask;
        Assert.True(model.ShowChartError); Assert.Contains("图表数据无效", model.Error); Assert.False(model.HasBrowser);
        model.RefreshChartCommand.Execute(null); await model.ChartLoadTask;
        Assert.True(model.HasGraph); Assert.False(model.HasError); Assert.False(model.ShowChartError);
        Assert.True(service.Requests[1].Refresh);
    }

    [Fact]
    public async Task UnconfiguredServiceOffersARecoverableErrorInsteadOfStartingTheBrowser()
    {
        using var model = new GtnhToolsViewModel();
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "轻燃油")); await model.ChartLoadTask;
        Assert.True(model.ShowChartError); Assert.Contains("尚未配置", model.Error); Assert.False(model.HasBrowser);
        model.ConfigureFlowCharts(new ChartService());
        model.RefreshChartCommand.Execute(null); await model.ChartLoadTask;
        Assert.True(model.HasGraph); Assert.False(model.HasError);
    }

    [Fact]
    public async Task CachedWarningAndMetadataRemainVisibleWithoutBeingTreatedAsLoadFailure()
    {
        var service = new ChartService
        { Handler = request => Task.FromResult(Content(request.Chart) with { FromCache = true, RevisionId = null, Warning = "原站无法连接，保留缓存。" }) };
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "轻燃油")); await model.ChartLoadTask;
        Assert.Equal("原站无法连接，保留缓存。", model.ChartStatus);
        Assert.Contains("本机缓存", model.SourceMetadata); Assert.Contains("未提供修订号", model.SourceMetadata);
        Assert.True(model.HasGraph); Assert.False(model.HasError);
    }

    [Fact]
    public async Task LanguageSwitchKeepsMatchingGroupAndFallsBackWhenTheGroupDoesNotExist()
    {
        using var model = new GtnhToolsViewModel(new ChartService());
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "轻燃油")); await model.ChartLoadTask;
        Assert.True(model.HasEnglish); Assert.Equal("zh", model.CurrentView!.Language);
        Assert.Equal(3, model.Groups.Count); Assert.Null(model.HighlightGroup);
        model.SelectedGroup = model.Groups.Single(group => group.Id == "shared");
        Assert.Equal("shared", model.HighlightGroup);
        model.IsEnglish = true;
        Assert.Equal("en", model.CurrentView!.Language); Assert.Equal("Shared route", model.SelectedGroup!.Label);
        Assert.Equal("shared", model.HighlightGroup);
        model.IsEnglish = false; model.SelectedGroup = model.Groups.Single(group => group.Id == "zh-only");
        model.IsEnglish = true;
        Assert.Equal("", model.SelectedGroup!.Id); Assert.Null(model.HighlightGroup);
    }

    [Fact]
    public async Task OpeningAChineseOnlyChartResetsPreviousEnglishSelection()
    {
        var service = new ChartService
        {
            Handler = request => Task.FromResult(request.Chart.Name == "轻燃油" ? Content(request.Chart) :
                Content(request.Chart) with { Document = new GtnhFlowDocument([Views()[0]]) })
        };
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "轻燃油")); await model.ChartLoadTask;
        model.IsEnglish = true;
        model.OpenDetailsCommand.Execute(Chart(model, "聚乙烯")); await model.ChartLoadTask;
        Assert.False(model.HasEnglish); Assert.False(model.IsEnglish); Assert.Equal("zh", model.CurrentView!.Language);
    }

    [Fact]
    public async Task SourceAndOriginalViewsAreExplicitAndReturningUsesTheLoadedGraph()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        var content = model.ChartContent;
        model.ToggleSourceCommand.Execute(null);
        Assert.True(model.ShowSource); Assert.False(model.ShowCanvas);
        Assert.Equal(content!.PayloadJson, model.SourceText); Assert.Equal("返回交互图", model.SourceToggleLabel);
        model.ViewOriginalCommand.Execute(null);
        Assert.True(model.IsOriginalPage); Assert.True(model.HasBrowser); Assert.False(model.HasGraph);
        Assert.Contains("可能执行站点脚本", model.SourceLabel);
        Assert.DoesNotContain("不执行原站脚本", model.SourceLabel);
        Assert.False(model.ShowSource); Assert.Equal(chart.Address.AbsoluteUri, model.RequestedAddress);
        model.ReturnToLocalCommand.Execute(null);
        Assert.False(model.IsOriginalPage); Assert.False(model.HasBrowser); Assert.True(model.ShowCanvas);
        Assert.Contains("不执行原站脚本", model.SourceLabel);
        Assert.Same(content, model.ChartContent); Assert.Single(service.Requests);
    }

    [Fact]
    public async Task OriginalPageCancelsThePendingGraphAndReturningStartsANewLoad()
    {
        var pending = Pending();
        var service = new ChartService { Handler = request => request.Index == 0 ? pending.Task : Task.FromResult(Content(request.Chart)) };
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); var originalLoad = model.ChartLoadTask;
        model.ViewOriginalCommand.Execute(null);
        Assert.True(service.Requests[0].Token.IsCancellationRequested);
        model.ReturnToLocalCommand.Execute(null); await model.ChartLoadTask;
        pending.SetResult(Content(chart) with { RevisionId = 999 }); await originalLoad;
        Assert.Equal(2, service.Requests.Count); Assert.Equal(123L, model.ChartContent!.RevisionId);
        Assert.True(model.ShowCanvas); Assert.False(model.HasBrowser);
    }

    [Fact]
    public void RefreshOnlyUpdatesDirectoryAfterAnExplicitRequestAndNeverReadsCharts()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var json = Directory(("钢", true), ("铝", false));
        Assert.False(model.ApplyDirectoryJson(json)); Assert.Equal(104, model.Charts.Count);
        model.RefreshDirectoryCommand.Execute(null);
        Assert.True(model.IsUpdatingDirectory); Assert.False(model.IsChartList);
        Assert.Equal(GtnhToolsCatalog.FlowChartIndexUri.AbsoluteUri, model.RequestedAddress);
        Assert.True(model.ApplyDirectoryJson(json));
        Assert.True(model.IsChartList); Assert.False(model.IsUpdatingDirectory); Assert.False(model.HasBrowser);
        Assert.Equal(new[] { "钢", "铝" }, model.Charts.Select(chart => chart.Name));
        Assert.True(model.Charts[0].IsCreated); Assert.False(model.Charts[1].IsCreated);
        Assert.Contains("目录已更新", model.DirectoryStatus); Assert.Empty(service.Requests);
    }

    [Fact]
    public void DirectoryUpdateRetainsTheSelectedEntryAndItsNewPositionWithoutAutoLoading()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.SelectedChart = Chart(model, "钢"); model.RefreshDirectoryCommand.Execute(null);
        Assert.True(model.ApplyDirectoryJson(Directory(("铝", false), ("钢", true))));
        Assert.Equal("钢", model.SelectedChart?.Name);
        Assert.Equal(1, model.Charts.IndexOf(model.SelectedChart!)); Assert.True(model.SelectedChart!.IsCreated);
        Assert.True(model.IsChartList); Assert.False(model.HasBrowser); Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task DirectoryUpdateReplacesAnOpenPlaceholderWithItsNewlyCreatedGraph()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "钢"));
        Assert.True(model.IsMissingChart);
        model.RefreshDirectoryCommand.Execute(null);

        Assert.True(model.ApplyDirectoryJson(Directory(("铝", false), ("钢", true))));
        await model.ChartLoadTask;

        Assert.Equal("钢", model.SelectedChart!.Name); Assert.True(model.SelectedChart.IsCreated);
        Assert.Equal(1, model.Charts.IndexOf(model.SelectedChart));
        Assert.False(model.IsMissingChart); Assert.True(model.HasGraph); Assert.False(model.HasBrowser);
        Assert.Single(service.Requests); Assert.DoesNotContain("未创建", model.ChartStatus);
    }

    [Fact]
    public async Task DirectoryUpdateToMissingClearsTheOldGraphAndOriginalPage()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        model.OpenDetailsCommand.Execute(Chart(model, "轻燃油")); await model.ChartLoadTask;
        model.ViewOriginalCommand.Execute(null); model.RefreshDirectoryCommand.Execute(null);

        Assert.True(model.ApplyDirectoryJson(Directory(("轻燃油", false))));

        Assert.True(model.IsMissingChart); Assert.False(model.HasGraph); Assert.Null(model.ChartContent);
        Assert.False(model.IsOriginalPage); Assert.False(model.HasBrowser);
        Assert.Equal("未创建，等待更新", model.ChartStatus); Assert.Single(service.Requests);
    }

    [Fact]
    public async Task FailedDirectoryUpdatePreservesTheOpenGraphAndItsReadingState()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        var content = model.ChartContent;
        model.ToggleSourceCommand.Execute(null); model.RefreshDirectoryCommand.Execute(null);

        Assert.False(model.ApplyDirectoryJson("[]"));

        Assert.Same(chart, model.SelectedChart); Assert.Same(content, model.ChartContent);
        Assert.True(model.IsChartDetail); Assert.True(model.ShowSource); Assert.False(model.HasBrowser);
        Assert.Single(service.Requests);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("[{\"name\":\"恶意地址\",\"section\":\"工艺流程图\",\"href\":\"file:///C:/private\",\"isCreated\":true}]")]
    public void InvalidDirectoryPreservesTheOriginalDirectoryAndReturnsToTheList(string json)
    {
        using var model = new GtnhToolsViewModel();
        model.ShowFlowChartsCommand.Execute(null);
        var original = model.Charts.ToArray(); model.RefreshDirectoryCommand.Execute(null);
        Assert.False(model.ApplyDirectoryJson(json));
        Assert.Equal(original, model.Charts); Assert.Contains("保留原目录", model.DirectoryStatus);
        Assert.True(model.IsChartList); Assert.False(model.HasBrowser);
    }

    [Theory]
    [InlineData("quest")]
    [InlineData("cancel")]
    public void DirectoryResponseArrivingAfterItsReaderIsClosedIsIgnored(string action)
    {
        using var model = new GtnhToolsViewModel();
        model.ShowFlowChartsCommand.Execute(null);
        model.RefreshDirectoryCommand.Execute(null);
        if (action == "quest") model.ShowQuestBookCommand.Execute(null);
        else model.CancelDirectoryUpdateCommand.Execute(null);
        Assert.False(model.ApplyDirectoryJson(Directory(("钢", true))));
        Assert.Equal(104, model.Charts.Count); Assert.False(model.IsUpdatingDirectory);
        if (action == "quest") Assert.Equal(GtnhToolsCatalog.QuestBookUri.AbsoluteUri, model.RequestedAddress);
        else { Assert.True(model.IsChartList); Assert.Null(model.RequestedAddress); }
    }

    [Fact]
    public void RefreshWhileAlreadyAtIndexCreatesANewNavigationRequest()
    {
        using var model = new GtnhToolsViewModel();
        model.ShowFlowChartsCommand.Execute(null);
        model.RefreshDirectoryCommand.Execute(null); var revision = model.NavigationRevision;
        model.RefreshDirectoryCommand.Execute(null);
        Assert.Equal(revision + 1, model.NavigationRevision);
        Assert.Equal(GtnhToolsCatalog.FlowChartIndexUri.AbsoluteUri, model.RequestedAddress);
    }

    [Fact]
    public void BrowserFailureDoesNotRemoveDirectoryAndExplicitCancelClearsItsError()
    {
        using var model = new GtnhToolsViewModel();
        model.ShowFlowChartsCommand.Execute(null);
        model.RefreshDirectoryCommand.Execute(null);
        model.UpdateBrowserState(model.RequestedAddress, false, false, false, "缺少运行时", true);
        Assert.True(model.HasError); Assert.True(model.RuntimeMissing); Assert.Equal(104, model.Charts.Count);
        Assert.Contains("现有目录仍保留", model.DirectoryStatus);
        model.CancelDirectoryUpdateCommand.Execute(null);
        Assert.True(model.IsChartList); Assert.False(model.HasError);
        Assert.False(model.RuntimeMissing); Assert.False(model.HasBrowser);
    }

    [Fact]
    public async Task QuestModeRestoresItsAddressAndReturnsToTheSameLocalChart()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        var content = model.ChartContent;
        model.ShowQuestBookCommand.Execute(null);
        Assert.True(model.IsQuestBook); Assert.False(model.IsChartList); Assert.False(model.IsChartDetail);
        Assert.Equal(GtnhToolsCatalog.QuestBookUri.AbsoluteUri, model.RequestedAddress);
        var quest = "https://gtnhquestsbook.top/#quests/example";
        model.UpdateBrowserState(quest, false, true, false, null, false);
        model.ShowFlowChartsCommand.Execute(null);
        Assert.True(model.IsChartDetail); Assert.Same(content, model.ChartContent);
        Assert.Same(chart, model.SelectedChart); Assert.Null(model.RequestedAddress); Assert.Single(service.Requests);
        model.ShowQuestBookCommand.Execute(null); Assert.Equal(quest, model.RequestedAddress);
    }

    [Fact]
    public async Task QuestAndOriginalPageRestoreIndependentlyWithoutAcceptingCrossSiteEvents()
    {
        using var model = new GtnhToolsViewModel(new ChartService());
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        model.ViewOriginalCommand.Execute(null); model.ShowQuestBookCommand.Execute(null);
        model.UpdateBrowserState(chart.Address.AbsoluteUri, false, true, false, "旧图表错误", true);
        Assert.Equal(GtnhToolsCatalog.QuestBookUri.AbsoluteUri, model.CurrentAddress); Assert.False(model.HasError);
        model.ShowFlowChartsCommand.Execute(null);
        Assert.Equal(chart.Address.AbsoluteUri, model.RequestedAddress);
        model.UpdateBrowserState(GtnhToolsCatalog.QuestBookUri.AbsoluteUri, false, true, false, "旧任务书错误", true);
        Assert.Equal(chart.Address.AbsoluteUri, model.CurrentAddress); Assert.False(model.HasError);
    }

    [Theory]
    [InlineData("file:///C:/private")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://gtnhquestsbook.top/")]
    [InlineData("https://gtnhquestsbook.top.evil.test/")]
    [InlineData("https://user@gtnhquestsbook.top/")]
    [InlineData("https://gtnhquestsbook.top:444/")]
    public void ActiveBrowserNeverExposesAnUntrustedAddressOrItsError(string address)
    {
        using var model = new GtnhToolsViewModel();
        model.ShowFlowChartsCommand.Execute(null);
        model.ShowQuestBookCommand.Execute(null); var original = model.CurrentAddress;
        model.UpdateBrowserState(address, false, true, true, "不可信页面错误", true);
        Assert.Equal(original, model.CurrentAddress); Assert.False(model.HasError);
        Assert.False(model.CanGoBack); Assert.False(model.CanGoForward); Assert.False(model.RuntimeMissing);
    }

    [Fact]
    public void StaleBrowserEventsCannotReplaceANativePlaceholder()
    {
        using var model = new GtnhToolsViewModel();
        model.ShowFlowChartsCommand.Execute(null);
        model.ShowQuestBookCommand.Execute(null); var previous = model.RequestedAddress;
        model.ShowFlowChartsCommand.Execute(null); model.OpenDetailsCommand.Execute(Chart(model, "钢"));
        model.UpdateBrowserState(previous, false, true, true, "旧页错误", true);
        Assert.True(model.IsMissingChart); Assert.Null(model.RequestedAddress);
        Assert.False(model.HasError); Assert.False(model.CanGoBack); Assert.False(model.RuntimeMissing);
    }

    [Fact]
    public async Task StopWaitsForTheCanceledLoadWithoutApplyingItsResult()
    {
        var pending = Pending();
        var service = new ChartService { Handler = _ => pending.Task };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油"); model.OpenDetailsCommand.Execute(chart);
        var stop = model.StopAsync();
        Assert.True(Assert.Single(service.Requests).Token.IsCancellationRequested); Assert.False(stop.IsCompleted);
        pending.SetResult(Content(chart)); await stop;
        Assert.False(model.IsActive); Assert.False(model.IsLoadingChart); Assert.Null(model.ChartContent);
    }

    [Fact]
    public void DisposedModelCannotStartANewChartRead()
    {
        var service = new ChartService();
        var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null); var chart = Chart(model, "轻燃油");
        model.Dispose(); model.OpenDetailsCommand.Execute(chart); model.RefreshDirectoryCommand.Execute(null);
        Assert.Empty(service.Requests); Assert.False(model.IsDetailOpen);
        Assert.False(model.IsUpdatingDirectory); Assert.Null(model.RequestedAddress);
    }

    [Fact]
    public async Task ExplicitPageDataImportReturnsToNativeGraphAndNeverReadsAnotherChart()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        model.ViewOriginalCommand.Execute(null);
        model.ImportPageData("other", new("payload", 456));
        Assert.Empty(service.Imports);
        model.ImportPageData(chart.Id, new("payload", 456)); await model.ChartLoadTask;
        Assert.Single(service.Imports); Assert.Equal(456, model.ChartContent!.RevisionId);
        Assert.True(model.ShowCanvas); Assert.False(model.IsViewingOriginal); Assert.Null(model.RequestedAddress);
    }

    [Fact]
    public async Task QuestModeCannotImportDataFromThePreviouslyOpenOriginalChart()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service);
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        model.ViewOriginalCommand.Execute(null); model.ShowQuestBookCommand.Execute(null);
        Assert.False(model.IsViewingOriginal);
        model.ImportPageData(chart.Id, new("payload", 456)); model.ReturnToLocalCommand.Execute(null);
        Assert.Empty(service.Imports); Assert.Equal(GtnhToolsCatalog.QuestBookUri.AbsoluteUri, model.RequestedAddress);
    }

    [Fact]
    public async Task ForbiddenInitialReadAutomaticallyOpensTheSameOriginalPageWithoutAnError()
    {
        var service = new ChartService { Handler = _ => Task.FromException<GtnhFlowChartContent>(BrowserRequired()) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");

        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;

        Assert.True(model.IsViewingOriginal); Assert.True(model.IsAutomaticPageRead); Assert.True(model.HasBrowser);
        Assert.Equal(chart.Address.AbsoluteUri, model.RequestedAddress);
        Assert.False(model.HasError); Assert.Empty(model.Error); Assert.False(model.IsLoadingChart);
        Assert.Contains("原站", model.BrowserStatus); Assert.Contains("读取", model.BrowserStatus);
        Assert.Null(model.ChartContent); Assert.Single(service.Requests); Assert.Empty(service.Imports);
    }

    [Fact]
    public async Task ForbiddenRefreshPreservesTheTrustedCachedContentWhileOpeningItsOriginalPage()
    {
        GtnhFlowChartContent? cached = null;
        var service = new ChartService
        {
            Handler = request =>
            {
                if (request.Refresh) return Task.FromException<GtnhFlowChartContent>(BrowserRequired(cached));
                cached = Content(request.Chart) with { FromCache = true, RevisionId = 654 };
                return Task.FromResult(cached);
            }
        };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;

        model.RefreshChartCommand.Execute(null); await model.ChartLoadTask;

        Assert.Same(cached, model.ChartContent); Assert.True(model.IsAutomaticPageRead); Assert.True(model.IsViewingOriginal);
        Assert.False(model.HasError); Assert.Equal(chart.Address.AbsoluteUri, model.RequestedAddress);
        Assert.Contains("本机缓存", model.SourceMetadata); Assert.Equal(654, model.ChartContent!.RevisionId);
        model.ReturnToLocalCommand.Execute(null);
        Assert.Same(cached, model.ChartContent); Assert.True(model.ShowCanvas); Assert.False(model.HasBrowser);
        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri)); Assert.Equal(2, service.Requests.Count);
    }

    [Fact]
    public async Task ForbiddenFirstLoadCanExposeCachedContentCarriedByTheRecoveryException()
    {
        GtnhFlowChartContent? cached = null;
        var service = new ChartService
        {
            Handler = request => Task.FromException<GtnhFlowChartContent>(BrowserRequired(
                cached = Content(request.Chart) with { FromCache = true }))
        };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;

        Assert.Same(cached, model.ChartContent); Assert.True(model.IsViewingOriginal); Assert.True(model.IsAutomaticPageRead);
        model.ReturnToLocalCommand.Execute(null);
        Assert.True(model.HasGraph); Assert.Same(cached, model.ChartContent); Assert.Single(service.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/聚乙烯")]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart")]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/轻燃油#other")]
    [InlineData("https://gtnh.huijiwiki.com/wiki/模板:FlowChart/轻燃油?oldid=123")]
    [InlineData("https://gtnhquestsbook.top/")]
    [InlineData("https://gtnh.huijiwiki.com.evil.test/wiki/模板:FlowChart/轻燃油")]
    [InlineData("file:///C:/private")]
    [InlineData("javascript:alert(1)")]
    public async Task AutomaticPageReadRejectsAnUnrelatedAddressWithoutConsumingItsOneShot(string? address)
    {
        var service = new ChartService { Handler = _ => Task.FromException<GtnhFlowChartContent>(BrowserRequired()) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        model.UpdateBrowserState(chart.Address.AbsoluteUri, false, false, false, null, false);

        Assert.False(model.TryBeginAutomaticPageRead(address));
        Assert.True(model.IsAutomaticPageRead);
        Assert.True(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
        Assert.False(model.IsAutomaticPageRead);
        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
        Assert.Single(service.Requests); Assert.Empty(service.Imports);
    }

    [Fact]
    public async Task AutomaticPageReadWaitsForBrowserCompletionAndDoesNotConsumeWhileAChartIsLoading()
    {
        var service = new ChartService { Handler = _ => Task.FromException<GtnhFlowChartContent>(BrowserRequired()) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;

        Assert.True(model.IsLoading);
        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri)); Assert.True(model.IsAutomaticPageRead);
        model.UpdateBrowserState(chart.Address.AbsoluteUri, false, false, false, null, false);
        model.IsLoadingChart = true;
        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri)); Assert.True(model.IsAutomaticPageRead);
        model.IsLoadingChart = false;
        Assert.True(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
    }

    [Fact]
    public async Task AutomaticPageReadDoesNotConsumeOrLoopWhenWebViewRuntimeIsUnavailable()
    {
        var service = new ChartService { Handler = _ => Task.FromException<GtnhFlowChartContent>(BrowserRequired()) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        var revision = model.NavigationRevision;
        model.UpdateBrowserState(chart.Address.AbsoluteUri, false, false, false, "未安装 WebView2 运行时，请先安装后重试。", true);

        Assert.True(model.RuntimeMissing); Assert.True(model.HasError); Assert.Contains("WebView2", model.BrowserStatus);
        for (var index = 0; index < 3; index++)
            Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
        Assert.Equal(revision, model.NavigationRevision); Assert.Single(service.Requests); Assert.Empty(service.Imports);
        Assert.Equal(chart.Address.AbsoluteUri, model.RequestedAddress);
        model.BackToChartsCommand.Execute(null);
        Assert.True(model.IsChartList); Assert.False(model.HasBrowser); Assert.False(model.HasError);
    }

    [Fact]
    public async Task AutomaticPageReadDoesNotConsumeWhileThePageHasAnErrorAndCanProceedAfterExplicitRecovery()
    {
        var service = new ChartService { Handler = _ => Task.FromException<GtnhFlowChartContent>(BrowserRequired()) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        model.UpdateBrowserState(chart.Address.AbsoluteUri, false, false, false, "网页加载失败", false);

        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri)); Assert.True(model.IsAutomaticPageRead);
        model.UpdateBrowserState(chart.Address.AbsoluteUri, false, false, false, null, false);
        Assert.True(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri)); Assert.False(model.IsAutomaticPageRead);
    }

    [Theory]
    [InlineData("back")]
    [InlineData("manual")]
    [InlineData("quest")]
    [InlineData("inactive")]
    [InlineData("dispose")]
    public async Task LeavingOrManuallyOpeningTheOriginalPageDoesNotTriggerAnAutomaticRead(string action)
    {
        var service = new ChartService { Handler = _ => Task.FromException<GtnhFlowChartContent>(BrowserRequired()) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        model.UpdateBrowserState(chart.Address.AbsoluteUri, false, false, false, null, false);
        if (action == "back") model.BackToChartsCommand.Execute(null);
        else if (action == "manual") model.ViewOriginalCommand.Execute(null);
        else if (action == "quest") model.ShowQuestBookCommand.Execute(null);
        else if (action == "inactive") model.IsActive = false;
        else model.Dispose();
        if (action == "manual") model.UpdateBrowserState(chart.Address.AbsoluteUri, false, false, false, null, false);

        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
        Assert.Single(service.Requests); Assert.Empty(service.Imports);
    }

    [Fact]
    public async Task AutomaticallyReadPageDataReturnsToTheNativeGraphWithoutAnotherNetworkLoad()
    {
        var service = new ChartService { Handler = _ => Task.FromException<GtnhFlowChartContent>(BrowserRequired()) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        model.UpdateBrowserState(chart.Address.AbsoluteUri, false, false, false, null, false);
        Assert.True(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
        var revision = model.NavigationRevision;

        model.ImportPageData(chart.Id, new("page payload", 789), revision); await model.ChartLoadTask;

        Assert.Single(service.Imports); Assert.Single(service.Requests);
        Assert.True(model.ShowCanvas); Assert.False(model.IsViewingOriginal); Assert.False(model.IsAutomaticPageRead);
        Assert.False(model.HasBrowser); Assert.Null(model.RequestedAddress); Assert.False(model.HasError);
        Assert.Equal(789, model.ChartContent!.RevisionId); Assert.Equal("page payload", model.SourceText);
        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
    }

    [Fact]
    public async Task PageImportCannotUseAnOldNavigationRevisionEvenForTheSameChart()
    {
        var service = new ChartService();
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        var original = model.ChartContent;
        model.ViewOriginalCommand.Execute(null); var oldRevision = model.NavigationRevision;
        model.ViewOriginalCommand.Execute(null); var currentRevision = model.NavigationRevision;
        Assert.NotEqual(oldRevision, currentRevision);

        model.ImportPageData(chart.Id, new("late payload", 999), oldRevision);

        Assert.Empty(service.Imports); Assert.Same(original, model.ChartContent); Assert.True(model.IsViewingOriginal);
        model.ImportPageData(chart.Id, new("current payload", 456), currentRevision); await model.ChartLoadTask;
        Assert.Single(service.Imports); Assert.Equal(456, model.ChartContent!.RevisionId); Assert.True(model.ShowCanvas);
    }

    [Fact]
    public async Task ForbiddenPageImportReportsAnErrorWithoutStartingAnotherAutomaticBrowserCycle()
    {
        var service = new ChartService
        {
            Handler = _ => Task.FromException<GtnhFlowChartContent>(BrowserRequired()),
            ImportHandler = (_, _) => Task.FromException<GtnhFlowChartContent>(BrowserRequired())
        };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;
        model.UpdateBrowserState(chart.Address.AbsoluteUri, false, false, false, null, false);
        Assert.True(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
        var revision = model.NavigationRevision;

        model.ImportPageData(chart.Id, new("invalid page payload", 123), revision); await model.ChartLoadTask;

        Assert.True(model.HasError); Assert.True(model.IsViewingOriginal); Assert.False(model.IsAutomaticPageRead);
        Assert.Equal(revision, model.NavigationRevision); Assert.Single(service.Requests); Assert.Single(service.Imports);
        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("quest")]
    [InlineData("back")]
    [InlineData("inactive")]
    [InlineData("dispose")]
    public async Task LateForbiddenResponseCannotNavigateAfterTheReaderChanged(string action)
    {
        var pending = Pending();
        var service = new ChartService
        { Handler = request => request.Index == 0 ? pending.Task : Task.FromResult(Content(request.Chart)) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); var previousLoad = model.ChartLoadTask;
        if (action == "selection") { model.OpenDetailsCommand.Execute(Chart(model, "聚乙烯")); await model.ChartLoadTask; }
        else if (action == "quest") model.ShowQuestBookCommand.Execute(null);
        else if (action == "back") model.BackToChartsCommand.Execute(null);
        else if (action == "inactive") model.IsActive = false;
        else model.Dispose();
        var revision = model.NavigationRevision;
        var address = model.RequestedAddress;
        var content = model.ChartContent;

        pending.SetException(BrowserRequired(Content(chart) with { FromCache = true })); await previousLoad;

        Assert.Equal(revision, model.NavigationRevision); Assert.Equal(address, model.RequestedAddress);
        Assert.Same(content, model.ChartContent); Assert.False(model.IsAutomaticPageRead); Assert.False(model.HasError);
        Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
        if (action == "selection") Assert.Equal("聚乙烯", model.SelectedChart!.Name);
        if (action == "quest") Assert.Equal(GtnhToolsCatalog.QuestBookUri.AbsoluteUri, model.RequestedAddress);
    }

    [Fact]
    public async Task OrdinaryIoFailureDoesNotAutomaticallyOpenTheOriginalPage()
    {
        var service = new ChartService { Handler = _ => Task.FromException<GtnhFlowChartContent>(new IOException("离线测试错误")) };
        using var model = new GtnhToolsViewModel(service) { IsActive = true };
        model.ShowFlowChartsCommand.Execute(null);
        var chart = Chart(model, "轻燃油");
        model.OpenDetailsCommand.Execute(chart); await model.ChartLoadTask;

        Assert.True(model.ShowChartError); Assert.Contains("离线测试错误", model.Error);
        Assert.False(model.IsAutomaticPageRead); Assert.False(model.IsViewingOriginal); Assert.False(model.HasBrowser);
        Assert.Null(model.RequestedAddress); Assert.False(model.TryBeginAutomaticPageRead(chart.Address.AbsoluteUri));
        Assert.Single(service.Requests);
    }

    private static GtnhFlowBrowserRequiredException BrowserRequired(GtnhFlowChartContent? cached = null) =>
        new(cached, new HttpRequestException("原站返回 403", null, System.Net.HttpStatusCode.Forbidden));

    private static GtnhFlowChart Chart(GtnhToolsViewModel model, string name) => model.Charts.Single(chart => chart.Name == name);
    private static TaskCompletionSource<GtnhFlowChartContent> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static GtnhFlowChartContent Content(GtnhFlowChart chart) => new(new GtnhFlowDocument(Views()),
        "{\"safe\":\"declarative data\"}", chart.Address.AbsoluteUri, 123,
        new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero), false);

    private static IReadOnlyList<GtnhFlowView> Views() =>
    [
        new("zh", "测试流程图", [new("input", "原料", "item", 0, 0), new("machine", "机器", "recipe", 200, 0)],
            [new("edge", "input", "machine", "input", 0)],
            [new("shared", "公共路线", ["input", "machine"], ["edge"]), new("zh-only", "中文路线", ["machine"], [])]),
        new("en", "Test chart", [new("input", "Input", "item", 0, 0), new("machine", "Machine", "recipe", 200, 0)],
            [new("edge", "input", "machine", "input", 0)], [new("shared", "Shared route", ["input", "machine"], ["edge"])])
    ];

    private static string Directory(params (string Name, bool Created)[] entries) => JsonSerializer.Serialize(
        entries.Select(entry => new
        {
            name = entry.Name, section = "多选择 / 配方对比",
            href = "/wiki/模板:FlowChart/" + entry.Name, isCreated = entry.Created
        }));

    private sealed record ChartRequest(GtnhFlowChart Chart, bool Refresh, CancellationToken Token, int Index);

    /// <summary>默认立即返回；竞争测试显式控制完成源，不等待真实时间或网络。</summary>
    private sealed class ChartService : IGtnhFlowChartService
    {
        public List<ChartRequest> Requests { get; } = [];
        public List<GtnhFlowPageData> Imports { get; } = [];
        public Func<GtnhFlowChart, GtnhFlowPageData, Task<GtnhFlowChartContent>>? ImportHandler { get; init; }
        public Task<GtnhFlowChartContent> ImportPageDataAsync(GtnhFlowChart chart, string payloadJson, long? revisionId, CancellationToken cancellationToken = default)
        {
            var data = new GtnhFlowPageData(payloadJson, revisionId);
            Imports.Add(data);
            return ImportHandler?.Invoke(chart, data) ?? Task.FromResult(Content(chart) with { PayloadJson = payloadJson, RevisionId = revisionId });
        }
        public Func<ChartRequest, Task<GtnhFlowChartContent>>? Handler { get; init; }
        public Task<GtnhFlowChartContent> LoadAsync(GtnhFlowChart chart, bool refresh = false, CancellationToken cancellationToken = default)
        {
            var request = new ChartRequest(chart, refresh, cancellationToken, Requests.Count);
            Requests.Add(request); return Handler?.Invoke(request) ?? Task.FromResult(Content(chart));
        }
    }
}

public sealed partial class GtnhHubViewModelTests
{
    [Fact]
    public async Task ToolsRemainAvailableWithoutAnInstanceAndDuringInstanceOperations()
    {
        await using var app = new Harness();
        var inspections = 0;
        app.Services.InspectGtnh = (_, _) => { inspections++; return Task.FromResult<GtnhInstanceInfo?>(null); };
        app.Instances.Instances.Clear(); app.Instances.SelectedInstance = null;
        app.Instances.IsBusy = true;
        app.Main.SelectedPage = 9;

        Assert.True(app.Main.IsGtnhToolsPage);
        Assert.False(app.Main.IsGtnhPage);
        Assert.Same(app.Hub.Tools, app.Main.Tools);
        Assert.True(app.Main.Tools!.IsActive);
        Assert.False(app.Hub.HasTarget);
        Assert.False(app.Hub.CanInteract);
        Assert.True(app.Hub.Tools.ShowQuestBookCommand.CanExecute(null));
        app.Hub.Tools.ShowQuestBookCommand.Execute(null);
        Assert.Equal(GtnhToolsCatalog.QuestBookUri.AbsoluteUri, app.Hub.Tools.RequestedAddress);
        Assert.Equal(0, app.Addons.Reads);
        Assert.Equal(0, app.Addons.Installs);
        Assert.Equal(0, inspections);

        app.Main.SelectedPage = 0;
        Assert.False(app.Hub.Tools.IsActive);
        app.Instances.IsBusy = false;
    }

    [Fact]
    public async Task ToolsNavigationDoesNotReuseTheHubTargetScanAndRetainsReadingState()
    {
        await using var app = new Harness();
        var inspections = 0;
        app.Services.InspectGtnh = (_, _) => { inspections++; return Task.FromResult<GtnhInstanceInfo?>(null); };
        app.Main.SelectedPage = 9;
        app.Main.Tools!.SearchText = "轻燃油";
        Assert.Equal(0, inspections);
        app.Main.SelectedPage = 3;
        Assert.False(app.Main.Tools.IsActive);
        app.Main.SelectedPage = 9;
        Assert.True(app.Main.Tools.IsActive);
        Assert.Equal("轻燃油", app.Main.Tools.SearchText);
        Assert.Equal(0, inspections);

        app.Main.SelectedPage = 4;
        await app.Hub.TargetLoadTask;
        Assert.False(app.Main.Tools.IsActive);
        Assert.Equal(3, inspections);
        app.Main.SelectedPage = 9;
        Assert.True(app.Main.Tools.IsActive);
        Assert.Equal(3, inspections);
        app.Main.Dispose();
        Assert.False(app.Main.Tools.IsActive);
    }
}
