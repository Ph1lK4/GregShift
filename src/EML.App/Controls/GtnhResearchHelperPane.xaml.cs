using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using EML.App.ViewModels;
using Microsoft.Win32;

namespace EML.App.Controls;

public partial class GtnhResearchHelperPane : UserControl
{
    public GtnhResearchHelperViewModel ViewModel { get; } = new();
    public event EventHandler? DataRequested;
    private string? _settingsPath;
    private string? _pendingSettings;
    public GtnhResearchHelperPane()
    {
        InitializeComponent(); ToolRoot.DataContext = ViewModel;
        SizeChanged += (_, e) => ToolRoot.MinHeight = e.NewSize.Width < 900 ? 800 : 650;
    }

    public void LoadData(string json, string? sourceLabel = null)
    {
        ViewModel.LoadData(json, sourceLabel);
        if (_pendingSettings is not null)
        {
            try { ViewModel.ImportSettingsJson(_pendingSettings); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            { ViewModel.Status = "已读取要素数据，但设置未恢复：" + ex.Message; }
            _pendingSettings = null;
        }
    }
    public void SetStatus(string message) => ViewModel.Status = message;

    public void ConfigureStorage(string? toolsFolder)
    {
        if (string.IsNullOrWhiteSpace(toolsFolder)) return;
        var path = Path.Combine(Path.GetFullPath(toolsFolder), "research-helper.settings.json");
        if (_settingsPath == path) return;
        _settingsPath = path;
        try
        {
            if (!File.Exists(path)) return;
            var settings = ReadSettings(path);
            if (ViewModel.HasData) ViewModel.ImportSettingsJson(settings);
            else _pendingSettings = settings;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { ViewModel.Status = "本地设置未能读取：" + ex.Message + "\n" + path; }
    }

    private void OnRequestData(object sender, RoutedEventArgs e)
    {
        if (DataRequested is null) { ViewModel.Status = "要素数据读取服务尚未配置。"; return; }
        DataRequested.Invoke(this, EventArgs.Empty);
    }
    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasResult) return;
        try { Clipboard.SetText(ViewModel.GetPathText()); ViewModel.Status = "已复制要素连接链。"; }
        catch (System.Runtime.InteropServices.ExternalException ex) { ViewModel.Status = "剪贴板暂不可用，请直接选中结果复制：" + ex.Message; }
    }
    private void OnLocateInvalidWeight(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Aspects.FirstOrDefault(item => !item.IsWeightValid) is not { } invalid) return;
        ViewModel.IsWeighted = true;
        ViewModel.SearchText = "";
        AspectList.SelectedItem = invalid;
        AspectList.ScrollIntoView(invalid);
        ViewModel.Status = invalid.Label + " 的权重无效，请输入大于 0 的有限数值（使用小数点）。";
        AspectList.UpdateLayout();
        if (AspectList.ItemContainerGenerator.ContainerFromItem(invalid) is ListBoxItem container) container.Focus();
    }
    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasData) { ViewModel.Status = "请先读取要素数据，再保存设置。"; return; }
        if (_settingsPath is null) { ViewModel.Status = "本地设置目录尚未配置，可使用“导出设置”另存。"; return; }
        try
        {
            WriteSettings(_settingsPath);
            ViewModel.Status = "已保存研究助手设置，下次读取要素数据后恢复。\n" + _settingsPath;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { ViewModel.Status = "研究助手设置保存失败：" + ex.Message + "\n" + _settingsPath; }
    }
    private void OnImportSettings(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasData) { ViewModel.Status = "请先读取要素数据，再导入设置。"; return; }
        var dialog = new OpenFileDialog { Title = "导入研究助手设置", Filter = "研究助手设置 (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { ViewModel.ImportSettingsJson(ReadSettings(dialog.FileName)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { ViewModel.Status = "研究助手设置导入失败：" + ex.Message + "\n" + dialog.FileName; }
    }
    private void OnExportSettings(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasData) { ViewModel.Status = "请先读取要素数据，再导出设置。"; return; }
        var dialog = new SaveFileDialog { Title = "导出研究助手设置", Filter = "研究助手设置 (*.json)|*.json", FileName = "GregShift-研究助手.json", AddExtension = true, DefaultExt = ".json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { WriteSettings(dialog.FileName); ViewModel.Status = "已导出研究助手设置。\n" + dialog.FileName; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { ViewModel.Status = "研究助手设置导出失败：" + ex.Message + "\n" + dialog.FileName; }
    }
    private static string ReadSettings(string path)
    {
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("设置文件不能超过 256 KiB。");
        return File.ReadAllText(path, Encoding.UTF8);
    }
    private void WriteSettings(string path)
    {
        var settings = ViewModel.ExportSettingsJson();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, settings, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
