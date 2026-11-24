using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using EML.App.ViewModels;
using EML.Core.Minecraft.Gtnh;

namespace EML.App.Services;

public sealed partial class InstanceMigrationDialogs
{
    /// <summary>只读版本选择复用镜像目录、筛选和取消机制，不在模态窗口里导入或迁移文件。</summary>
    public Task<GtnhPackRelease?> ChooseUpgradeVersionAsync(GtnhPackDownloadViewModel versions, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var (window, body) = Create("选择新版 GTNH", "选择要下载的版本 · 旧实例保留，新版独立安装", 780, 710);
        window.Surface.DataContext = versions;
        var source = Text(""); source.SetBinding(TextBlock.TextProperty, nameof(versions.UpgradeHint));
        body.Children.Add(source);
        var filters = new WrapPanel { Margin = new(0, 14, 0, 4) };
        var java = new ComboBox { Width = 165, Margin = new(0, 0, 14, 8) };
        java.SetBinding(ItemsControl.ItemsSourceProperty, nameof(versions.JavaFilters));
        java.SetBinding(Selector.SelectedIndexProperty, new Binding(nameof(versions.JavaFilterIndex)) { Mode = BindingMode.TwoWay });
        java.SetBinding(UIElement.IsEnabledProperty, nameof(versions.CanBrowse));
        AutomationProperties.SetName(java, "升级版本 Java 筛选");
        filters.Children.Add(java);
        var prereleases = new CheckBox { Content = "包含测试版本", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 14, 8) };
        prereleases.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(versions.IncludePrereleases)) { Mode = BindingMode.TwoWay });
        prereleases.SetBinding(UIElement.IsEnabledProperty, nameof(versions.CanBrowse));
        AutomationProperties.SetName(prereleases, "包含 GTNH 测试版本");
        filters.Children.Add(prereleases);
        filters.Children.Add(new Button { Content = "刷新版本", Command = versions.RefreshCommand, Margin = new(0, 0, 0, 8) });
        body.Children.Add(filters);
        var status = Text(""); status.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        status.SetBinding(TextBlock.TextProperty, nameof(versions.Status));
        body.Children.Add(status);
        var loading = new ProgressBar { IsIndeterminate = true, Height = 3, Margin = new(0, 8, 0, 0) };
        loading.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(versions.IsLoading)) { Converter = new BooleanToVisibilityConverter() });
        body.Children.Add(loading);
        var releases = new ListBox
        {
            DisplayMemberPath = nameof(GtnhPackRelease.DisplayName), SelectionMode = SelectionMode.Single,
            Height = 210, Margin = new(0, 12, 0, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        VirtualizingPanel.SetIsVirtualizing(releases, true);
        VirtualizingPanel.SetVirtualizationMode(releases, VirtualizationMode.Recycling);
        releases.SetBinding(ItemsControl.ItemsSourceProperty, nameof(versions.Releases));
        releases.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(versions.SelectedRelease)) { Mode = BindingMode.TwoWay });
        releases.SetBinding(UIElement.IsEnabledProperty, nameof(versions.CanBrowse));
        AutomationProperties.SetName(releases, "可升级的 GTNH Prism 版本");
        body.Children.Add(releases);
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new(0), MinHeight = 62 };
        details.SetBinding(TextBox.TextProperty, new Binding(nameof(versions.ReleaseInformation)) { Mode = BindingMode.OneWay });
        AutomationProperties.SetName(details, "所选新版信息与可复制文件名");
        body.Children.Add(details);
        var warning = Text("测试版本可能存在兼容性或存档问题。请保留旧实例，先使用存档副本测试。");
        warning.Margin = new(0, 10, 0, 0); warning.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        warning.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(versions.IsPrereleaseSelection)) { Converter = new BooleanToVisibilityConverter() });
        body.Children.Add(warning);
        body.Children.Add(Text("确认后关闭弹窗，在版本升级页下载、导入并补全。此步骤不会迁移数据；关闭或取消不会开始下载。"));
        var accept = window.AddAccept("下载所选新版");
        bool CanAccept() => versions.IsUpgrade && versions.CanBrowse && !versions.IsLoading &&
            versions.SelectedRelease is { } release && versions.Releases.Contains(release);
        void Refresh(object? sender, PropertyChangedEventArgs args) => accept.IsEnabled = CanAccept();
        versions.PropertyChanged += Refresh;
        accept.IsEnabled = CanAccept();
        using var cancellation = token.Register(() =>
        {
            if (!window.Dispatcher.HasShutdownStarted)
                _ = window.Dispatcher.BeginInvoke(new Action(window.Close));
        });
        try
        {
            token.ThrowIfCancellationRequested();
            var accepted = _show(window) == true;
            token.ThrowIfCancellationRequested();
            return Task.FromResult(accepted && CanAccept() ? versions.SelectedRelease : null);
        }
        finally { versions.PropertyChanged -= Refresh; }
    }
}
