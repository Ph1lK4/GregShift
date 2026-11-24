using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EML.Core.Minecraft.Gtnh.Tools;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EML.App.Controls;

/// <summary>只供 GTNH 工具阅读使用的浏览器，不向网页提供账号或实例访问接口。</summary>
public sealed partial class GtnhToolBrowser : UserControl, IDisposable
{
    public static readonly DependencyProperty RequestedAddressProperty = DependencyProperty.Register(
        nameof(RequestedAddress), typeof(string), typeof(GtnhToolBrowser), new PropertyMetadata(null, OnRequestChanged));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(GtnhToolBrowser), new PropertyMetadata(false, OnRequestChanged));
    public static readonly DependencyProperty UserDataFolderProperty = DependencyProperty.Register(
        nameof(UserDataFolder), typeof(string), typeof(GtnhToolBrowser), new PropertyMetadata(null, OnRequestChanged));

    private WebView2? _browser;
    private Task? _initializing;
    private Window? _window;
    private CancellationTokenSource? _navigationTimeout;
    private string? _lastRequestedAddress;
    private ulong _navigationId;
    private long _readGeneration;
    private bool _disposed, _runtimeMissing;
    private string _message = "选择一个流程图，或打开在线任务书。";
    private bool _hasError, _loading;

    public GtnhToolBrowser()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? RequestedAddress { get => (string?)GetValue(RequestedAddressProperty); set => SetValue(RequestedAddressProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public string? UserDataFolder { get => (string?)GetValue(UserDataFolderProperty); set => SetValue(UserDataFolderProperty, value); }
    public event EventHandler<GtnhToolBrowserState>? BrowserStateChanged;
    public event EventHandler<string>? DirectoryReceived;
    internal bool IsBrowserCreated => _browser is not null;
    internal bool PlannerMode { get; init; }

    /// <summary>浏览历史只保存在工具自己的 WebView2 配置目录中。</summary>
    public void GoBack() { if (_browser?.CoreWebView2 is { CanGoBack: true } core) { BeginNavigation(); core.GoBack(); } }
    public void GoForward() { if (_browser?.CoreWebView2 is { CanGoForward: true } core) { BeginNavigation(); core.GoForward(); } }
    public void Reload()
    {
        if (_disposed || !IsActive) return;
        if (_browser?.CoreWebView2 is { } core && IsAllowedAddress(core.Source))
        { BeginNavigation(); core.Reload(); }
        else { _lastRequestedAddress = null; QueueRequest(); }
    }

    /// <summary>只读取当前已加载图表页的声明式数据，不暴露宿主对象或浏览器凭据。</summary>
    public async Task<GtnhFlowPageData> ReadFlowChartDataAsync(Uri expectedAddress)
    {
        if (_disposed || !IsActive || _loading || _hasError || _browser?.CoreWebView2 is not { } core ||
            !GtnhToolNavigationPolicy.IsFlowChartPage(expectedAddress) || !Uri.TryCreate(core.Source, UriKind.Absolute, out var actual) ||
            !string.Equals(actual.AbsoluteUri, expectedAddress.AbsoluteUri, StringComparison.Ordinal))
            throw new InvalidOperationException("请等待对应的原站流程图页面加载完成，再读取数据。");
        var navigation = _navigationId;
        var generation = _readGeneration;
        var json = await core.ExecuteScriptAsync(GtnhFlowPageDataScript.Script);
        if (_disposed || !IsActive || _loading || _hasError || generation != _readGeneration ||
            navigation != _navigationId || !ReferenceEquals(core, _browser?.CoreWebView2) ||
            !Uri.TryCreate(core.Source, UriKind.Absolute, out actual) ||
            !string.Equals(actual.AbsoluteUri, expectedAddress.AbsoluteUri, StringComparison.Ordinal))
            throw new InvalidOperationException("页面已切换，未读取旧页面的数据。");
        if (json.Length > 13 * 1024 * 1024) throw new InvalidDataException("页面图表数据过大。");
        var data = JsonSerializer.Deserialize<GtnhFlowPageData>(json, new JsonSerializerOptions { MaxDepth = 4 });
        if (data?.PayloadJson is null || data.PayloadJson.Length > GtnhFlowDocumentParser.MaximumPayloadCharacters)
            throw new InvalidDataException("页面还未提供可读取的流程图数据。若原站要求验证，请先自行完成验证。");
        return data;
    }

    private static void OnRequestChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((GtnhToolBrowser)sender).QueueRequest();
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (!ReferenceEquals(_window, window))
        {
            if (_window is not null) _window.Closed -= OnWindowClosed;
            _window = window;
            if (_window is not null) _window.Closed += OnWindowClosed;
        }
        QueueRequest();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) => PauseBrowser();
    private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

    private void QueueRequest()
    {
        if (_disposed) return;
        if (!IsLoaded || !IsActive || string.IsNullOrWhiteSpace(RequestedAddress))
        {
            if (_browser is not null) _browser.Visibility = Visibility.Collapsed;
            PauseBrowser();
            return;
        }
        _ = ActivateAsync();
    }

    private async Task ActivateAsync()
    {
        try
        {
            if (!IsPermittedAddress(RequestedAddress)) { Report("已阻止非 GTNH 工具网页地址。", true); return; }
            if (_initializing is null)
            {
                _initializing = InitializeBrowserAsync();
                try { await _initializing; }
                finally { _initializing = null; }
            }
            else await _initializing;
            if (_disposed || !IsLoaded || !IsActive || !IsPermittedAddress(RequestedAddress) || _browser?.CoreWebView2 is not { } core) return;
            _browser.Visibility = Visibility.Visible;
            core.Resume();
            if (!string.Equals(_lastRequestedAddress, RequestedAddress, StringComparison.Ordinal))
            {
                _lastRequestedAddress = RequestedAddress;
                // Navigate 的开始事件稍后才到达，不能把旧页面的完成状态当成本次读取时机。
                BeginNavigation();
                core.Navigate(RequestedAddress);
            }
            PublishState();
        }
        catch (Exception ex)
        {
            if (!_disposed) Report("内嵌网页暂时无法打开：" + ex.Message, true);
        }
    }

    private async Task InitializeBrowserAsync()
    {
        if (_browser?.CoreWebView2 is not null) return;
        if (string.IsNullOrWhiteSpace(UserDataFolder) || !Path.IsPathFullyQualified(UserDataFolder))
        { Report("未配置 GTNH 工具的浏览器数据目录。", true); return; }
        try
        {
            // 仅显式打开工具时建立独立浏览器配置；不使用系统浏览器或微软登录配置。
            Directory.CreateDirectory(UserDataFolder);
            _runtimeMissing = false;
            _loading = true; Report("正在准备内嵌网页……");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
            if (_disposed) return;
            _browser ??= new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(12, 17, 28) };
            Content = _browser;
            await _browser.EnsureCoreWebView2Async(environment);
            if (_disposed) return;
            ConfigureBrowser(_browser.CoreWebView2);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _runtimeMissing = true;
            Report("尚未安装 Microsoft Edge WebView2 运行时。安装后可在启动器内阅读网页。", true);
            ReleaseBrowser();
        }
        catch
        {
            ReleaseBrowser();
            throw;
        }
        finally
        {
            _loading = false; PublishState();
            if (!_disposed && (!IsActive || !IsLoaded)) PauseBrowser();
        }
    }

    private void ConfigureBrowser(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        core.IsMuted = true;
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.SourceChanged += OnSourceChanged;
        core.HistoryChanged += OnHistoryChanged;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.DownloadStarting += OnDownloadStarting;
        core.ProcessFailed += OnProcessFailed;
        core.ServerCertificateErrorDetected += OnCertificateError;
        core.BasicAuthenticationRequested += OnAuthenticationRequested;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_disposed || !IsPermittedAddress(e.Uri))
        { e.Cancel = true; Report("已阻止离开 GTNH 工具站点的跳转，可复制当前链接后自行查看。", false); return; }
        _navigationId = e.NavigationId;
        _readGeneration++;
        _navigationTimeout?.Cancel(); _navigationTimeout?.Dispose();
        _navigationTimeout = new CancellationTokenSource();
        _loading = true; Report("正在加载网页……");
        _ = WatchNavigationAsync(e.NavigationId, _navigationTimeout.Token);
    }

    private async Task WatchNavigationAsync(ulong navigationId, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), token);
            if (_disposed || navigationId != _navigationId || !_loading) return;
            _browser?.CoreWebView2?.Stop(); _loading = false;
            Report("网页加载超时。请检查网络后点击重新加载；目录与当前位置仍然保留。", true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        { if (!_disposed) { _loading = false; Report("网页连接已停止：" + ex.Message, true); } }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_disposed || e.NavigationId != _navigationId || !IsActive) return;
        _navigationTimeout?.Cancel();
        _loading = false;
        if (!e.IsSuccess)
        { Report("网页未能加载（" + e.WebErrorStatus + "）。请检查网络后重新加载。", true); return; }
        Report("网页内容由对应站点提供；不会读取游戏存档或同步任务进度。");
        if (PlannerMode)
        {
            try { await InitializePlannerAsync(); }
            catch (Exception ex) { if (!_disposed) Report("网页已打开，但中文适配尚未就绪：" + ex.Message); }
            return;
        }
        if (GtnhHandbookFocus.IsPage(RequestedAddress) && _browser?.CoreWebView2 is { } handbook &&
            GtnhHandbookFocus.IsPage(handbook.Source))
        {
            await FocusHandbookAsync(handbook, e.NavigationId);
            return;
        }
        if (_browser?.CoreWebView2 is not { } core || !IsFlowChartDirectory(core.Source)) return;
        try
        {
            var json = await core.ExecuteScriptAsync(DirectoryScript);
            if (_disposed || e.NavigationId != _navigationId || !IsFlowChartDirectory(core.Source) || json.Length > 262144) return;
            DirectoryReceived?.Invoke(this, json);
        }
        catch (Exception ex)
        { if (!_disposed) Report("网页已打开，但目录暂时未能更新：" + ex.Message); }
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e) => PublishState();
    private void OnHistoryChanged(object? sender, object e) => PublishState();
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (IsPermittedAddress(e.Uri) && _browser?.CoreWebView2 is { } core) { BeginNavigation(); core.Navigate(e.Uri); }
        else Report("该链接不在内嵌工具站点范围内，已阻止弹出新窗口。", false);
    }
    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; }
    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true; e.Handled = true;
        if (!_plannerTransferActive) Report(PlannerMode ? "请使用上方“导出方案”保存 JSON；其他网页下载已阻止。" :
            "内嵌阅读器不直接保存下载文件。图片导出请复制页面链接后在浏览器中操作。", false);
    }
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _navigationTimeout?.Cancel(); _loading = false; _lastRequestedAddress = null;
        Report("网页进程已停止（" + e.ProcessFailedKind + "），请重新加载。", true);
        ReleaseBrowser();
    }
    private void OnCertificateError(object? sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    { e.Action = CoreWebView2ServerCertificateErrorAction.Cancel; Report("网站证书验证失败，已停止连接。请检查系统时间或网络。", true); }
    private static void OnAuthenticationRequested(object? sender, CoreWebView2BasicAuthenticationRequestedEventArgs e) => e.Cancel = true;

    private void PauseBrowser()
    {
        _readGeneration++;
        if (_disposed || _browser?.CoreWebView2 is not { } core) return;
        _navigationTimeout?.Cancel();
        if (_loading) { core.Stop(); _lastRequestedAddress = null; _loading = false; }
        _ = SuspendAsync(core);
    }

    private void BeginNavigation()
    { _readGeneration++; _loading = true; Report("正在加载网页……"); }
    private async Task SuspendAsync(CoreWebView2 core)
    {
        try
        {
            await core.TrySuspendAsync();
            // 快速切回页面时，迟到的挂起结果不能盖过较新的 Resume。
            if (!_disposed && IsActive && IsLoaded && ReferenceEquals(core, _browser?.CoreWebView2)) core.Resume();
        }
        catch (Exception) { /* 不影响离开页面或关闭窗口。 */ }
    }

    private void Report(string message, bool error = false)
    {
        _message = message; _hasError = error;
        // 原生 WebView HWND 不能遮住 WPF 错误提示；错误时隐藏网页，重试导航后再显示。
        if (_browser is not null) _browser.Visibility = error || !IsActive ? Visibility.Collapsed : Visibility.Visible;
        PublishState();
    }
    private void PublishState()
    {
        if (_disposed) return;
        var core = _browser?.CoreWebView2;
        BrowserStateChanged?.Invoke(this, new(core?.Source ?? RequestedAddress ?? "", _loading,
            core?.CanGoBack == true, core?.CanGoForward == true, _message, _hasError, _runtimeMissing));
    }

    internal static bool IsAllowedAddress(string? address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        GtnhToolNavigationPolicy.IsAllowed(uri);

    internal bool IsPermittedAddress(string? address) => IsAllowedAddress(address) &&
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && (PlannerMode
            ? GtnhToolNavigationPolicy.IsPlanner(uri) : !GtnhToolNavigationPolicy.IsPlanner(uri) &&
                Uri.TryCreate(RequestedAddress, UriKind.Absolute, out var requested) && uri.IdnHost == requested.IdnHost);

    private static bool IsFlowChartDirectory(string? address)
    {
        if (!IsAllowedAddress(address) || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Host != "gtnh.huijiwiki.com") return false;
        return Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/') == "/wiki/模板:FlowChart";
    }

    private void ReleaseBrowser()
    {
        var browser = _browser; _browser = null; Content = null;
        if (browser?.CoreWebView2 is { } core)
        {
            core.NavigationStarting -= OnNavigationStarting; core.NavigationCompleted -= OnNavigationCompleted;
            core.SourceChanged -= OnSourceChanged; core.HistoryChanged -= OnHistoryChanged;
            core.NewWindowRequested -= OnNewWindowRequested; core.PermissionRequested -= OnPermissionRequested;
            core.DownloadStarting -= OnDownloadStarting; core.ProcessFailed -= OnProcessFailed;
            core.ServerCertificateErrorDetected -= OnCertificateError; core.BasicAuthenticationRequested -= OnAuthenticationRequested;
        }
        browser?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _navigationTimeout?.Cancel(); _navigationTimeout?.Dispose();
        Loaded -= OnLoaded; Unloaded -= OnUnloaded;
        if (_window is not null) _window.Closed -= OnWindowClosed;
        _window = null; ReleaseBrowser();
    }

    // 只读取已打开的官方目录 DOM；不访问网页内部状态、凭据或隐藏接口。
    private const string DirectoryScript = """
        (() => {
          const root = document.querySelector('#mw-content-text');
          if (!root) return [];
          const output = [];
          const seen = new Set();
          let groupList = null, groupIndex = 0;
          for (const a of root.querySelectorAll('a[href]')) {
            let list = a.closest('ul');
            if (!list) continue;
            while (list.parentElement && list.parentElement.closest('ul')) list = list.parentElement.closest('ul');
            let u;
            try { u = new URL(a.getAttribute('href'), location.href); } catch { continue; }
            if (u.protocol !== 'https:' || u.hostname !== 'gtnh.huijiwiki.com' || u.username || u.password) continue;
            let title = u.pathname.startsWith('/wiki/') ? decodeURIComponent(u.pathname.slice(6)) : u.searchParams.get('title');
            if (!title || !title.startsWith('模板:FlowChart/') || title.length > 256) continue;
            if (seen.has(title)) continue;
            seen.add(title);
            const name = (a.textContent || '').trim().replace(/^模板:FlowChart\//, '');
            if (!name || name.length > 160) continue;
            if (list !== groupList) { groupList = list; groupIndex++; }
            const section = groupIndex === 1 ? '工艺流程图' : '多选择 / 配方对比';
            output.push({ name, href: u.href, section, isCreated: !a.classList.contains('new') });
            if (output.length >= 512) break;
          }
          return output;
        })()
        """;
}

public sealed record GtnhToolBrowserState(string Address, bool IsLoading, bool CanGoBack, bool CanGoForward,
    string Message, bool HasError, bool RuntimeMissing);
