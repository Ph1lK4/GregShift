using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.ViewModels;

/// <summary>资料库独立于游戏实例；只解析图表数据，任务书仍使用隔离网页。</summary>
public sealed partial class GtnhToolsViewModel : ObservableObject, IDisposable
{
    private IReadOnlyList<GtnhFlowChart> _directory = GtnhToolsCatalog.FlowCharts;
    private IGtnhFlowChartService? _flowCharts;
    private CancellationTokenSource? _loadCancellation;
    private int _loadRevision;
    private bool _disposed;
    private readonly Dictionary<int, string> _onlineAddresses = [];

    public GtnhToolsViewModel(IGtnhFlowChartService? flowCharts = null)
    { _flowCharts = flowCharts; ApplyFilter(); }

    public ObservableCollection<GtnhFlowChart> Charts { get; } = [];
    public IReadOnlyList<GtnhToolEntry> Tools => GtnhToolDirectory.Entries;
    public ObservableCollection<GtnhFlowGroup> Groups { get; } = [];
    public string? BrowserDataFolder { get; set; }
    public string? ToolsDataFolder { get; set; }
    public Task ChartLoadTask { get; private set; } = Task.CompletedTask;
    public IReadOnlyList<string> Sections => ["全部目录", .. _directory.Select(chart => chart.Section).Distinct()];
    public void ConfigureFlowCharts(IGtnhFlowChartService service) => _flowCharts = service;

    [ObservableProperty] private int selectedToolIndex;
    [ObservableProperty] private bool isToolDirectory = true;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string selectedSection = "全部目录";
    [ObservableProperty] private GtnhFlowChart? selectedChart;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private bool isDetailOpen;
    [ObservableProperty] private bool isOriginalPage;
    [ObservableProperty] private bool isAutomaticPageRead;
    [ObservableProperty] private bool isSourceVisible;
    [ObservableProperty] private bool isEnglish;
    [ObservableProperty] private bool isLoadingChart;
    [ObservableProperty] private bool isUpdatingDirectory;
    [ObservableProperty] private GtnhFlowChartContent? chartContent;
    [ObservableProperty] private GtnhFlowGroup? selectedGroup;
    [ObservableProperty] private string chartStatus = "选择流程图查看详情，首次读取后可离线查看。";
    [ObservableProperty] private string? requestedAddress;
    [ObservableProperty] private int navigationRevision;
    [ObservableProperty] private string currentAddress = "";
    [ObservableProperty] private string browserStatus = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool canGoBack;
    [ObservableProperty] private bool canGoForward;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool runtimeMissing;
    [ObservableProperty] private string directoryStatus = $"内置目录 · {GtnhToolsCatalog.SnapshotDate}；可联网更新。";

    public bool IsFlowCharts => SelectedToolIndex == 0;
    public bool IsQuestBook => SelectedToolIndex == 1;
    public bool IsPlanner => SelectedToolIndex == 2;
    public bool IsOreRegex => SelectedToolIndex == 3;
    public bool IsResearchHelper => SelectedToolIndex == 4;
    public bool IsSeedMap => SelectedToolIndex == 5;
    public bool IsThaumonomicon => SelectedToolIndex == 6;
    public bool IsToolWorkspace => !IsToolDirectory;
    public GtnhToolEntry SelectedTool => Tools.FirstOrDefault(tool => tool.Index == SelectedToolIndex) ?? Tools[0];
    public bool ShowBrowserAddress => IsViewingOriginal || IsUpdatingDirectory;
    public string OnlineNotice => IsSeedMap ? "按实际存档选择世界生成器；结构和矿脉为预测结果。不会自动读取存档。网页导出或文件导入请在外部浏览器完成。" :
        IsThaumonomicon ? "研究资料由中文维基提供；请核对整合包版本。不会读取或同步你的研究进度。" : "";
    public bool IsChartList => !IsToolDirectory && IsFlowCharts && !IsDetailOpen && !IsUpdatingDirectory;
    public bool IsChartDetail => !IsToolDirectory && IsFlowCharts && IsDetailOpen && !IsUpdatingDirectory;
    public bool IsViewingOriginal => IsChartDetail && IsOriginalPage;
    public bool IsMissingChart => IsChartDetail && SelectedChart is { IsCreated: false };
    public bool HasNoResults => Charts.Count == 0;
    public bool HasGraph => IsChartDetail && ChartContent is not null && !IsOriginalPage;
    public bool ShowCanvas => HasGraph && !IsSourceVisible;
    public bool ShowSource => HasGraph && IsSourceVisible;
    public bool HasBrowser => RequestedAddress is not null;
    public bool HasError => Error.Length > 0;
    public bool ShowChartError => IsChartDetail && HasError && !HasGraph && !HasBrowser;
    public bool HasEnglish => ChartContent?.Document.Views.Any(view => view.Language == "en") == true;
    public GtnhFlowView? CurrentView => ChartContent?.Document.Views.FirstOrDefault(view => view.Language == (IsEnglish ? "en" : "zh"))
        ?? ChartContent?.Document.Views.FirstOrDefault();
    public string? HighlightGroup => string.IsNullOrEmpty(SelectedGroup?.Id) ? null : SelectedGroup.Id;
    public string ReaderTitle => IsUpdatingDirectory ? "更新流程图目录" : !IsFlowCharts ? SelectedTool.Name : SelectedChart?.Name ?? "工艺流程图";
    public string DirectorySummary => $"{Charts.Count} / {_directory.Count} 项 · 保留未创建条目";
    public string SourceToggleLabel => IsSourceVisible ? "返回交互图" : "查看图表数据";
    public string SourceText => ChartContent?.PayloadJson ?? "";
    public string SourceLabel => IsToolDirectory ? "本地工具无需读取实例；在线工具由对应站点提供，使用前请核对整合包版本。" :
        IsOreRegex ? GtnhNativeToolData.OreSourceLabel + "；适用路线请核对 NEI 配方。" :
        IsResearchHelper ? GtnhNativeToolData.ResearchSourceLabel + "；只计算连接链，不修改游戏研究。" :
        IsSeedMap ? "来源：Chunkbase GTNH；预测结果仅供参考，不保证与所有存档生成结果一致。" :
        IsThaumonomicon ? "来源：GTNH 中文维基 · 研究一览；不与游戏研究进度同步。" :
        IsPlanner ? "在线工具：GTNH Planner · 中文词典：gtnh-planner-zh；配方与译名请核对整合包版本。" :
        IsQuestBook ? "来源：GTNH 在线任务书；不与游戏内任务进度同步。" :
        IsViewingOriginal || IsUpdatingDirectory ? "原站网页由 GTNH 中文维基提供，可能执行站点脚本；不会读取游戏数据。" :
        "图表数据来自 GTNH 中文维基贡献者；由 GregShift 本地绘制，不执行原站脚本。";
    public string SourceMetadata => ChartContent is { } content ?
        $"{(content.FromCache ? "本机缓存" : "原站读取")} · {content.RetrievedAt.ToLocalTime():yyyy-MM-dd HH:mm}" +
        (content.RevisionId is { } revision ? $" · 原站修订 {revision}" : " · 原站未提供修订号") +
        $"\n{GtnhFlowChartContent.LicenseName}" : "";

    [RelayCommand] private void ShowFlowCharts() => SelectTool(0);
    [RelayCommand] private void ShowQuestBook() => SelectTool(1);
    [RelayCommand] private void ShowPlanner() => SelectTool(2);
    [RelayCommand] private void OpenTool(GtnhToolEntry? tool)
    { if (tool is not null && Tools.Contains(tool)) SelectTool(tool.Index); }
    private void SelectTool(int index)
    {
        if (_disposed || !Tools.Any(tool => tool.Index == index)) return;
        IsToolDirectory = false;
        if (SelectedToolIndex == index) OnSelectedToolIndexChanged(index);
        else SelectedToolIndex = index;
    }
    [RelayCommand] private void BackToTools()
    { CancelLoad(); IsUpdatingDirectory = false; IsToolDirectory = true; Request(null); NotifyReader(); }
    [RelayCommand] private void ToggleSource() => IsSourceVisible = !IsSourceVisible;

    /// <summary>列表选择不触发下载；仅明确打开已创建条目时读取资料。</summary>
    [RelayCommand]
    private void OpenDetails(GtnhFlowChart? chart)
    {
        if (_disposed || chart is null || !_directory.Any(item => item.Id == chart.Id)) return;
        IsToolDirectory = false;
        if (!IsFlowCharts) SelectedToolIndex = 0;
        CancelLoad();
        SelectedChart = chart; IsDetailOpen = true; IsOriginalPage = false; IsSourceVisible = false;
        ChartContent = null; Error = "";
        Request(null); CurrentAddress = chart.Address.AbsoluteUri;
        if (!chart.IsCreated) ChartStatus = "未创建，等待更新";
        else ChartLoadTask = LoadChartAsync(false);
        NotifyReader();
    }

    [RelayCommand]
    private void BackToCharts()
    {
        CancelLoad(); IsDetailOpen = false; IsOriginalPage = false; IsSourceVisible = false;
        Request(null); Error = ""; NotifyReader();
    }

    [RelayCommand]
    private void RefreshChart()
    {
        if (!IsChartDetail || SelectedChart is not { IsCreated: true } || IsLoadingChart || _disposed) return;
        IsOriginalPage = false; Request(null); ChartLoadTask = LoadChartAsync(true);
    }

    /// <summary>复杂或暂不支持的图表可显式打开原站，不把网页冒充本地画布。</summary>
    [RelayCommand]
    private void ViewOriginal()
    {
        if (!IsChartDetail || SelectedChart is not { IsCreated: true }) return;
        CancelLoad(); IsOriginalPage = true; IsSourceVisible = false;
        Request(SelectedChart.Address.AbsoluteUri, force: true); NotifyReader();
    }

    [RelayCommand]
    private void ReturnToLocal()
    {
        if (!IsChartDetail) return;
        IsOriginalPage = false; Request(null); Error = "";
        if (ChartContent is null && SelectedChart is { IsCreated: true }) ChartLoadTask = LoadChartAsync(false);
        NotifyReader();
    }

    [RelayCommand]
    private void RefreshDirectory()
    {
        if (!IsFlowCharts || _disposed) return;
        IsToolDirectory = false;
        CancelLoad(); IsUpdatingDirectory = true;
        DirectoryStatus = "正在从原站读取目录；失败时保留原目录。";
        Request(GtnhToolsCatalog.FlowChartIndexUri.AbsoluteUri, force: true); NotifyReader();
    }

    public void ImportPageData(string chartId, GtnhFlowPageData data, int? navigationRevision = null)
    {
        if (_disposed || !IsViewingOriginal || SelectedChart?.Id != chartId || IsLoadingChart ||
            navigationRevision is { } revision && revision != NavigationRevision) return;
        ChartLoadTask = LoadChartAsync(false, data);
    }

    /// <summary>仅消费当前原站页面的一次自动读取；加载、失败和其他页面的事件不能触发导入。</summary>
    public bool TryBeginAutomaticPageRead(string? address)
    {
        if (_disposed || !IsActive || !IsViewingOriginal || !IsAutomaticPageRead || IsLoading || IsLoadingChart || HasError ||
            SelectedChart is not { IsCreated: true } chart || !Uri.TryCreate(address, UriKind.Absolute, out var actual) ||
            !string.Equals(actual.AbsoluteUri, chart.Address.AbsoluteUri, StringComparison.Ordinal)) return false;
        IsAutomaticPageRead = false;
        BrowserStatus = "正在读取原站页面中的图表数据……";
        return true;
    }

    private async Task LoadChartAsync(bool refresh, GtnhFlowPageData? pageData = null)
    {
        if (SelectedChart is not { IsCreated: true } chart || _disposed) return;
        CancelLoad();
        var revision = _loadRevision;
        var cancellation = _loadCancellation = new CancellationTokenSource();
        IsLoadingChart = true; Error = ""; ChartStatus = refresh ? "正在检查原站图表更新……" : "正在读取图表数据……";
        try
        {
            if (_flowCharts is null) throw new InvalidOperationException("图表读取服务尚未配置，可在此页查看原站。");
            var content = pageData is null ? await _flowCharts.LoadAsync(chart, refresh, cancellation.Token) :
                await _flowCharts.ImportPageDataAsync(chart, pageData.PayloadJson, pageData.RevisionId, cancellation.Token);
            if (_disposed || revision != _loadRevision || cancellation.IsCancellationRequested) return;
            ChartContent = content;
            if (pageData is not null) { IsOriginalPage = false; Request(null); }
            ChartStatus = content.Warning ?? (content.FromCache ? "已从本机缓存打开，可离线查看。" : "已保存图表数据，下次可离线查看。");
            if (!content.HasMedia && content.Warning is null) ChartStatus += " 点击“检查更新”可读取原站图标和配方。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (GtnhFlowBrowserRequiredException ex) when (pageData is null)
        {
            if (!_disposed && revision == _loadRevision && !cancellation.IsCancellationRequested)
            {
                // 普通 HTTP 被站点拒绝时使用正常网页加载，不伪造浏览器请求或绕过站点验证。
                ChartContent ??= ex.CachedContent;
                IsOriginalPage = true; IsSourceVisible = false; IsAutomaticPageRead = true;
                Request(chart.Address.AbsoluteUri, force: true);
                ChartStatus = "原站要求使用浏览器读取，正在自动加载图表页面。";
                BrowserStatus = "正在通过原站页面读取图表，完成后会自动显示本地图表。";
            }
        }
        catch (Exception ex)
        {
            if (!_disposed && revision == _loadRevision)
            {
                Error = "图表数据读取失败：" + ex.Message;
                if (pageData is not null) BrowserStatus = Error;
                ChartStatus = ChartContent is not null ? "更新未完成，仍显示上次成功读取的图表。" : "可重试，或在启动器内查看原站。";
            }
        }
        finally
        {
            if (revision == _loadRevision) { IsLoadingChart = false; _loadCancellation = null; }
            cancellation.Dispose(); NotifyReader();
        }
    }

    private void CancelLoad()
    { _loadRevision++; _loadCancellation?.Cancel(); _loadCancellation = null; IsLoadingChart = false; IsAutomaticPageRead = false; }

    partial void OnIsActiveChanged(bool value)
    {
        if (!value) CancelLoad();
        else if (IsChartDetail && ChartContent is null && !IsOriginalPage && SelectedChart is { IsCreated: true })
            ChartLoadTask = LoadChartAsync(false);
    }
    partial void OnSelectedToolIndexChanged(int value)
    {
        IsToolDirectory = false;
        CancelLoad(); IsUpdatingDirectory = false;
        Request(!IsToolDirectory && SelectedTool.Address is { } address && !IsPlanner ? _onlineAddresses.GetValueOrDefault(value) ?? address.AbsoluteUri :
            IsFlowCharts && IsOriginalPage && IsDetailOpen && SelectedChart is { IsCreated: true } ? SelectedChart.Address.AbsoluteUri : null);
        if (IsFlowCharts && IsDetailOpen && ChartContent is null && !IsOriginalPage && SelectedChart is { IsCreated: true })
            ChartLoadTask = LoadChartAsync(false);
        NotifyReader();
    }
    partial void OnIsToolDirectoryChanged(bool value) => NotifyReader();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedSectionChanged(string value) => ApplyFilter();
    partial void OnIsDetailOpenChanged(bool value) => NotifyReader();
    partial void OnIsOriginalPageChanged(bool value) => NotifyReader();
    partial void OnIsUpdatingDirectoryChanged(bool value) => NotifyReader();
    partial void OnIsSourceVisibleChanged(bool value) { OnPropertyChanged(nameof(ShowSource)); OnPropertyChanged(nameof(ShowCanvas)); OnPropertyChanged(nameof(SourceToggleLabel)); }
    partial void OnChartContentChanged(GtnhFlowChartContent? value)
    {
        if (!HasEnglish) IsEnglish = false;
        RefreshGroups(); NotifyReader(); OnPropertyChanged(nameof(SourceMetadata)); OnPropertyChanged(nameof(SourceText)); OnPropertyChanged(nameof(HasEnglish));
    }
    partial void OnIsEnglishChanged(bool value) { RefreshGroups(); OnPropertyChanged(nameof(CurrentView)); }
    partial void OnSelectedGroupChanged(GtnhFlowGroup? value) => OnPropertyChanged(nameof(HighlightGroup));
    partial void OnRequestedAddressChanged(string? value) => OnPropertyChanged(nameof(HasBrowser));
    partial void OnErrorChanged(string value) { OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(ShowChartError)); }

    private void RefreshGroups()
    {
        var id = SelectedGroup?.Id;
        Groups.Clear(); Groups.Add(new GtnhFlowGroup("", "全部节点与路线", [], []));
        if (CurrentView is { } view) foreach (var group in view.Groups) Groups.Add(group);
        SelectedGroup = Groups.FirstOrDefault(group => group.Id == id) ?? Groups[0];
        OnPropertyChanged(nameof(CurrentView));
    }

    private void ApplyFilter()
    {
        var current = SelectedChart?.Id;
        var text = SearchText.Trim();
        var next = _directory.Where(chart => (SelectedSection == "全部目录" || chart.Section == SelectedSection) &&
            (text.Length == 0 || chart.Name.Contains(text, StringComparison.OrdinalIgnoreCase))).ToArray();
        Charts.Clear(); foreach (var chart in next) Charts.Add(chart);
        if (!IsDetailOpen) SelectedChart = next.FirstOrDefault(chart => chart.Id == current);
        OnPropertyChanged(nameof(HasNoResults)); OnPropertyChanged(nameof(DirectorySummary));
    }

    private void Request(string? address, bool force = false)
    {
        if (!force && RequestedAddress == address && address is not null) return;
        RequestedAddress = address; CurrentAddress = address ?? SelectedChart?.Address.AbsoluteUri ?? "";
        Error = ""; RuntimeMissing = false; IsLoading = address is not null;
        CanGoBack = false; CanGoForward = false; BrowserStatus = address is null ? "" : "正在加载网页……";
        NavigationRevision++;
    }

    public bool ApplyDirectoryJson(string json)
    {
        if (!IsFlowCharts || !IsUpdatingDirectory) return false;
        var accepted = GtnhToolsCatalog.TryReadDirectoryJson(json, out var next);
        if (accepted)
        {
            _directory = next; OnPropertyChanged(nameof(Sections));
            if (!Sections.Contains(SelectedSection)) SelectedSection = "全部目录";
            var selected = _directory.FirstOrDefault(item => item.Id == SelectedChart?.Id);
            if (selected is not null)
            {
                SelectedChart = selected;
                if (!selected.IsCreated) { ChartContent = null; ChartStatus = "未创建，等待更新"; IsOriginalPage = false; }
            }
            DirectoryStatus = $"目录已更新 · {next.Count} 项；未创建条目保留原位置。";
        }
        else DirectoryStatus = "原站目录暂时无法解析，已保留原目录。";
        IsUpdatingDirectory = false;
        Request(IsOriginalPage && SelectedChart is { IsCreated: true } ? SelectedChart.Address.AbsoluteUri : null);
        ApplyFilter();
        if (IsChartDetail && !IsOriginalPage && ChartContent is null && SelectedChart is { IsCreated: true })
            ChartLoadTask = LoadChartAsync(false);
        NotifyReader(); return accepted;
    }

    public void UpdateBrowserState(string? address, bool loading, bool back, bool forward, string? error, bool missingRuntime, string? message = null)
    {
        if (!HasBrowser) return;
        if (address is not null && (!Uri.TryCreate(address, UriKind.Absolute, out var candidate) || !GtnhToolNavigationPolicy.IsAllowed(candidate))) return;
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && GtnhToolNavigationPolicy.IsAllowed(uri))
        {
            var expectedHost = SelectedTool.Address?.Host ?? GtnhToolsCatalog.FlowChartIndexUri.Host;
            if (uri.Host != expectedHost) return;
            CurrentAddress = uri.AbsoluteUri;
            if (SelectedTool.Address is not null && !IsPlanner) _onlineAddresses[SelectedToolIndex] = CurrentAddress;
        }
        IsLoading = loading; CanGoBack = back; CanGoForward = forward;
        Error = error ?? ""; RuntimeMissing = missingRuntime;
        BrowserStatus = HasError ? Error : loading ? "正在加载网页……" : message ?? "网页已加载。";
        if (IsUpdatingDirectory && HasError) DirectoryStatus = "目录更新未完成，现有目录仍保留；可返回目录后重试。";
    }

    [RelayCommand]
    private void CancelDirectoryUpdate()
    { IsUpdatingDirectory = false; Request(null); NotifyReader(); }

    private void NotifyReader()
    {
        foreach (var name in new[] { nameof(IsFlowCharts), nameof(IsQuestBook), nameof(IsPlanner), nameof(IsChartList), nameof(IsChartDetail),
            nameof(IsToolWorkspace), nameof(SelectedTool), nameof(IsOreRegex), nameof(IsResearchHelper), nameof(IsSeedMap), nameof(IsThaumonomicon), nameof(ShowBrowserAddress), nameof(OnlineNotice),
            nameof(IsMissingChart), nameof(IsViewingOriginal), nameof(HasGraph), nameof(ShowCanvas), nameof(ShowSource), nameof(HasBrowser),
            nameof(ShowChartError), nameof(ReaderTitle), nameof(SourceLabel), nameof(CurrentView) }) OnPropertyChanged(name);
    }

    public void Dispose()
    { if (_disposed) return; _disposed = true; IsActive = false; CancelLoad(); }

    public async Task StopAsync()
    {
        IsActive = false; CancelLoad();
        await ChartLoadTask;
    }
}
