using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using EML.App.ViewModels;
using EML.Core.Minecraft.Gtnh.Tools;
using Microsoft.Win32;

namespace EML.App.Controls;

public partial class GtnhOreRegexPane : UserControl
{
    public GtnhOreRegexViewModel ViewModel { get; } = new();
    private string? _settingsPath;
    public GtnhOreRegexPane()
    {
        InitializeComponent(); ToolRoot.DataContext = ViewModel;
        SizeChanged += (_, e) => ToolRoot.MinHeight = e.NewSize.Width < 900 ? 800 : 650;
    }
    private GtnhOreRegexViewModel Model => ViewModel;
    public void LoadCatalog(GtnhOreCatalog catalog) => ViewModel.LoadCatalog(catalog);
    public void ConfigureStorage(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        var path = Path.Combine(Path.GetFullPath(folder), "ore-processing.settings.json");
        if (_settingsPath == path) return;
        _settingsPath = path;
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("处理方案超过 1 MiB。");
            ViewModel.ImportPlanJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { ViewModel.Status = "保存的方案未能恢复，现有设置仍保留：" + ex.Message + "\n" + path; }
    }
    private void OnSavePlan(object sender, RoutedEventArgs e)
    {
        if (_settingsPath is null) { ViewModel.Status = "本地设置目录尚未配置，可使用“导出方案”另存。"; return; }
        try { WritePlan(_settingsPath); ViewModel.Status = "已保存方案，下次打开此工具时恢复。\n" + _settingsPath; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { ViewModel.Status = "方案保存失败，当前输入仍保留：" + ex.Message + "\n" + _settingsPath; }
    }
    private void WritePlan(string path)
    {
        var json = ViewModel.ExportPlanJson();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void OnCopyRule(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model || sender is not Button { Tag: GtnhOreRegexResult result }) return;
        if (result.IsOverLimit) { model.Status = "此规则超过过滤卡长度上限，请先调整方案；不能复制被截断的规则。"; return; }
        try { Clipboard.SetText(result.Expression); model.Status = "已复制" + result.MachineName + "的过滤规则。"; }
        catch (ExternalException) { model.Status = "剪贴板暂时被其他程序占用，请稍后重试；也可以选中规则手动复制。"; }
    }
    private async void OnImportPlan(object sender, RoutedEventArgs e)
    {
        if (Model is not { HasCatalog: true } model) return;
        var dialog = new OpenFileDialog { Title = "导入矿物处理方案", Filter = "矿物处理方案 (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidDataException("处理方案超过 1 MiB，未导入。");
            model.ImportPlanJson(await File.ReadAllTextAsync(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { model.Status = "方案导入失败：" + ex.Message + "\n路径：" + dialog.FileName; }
    }
    private async void OnExportPlan(object sender, RoutedEventArgs e)
    {
        if (Model is not { HasCatalog: true } model) return;
        var dialog = new SaveFileDialog { Title = "导出矿物处理方案", Filter = "矿物处理方案 (*.json)|*.json", FileName = "gtnh-ore-processing.json", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { WritePlan(dialog.FileName); model.Status = "方案已导出：" + dialog.FileName; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { model.Status = "方案导出失败：" + ex.Message + "\n路径：" + dialog.FileName; }
    }
}
