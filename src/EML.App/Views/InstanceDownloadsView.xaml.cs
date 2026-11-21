using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EML.App.ViewModels;

namespace EML.App.Views;

/// <summary>独立新实例下载页面，业务状态与请求由 ViewModel 管理。</summary>
public partial class InstanceDownloadsView : UserControl
{
    private object? _lastDetailProject;

    /// <summary>加载共享主题下的目录与版本控件，不在构造时发起网络请求。</summary>
    public InstanceDownloadsView() => InitializeComponent();

    /// <summary>点击项目正文时打开详情；滚动条和行内按钮由各自的原生交互处理。</summary>
    private void OnProjectMouseUp(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(ProjectsList, source) is not ListBoxItem { DataContext: InstanceDownloadRow row }) return;
        OpenProject(row);
    }

    /// <summary>仅列表行自身的 Enter 打开详情，行内按钮保留自己的键盘命令。</summary>
    private void OnProjectKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter ||
            args.OriginalSource is not ListBoxItem { DataContext: InstanceDownloadRow row } item ||
            !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), ProjectsList)) return;
        OpenProject(row);
        args.Handled = true;
    }

    /// <summary>仅转发页面命令，实际版本读取和目标规则仍由 ViewModel 决定。</summary>
    private void OpenProject(InstanceDownloadRow row)
    {
        if ((Content as FrameworkElement)?.DataContext is InstanceDownloadsViewModel model && model.OpenDetailsCommand.CanExecute(row))
            model.OpenDetailsCommand.Execute(row);
    }

    /// <summary>进入详情后聚焦返回入口；换项目从顶部开始，同项目保留正文滚动。</summary>
    private void OnDetailsVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true || !IsLoaded) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!DetailsPage.IsVisible) return;
            var project = ((Content as FrameworkElement)?.DataContext as InstanceDownloadsViewModel)?.SelectedProject;
            if (!ReferenceEquals(project, _lastDetailProject)) DetailScroll.ScrollToTop();
            _lastDetailProject = project;
            BackToResultsButton.Focus();
        }));
    }

    /// <summary>返回时恢复原结果焦点，不重建或重置搜索列表。</summary>
    private void OnBrowseVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true || !IsLoaded) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!BrowsePage.IsVisible) return;
            if (ProjectsList.ItemContainerGenerator.ContainerFromItem(ProjectsList.SelectedItem) is ListBoxItem item) item.Focus();
            else SearchInput.Focus();
        }));
    }

    /// <summary>下拉框先处理自己的 Esc，其余详情区域按 Esc 返回，不影响安装。</summary>
    private void OnPageKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || (Content as FrameworkElement)?.DataContext is not InstanceDownloadsViewModel { IsDetailOpen: true } model) return;
        model.BackToResultsCommand.Execute(null);
        args.Handled = true;
    }
}
