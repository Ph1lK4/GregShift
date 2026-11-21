using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using EML.App.Services;
using EML.App.ViewModels;

namespace EML.App.Views;

/// <summary>在线模组页只提供绑定布局，查询与安装状态由独立 ViewModel 管理。</summary>
public partial class OnlineModsView : UserControl
{
    private OnlineModResultRow? _openedProject;
    private bool _returningFromDetail;

    /// <summary>初始化页面，不在构造期间发起网络请求。</summary>
    public OnlineModsView() => InitializeComponent();

    /// <summary>单击结果内容打开对应项目；按钮使用自己的命令，不依赖列表选择事件。</summary>
    private void OnProjectClick(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(ProjectResults, source) is not ListBoxItem { DataContext: OnlineModResultRow project }) return;
        OpenDetails(project);
    }

    /// <summary>方向键只移动选择；行自身的 Enter 打开详情，不截走行内按钮的命令。</summary>
    private void OnProjectKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter ||
            args.OriginalSource is not ListBoxItem { DataContext: OnlineModResultRow project } item ||
            !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), ProjectResults)) return;
        OpenDetails(project);
        args.Handled = true;
    }

    /// <summary>统一鼠标与键盘入口，是否允许打开仍由页面模型判断。</summary>
    private void OpenDetails(OnlineModResultRow project)
    {
        if (DataContext is OnlineModsViewModel model && model.OpenDetailsCommand.CanExecute(project))
            model.OpenDetailsCommand.Execute(project);
    }

    /// <summary>新项目先展示介绍，同一项目重开保留当前分区；焦点进入可见的返回入口。</summary>
    private void OnDetailsVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true || DataContext is not OnlineModsViewModel model) return;
        if (!ReferenceEquals(_openedProject, model.SelectedProject)) DetailTabs.SelectedIndex = 0;
        _openedProject = model.SelectedProject;
        _returningFromDetail = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (DetailsPage.IsVisible) BackButton.Focus();
        }));
    }

    /// <summary>返回时恢复结果项的键盘位置，不重建列表或请求搜索。</summary>
    private void OnResultsVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true || !_returningFromDetail) return;
        _returningFromDetail = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!ResultsPage.IsVisible) return;
            if (ProjectResults.ItemContainerGenerator.ContainerFromItem(ProjectResults.SelectedItem) is ListBoxItem item) item.Focus();
            else SearchInput.Focus();
        }));
    }

    /// <summary>介绍中的主动作跳到版本区，不改变已经选好的版本或依赖。</summary>
    private void OnChooseVersion(object sender, RoutedEventArgs args)
    {
        DetailTabs.SelectedIndex = 1;
        if (DetailTabs.Items[1] is TabItem tab) tab.Focus();
    }

    /// <summary>清单生成后将标题和摘要带入视口，不改变键盘焦点或隐藏页面的滚动位置。</summary>
    private void OnPlanUpdated(object sender, DataTransferEventArgs args)
    {
        if (args.Property != TagProperty || PlanSection.Tag is null ||
            DetailsPage.Visibility != Visibility.Visible || DetailTabs.SelectedIndex != 1) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (PlanSection.Tag is null || DetailsPage.Visibility != Visibility.Visible || DetailTabs.SelectedIndex != 1) return;
            PlanSection.UpdateLayout();
            PlanSection.BringIntoView(new Rect(0, 0, PlanSection.ActualWidth, Math.Min(PlanSection.ActualHeight, 140)));
        }));
    }

    /// <summary>下拉框先消费自己的 Esc，其余详情区域可用 Esc 返回列表。</summary>
    private void OnPageKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || DataContext is not OnlineModsViewModel { IsDetailOpen: true } model) return;
        model.BackToResultsCommand.Execute(null);
        args.Handled = true;
    }
}

/// <summary>为版本与加载器选项提供中文展示，不更改绑定对象中的平台值。</summary>
public sealed class OnlineModLabelConverter : IValueConverter
{
    /// <summary>通过参数区分版本通道和加载器，未知转换类型保持原文。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (parameter as string) switch
    {
        "Channel" => OnlineModDisplayText.Channel(value as string),
        "Loader" => OnlineModDisplayText.Loader(value as string),
        _ => value
    };

    /// <summary>标签仅用于单向展示，不允许把中文显示值回写到筛选模型。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
