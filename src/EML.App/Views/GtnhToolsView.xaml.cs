using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EML.App.Services;
using EML.App.Controls;
using EML.App.ViewModels;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.Views;

/// <summary>仅连接阅读器与页面状态，网页不会取得 ViewModel 或任何本机服务引用。</summary>
public partial class GtnhToolsView : UserControl
{
    private GtnhToolsViewModel? _model;
    private (GtnhToolsViewModel Model, int Revision)? _readingPage;
    private bool _oreLoaded;
    private bool _researchLoaded;

    public GtnhToolsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += (_, _) => SynchronizeBrowser();
        Loaded += (_, _) => AttachModel();
        Unloaded += (_, _) => { Browser.IsActive = false; Planner.Activate(false, _model?.BrowserDataFolder); DetachModel(); };
        Browser.BrowserStateChanged += OnBrowserStateChanged;
        Browser.DirectoryReceived += (_, json) => _model?.ApplyDirectoryJson(json);
        ResearchHelper.DataRequested += (_, _) => LoadResearchData();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => AttachModel();
    private void AttachModel()
    {
        if (!ReferenceEquals(_model, DataContext))
        {
            DetachModel();
            _model = DataContext as GtnhToolsViewModel;
            if (_model is not null) _model.PropertyChanged += OnModelChanged;
        }
        SynchronizeBrowser();
    }
    private void DetachModel()
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = null;
    }
    private async void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GtnhToolsViewModel.IsDetailOpen))
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (_model?.IsDetailOpen == true) ReturnToChartsButton.Focus();
                else if (ChartList.SelectedItem is { } selected && ChartList.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item) item.Focus();
                else SearchInput.Focus();
            }));
        if (e.PropertyName is nameof(GtnhToolsViewModel.IsActive) or nameof(GtnhToolsViewModel.IsPlanner) or nameof(GtnhToolsViewModel.IsToolDirectory)) SynchronizeBrowser();
        if (e.PropertyName == nameof(GtnhToolsViewModel.IsToolWorkspace) && _model?.IsToolWorkspace == true)
            _ = Dispatcher.BeginInvoke(new Action(() => ReturnToToolsButton.Focus()));
        if (e.PropertyName == nameof(GtnhToolsViewModel.NavigationRevision))
        {
            var sameAddress = _model?.RequestedAddress == Browser.RequestedAddress;
            SynchronizeBrowser();
            if (sameAddress && _model is { IsActive: true, HasBrowser: true }) Browser.Reload();
        }
        // 已打开的同地址网页可能先于 HTTP 交接结束报告完成；交接结束后再检查一次。
        if (e.PropertyName == nameof(GtnhToolsViewModel.IsLoadingChart) && _model is { IsLoadingChart: false } model &&
            _readingPage != (model, model.NavigationRevision) && model.TryBeginAutomaticPageRead(model.CurrentAddress))
            await ReadPageDataAsync(model);
    }
    private void SynchronizeBrowser()
    {
        // 先暂停再改变地址，避免切换过程中旧页面与新页面竞争。
        Browser.IsActive = false;
        Browser.UserDataFolder = _model?.BrowserDataFolder;
        Browser.RequestedAddress = _model?.RequestedAddress;
        Browser.IsActive = IsVisible && _model is { IsActive: true, HasBrowser: true };
        Planner.Activate(IsVisible && _model is { IsActive: true, IsPlanner: true, IsToolDirectory: false }, _model?.BrowserDataFolder);
        if (_model is not { IsToolDirectory: false } model) return;
        if (model.IsOreRegex && !_oreLoaded)
        {
            try
            {
                OreRegex.LoadCatalog(GtnhNativeToolData.LoadOres());
                OreRegex.ConfigureStorage(model.ToolsDataFolder);
                _oreLoaded = true;
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or InvalidOperationException)
            { OreRegex.ViewModel.Status = "内置矿物数据读取失败：" + ex.Message; }
        }
        if (model.IsResearchHelper && !_researchLoaded)
        {
            ResearchHelper.ConfigureStorage(model.ToolsDataFolder);
            LoadResearchData();
        }
    }
    private void LoadResearchData()
    {
        try { ResearchHelper.LoadData(GtnhNativeToolData.LoadResearchJson(), GtnhNativeToolData.ResearchSourceLabel); _researchLoaded = true; }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or InvalidOperationException)
        { ResearchHelper.SetStatus("内置要素数据读取失败：" + ex.Message); }
    }
    private async void OnBrowserStateChanged(object? sender, GtnhToolBrowserState state)
    {
        if (_model is not { } model) return;
        model.UpdateBrowserState(state.Address, state.IsLoading, state.CanGoBack, state.CanGoForward,
            state.HasError ? state.Message : null, state.RuntimeMissing, state.Message);
        if (_readingPage != (model, model.NavigationRevision) && model.TryBeginAutomaticPageRead(state.Address))
            await ReadPageDataAsync(model);
    }

    private void OnBack(object sender, RoutedEventArgs e) => Browser.GoBack();
    private void OnForward(object sender, RoutedEventArgs e) => Browser.GoForward();
    private void OnReload(object sender, RoutedEventArgs e) => Browser.Reload();
    private void OnZoomIn(object sender, RoutedEventArgs e) => FlowCanvas.ZoomIn();
    private void OnZoomOut(object sender, RoutedEventArgs e) => FlowCanvas.ZoomOut();
    private void OnFit(object sender, RoutedEventArgs e) => FlowCanvas.FitView();
    private void OnResetZoom(object sender, RoutedEventArgs e) => FlowCanvas.ResetView();
    private async void OnReadPageData(object sender, RoutedEventArgs e)
    {
        if (_model is not { IsViewingOriginal: true } model || sender is not Button button) return;
        button.IsEnabled = false;
        try { await ReadPageDataAsync(model); }
        finally { button.IsEnabled = true; }
    }
    private async Task ReadPageDataAsync(GtnhToolsViewModel model)
    {
        if (model is not { IsViewingOriginal: true, SelectedChart: { } chart } || model.IsLoadingChart) return;
        var navigationRevision = model.NavigationRevision;
        var readingPage = (model, navigationRevision);
        if (_readingPage == readingPage) return;
        // 旧页面的脚本尚未返回时，新导航仍拥有自己的读取机会；旧结果由导航版本拒绝。
        _readingPage = readingPage;
        try
        {
            var data = await Browser.ReadFlowChartDataAsync(chart.Address);
            if (ReferenceEquals(model, _model)) model.ImportPageData(chart.Id, data, navigationRevision);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(model, _model) && model.IsViewingOriginal && model.SelectedChart?.Id == chart.Id &&
                model.NavigationRevision == navigationRevision)
                model.BrowserStatus = "尚未读到图表数据。若页面要求验证，请自行完成后点击“读取为本地图表”；也可刷新页面重试。原因：" + ex.Message;
        }
        finally { if (_readingPage == readingPage) _readingPage = null; }
    }
    private void OnChartMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(ChartList, source) is not ListBoxItem { DataContext: GtnhFlowChart chart }) return;
        _model?.OpenDetailsCommand.Execute(chart);
    }
    private void OnChartKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not ListBoxItem { DataContext: GtnhFlowChart chart }) return;
        _model?.OpenDetailsCommand.Execute(chart); e.Handled = true;
    }
    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _model is not { IsToolDirectory: false } model) return;
        if (model.IsChartDetail) model.BackToChartsCommand.Execute(null);
        else model.BackToToolsCommand.Execute(null);
        e.Handled = true;
    }
    private void OnAttribution(object sender, RoutedEventArgs e)
    {
        if (_model?.ChartContent is not { } content || Window.GetWindow(this) is not { } owner) return;
        DialogPrompt.Inform(owner, "流程图来源与署名", content.Attribution + "\n\n来源：\n" + content.SourceAddress +
            "\n\n贡献者与历史：\n" + content.HistoryAddress + "\n\n" + GtnhFlowChartContent.LicenseName +
            "\n" + GtnhFlowChartContent.LicenseAddress +
            "\n\n图表、配方与图片分别遵循原作者的许可，不属于启动器代码的 GPL 授权。图片按需从原站读取并缓存在本机；本地视图不执行原站绘图程序。配方仅供参考，实际以所玩整合包版本为准。");
    }
    private void OnCopyAddress(object sender, RoutedEventArgs e)
    {
        if (_model is not { CurrentAddress.Length: > 0 } model) return;
        try { Clipboard.SetText(model.CurrentAddress); model.BrowserStatus = "已复制当前网页链接。"; }
        catch (Exception ex) { model.BrowserStatus = "复制未完成，可选中地址手动复制：" + ex.Message; }
    }
    private void OnRuntimeHelp(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (_model is { } model) model.BrowserStatus = "无法打开安装帮助：" + ex.Message;
        }
    }
}
