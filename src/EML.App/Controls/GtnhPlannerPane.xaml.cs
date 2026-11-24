using System.IO;
using System.Windows;
using System.Windows.Controls;
using EML.App.Services;
using EML.Core.Minecraft.Gtnh.Tools;
using Microsoft.Win32;

namespace EML.App.Controls;

/// <summary>规划器使用独立的浏览器和存储；网页始终拿不到宿主文件服务。</summary>
public partial class GtnhPlannerPane : UserControl
{
    internal GtnhToolBrowser Browser { get; } = new() { PlannerMode = true };
    private bool _updatingSearch, _searchRefreshQueued, _busy;

    public GtnhPlannerPane()
    {
        InitializeComponent();
        BrowserHost.Content = Browser;
        Browser.BrowserStateChanged += (_, state) =>
        {
            ErrorPanel.Visibility = state.HasError ? Visibility.Visible : Visibility.Collapsed;
            ErrorText.Text = state.Message;
            if (!_busy) StatusText.Text = state.Message;
        };
        Unloaded += (_, _) => Browser.IsActive = false;
    }

    internal void Activate(bool active, string? readerDataFolder)
    {
        Browser.IsActive = false;
        Browser.UserDataFolder = string.IsNullOrWhiteSpace(readerDataFolder) ? null :
            Path.Combine(Path.GetDirectoryName(readerDataFolder)!, "planner-webview2");
        Browser.RequestedAddress = GtnhToolNavigationPolicy.PlannerUri.AbsoluteUri;
        Browser.IsActive = active;
    }

    private async void OnLanguageChanged(object sender, RoutedEventArgs e)
    {
        // XAML 初始化时 Checked 会早于其他命名控件。
        if (StatusText is null) return;
        try { await Browser.SetPlannerLanguageAsync(ChineseToggle.IsChecked == true); }
        catch (Exception ex) { StatusText.Text = "显示语言切换未完成：" + ex.Message; }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSearch || _searchRefreshQueued || e.OriginalSource is not TextBox editor) return;
        _searchRefreshQueued = true;
        // 等 ComboBox 完成本次文本/光标同步再刷新；合并同一次编辑的路由事件。
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            _searchRefreshQueued = false;
            UpdateSearchCandidates(editor);
        }));
    }

    private void UpdateSearchCandidates(TextBox editor)
    {
        // 可编辑 ComboBox 在输入新文字后仍可能保留上一次选择；不能因此忽略新查询。
        var query = editor.Text;
        if (ItemSearch.SelectedItem is GtnhPlannerSearchItem selected && query == selected.Label) return;
        var selectionStart = editor.SelectionStart;
        var selectionLength = editor.SelectionLength;
        _updatingSearch = true;
        try
        {
            ItemSearch.SelectedItem = null;
            ItemSearch.ItemsSource = GtnhPlannerResources.Search(query);
            ItemSearch.Text = query;
            editor.Select(Math.Min(selectionStart, query.Length), Math.Min(selectionLength, query.Length - Math.Min(selectionStart, query.Length)));
        }
        finally { _updatingSearch = false; }
    }

    private async void OnSearch(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var selected = ItemSearch.SelectedItem as GtnhPlannerSearchItem;
        if (selected is not null && ItemSearch.Text != selected.Label) selected = null;
        var query = selected?.English ?? ItemSearch.Text.Trim();
        if (query.Length == 0) { StatusText.Text = "请先输入并选择物品。"; ItemSearch.Focus(); return; }
        if (selected is null && query.Any(character => character > 127))
        {
            ItemSearch.IsDropDownOpen = true;
            StatusText.Text = "请在上方中文搜索框的候选列表中选择物品，再点“搜索物品”；下方网页搜索框使用英文。";
            return;
        }
        try { await Browser.SearchPlannerAsync(query); StatusText.Text = "正在规划器中搜索：" + (selected?.Label ?? query); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        if (_busy) { StatusText.Text = "请等待方案文件操作结束后再刷新。"; return; }
        Browser.Reload();
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (_busy || Window.GetWindow(this) is not { } owner) return;
        var dialog = new OpenFileDialog { Title = "导入 GTNH Planner 方案", Filter = "Planner 方案 (*.json)|*.json", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(owner) != true) return;
        SetBusy(true, "正在检查并导入方案；原有方案不会被覆盖……");
        try
        {
            var bytes = await ReadPlanAsync(dialog.FileName);
            var name = GtnhPlannerPlan.Validate(bytes);
            await Browser.ImportPlannerAsync(bytes);
            StatusText.Text = "已导入方案：" + name;
            Result("方案导入完成", "已在规划器中新建方案：“" + name + "”。原有方案仍保留。", dialog.FileName);
        }
        catch (Exception ex) { StatusText.Text = "方案导入未完成：" + ex.Message; Result("方案导入未完成", ex.Message, dialog.FileName); }
        finally { SetBusy(false); }
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (_busy || Window.GetWindow(this) is not { } owner) return;
        SetBusy(true, "正在读取当前方案 JSON……");
        string? path = null;
        try
        {
            var bytes = await Browser.ExportPlannerAsync();
            var dialog = new SaveFileDialog { Title = "导出 GTNH Planner 方案", Filter = "Planner 方案 (*.json)|*.json", FileName = "gtnh-plan.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = true };
            if (dialog.ShowDialog(owner) != true) { StatusText.Text = "已取消保存，方案仍保留在规划器中。"; return; }
            path = dialog.FileName;
            await SavePlanAsync(path, bytes);
            StatusText.Text = "已导出方案：" + path;
            Result("方案导出完成", "JSON 方案已保存，可在 GTNH Planner 中重新导入。", path);
        }
        catch (Exception ex) { StatusText.Text = "方案导出未完成：" + ex.Message; Result("方案导出未完成", ex.Message, path); }
        finally { SetBusy(false); }
    }

    internal static async Task<byte[]> ReadPlanAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (stream.Length == 0 || stream.Length > GtnhPlannerPlan.MaximumBytes) throw new InvalidDataException("方案必须为非空 JSON，且不超过 10 MiB。");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("读取时方案文件发生变化，请重试。");
        return bytes;
    }

    internal static async Task SavePlanAsync(string path, byte[] bytes)
    {
        GtnhPlannerPlan.Validate(bytes);
        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".gregshift-plan-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await stream.WriteAsync(bytes); await stream.FlushAsync(); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void SetBusy(bool value, string? status = null)
    {
        _busy = value;
        ImportButton.IsEnabled = ExportButton.IsEnabled = SearchButton.IsEnabled = !value;
        if (status is not null) StatusText.Text = status;
    }

    private void Result(string title, string message, string? path = null)
    {
        if (Window.GetWindow(this) is not { } owner) return;
        var dialog = new DialogWindow(title, "", height: 420) { Owner = owner };
        dialog.Body.Children.Add(DialogPrompt.DetailCard("操作结果，可复制", message));
        if (path is not null) dialog.Body.Children.Add(DialogPrompt.DetailCard("相关路径，可复制", path));
        dialog.CancelButton.Content = "知道了";
        dialog.ShowDialog();
    }

    private void OnAbout(object sender, RoutedEventArgs e) => Result("产线规划器说明",
        "GTNH Planner 由 jackwrichards/gtnh-factory-flow 提供。中文词典来自 NanShang126/gtnh-planner-zh（MIT），并补充当前界面用语；未收录内容保留英文。\n\n" +
        "这是在线工具，配方、图标和计算由原站提供，并非 GTNH 官方功能。社区服务可能单独不可用；GregShift 不保证社区登录。\n\n" +
        "方案和网站设置保存在独立浏览器目录；清理该目录或卸载时请先导出 JSON。不会读取游戏实例、启动器账号或游戏存档。\n\n" +
        "中文显示只改变文字呈现，不修改物品 ID、配方、方案名称或用户注释。配方和译名请以所玩整合包版本为准。",
        Browser.UserDataFolder);
}
