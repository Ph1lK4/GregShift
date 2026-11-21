using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EML.App.Services;
using EML.App.Views;

namespace EML.App.Tests;

/// <summary>真实目录视图使用隔离服务，验证列表、详情和返回而不创建游戏实例。</summary>
internal static class CatalogPageChecks
{
    /// <summary>在同一个 STA 资源环境中覆盖两类目录、两档缩放与主题。</summary>
    internal static void Run(Border host)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var fixture = new CatalogPageFixture();
        Wait(fixture.InitializeAsync());
        var directory = Path.Combine(Path.GetTempPath(), "EML.Catalog.Tests", Guid.NewGuid().ToString("N"));
        var resources = Application.Current.Resources.MergedDictionaries.ToArray();
        var values = Application.Current.Resources.Keys.Cast<object>().ToDictionary(key => key, key => Application.Current.Resources[key]);
        using var appearance = new AppearanceService(directory, watchSystemTheme: false);
        var width = host.Width; var height = host.Height;
        var originalBackground = host.Background;
        try
        {
            foreach (var theme in new[] { "dark", "light", "custom" })
            foreach (var scale in new[] { 1d, 2d })
            {
                appearance.Apply(appearance.Settings with
                {
                    ThemeMode = theme == "light" ? AppearanceThemeMode.Light : AppearanceThemeMode.Dark,
                    AccentHex = theme == "custom" ? "#EF8F8F" : "#D9B36A",
                    BackgroundHex = theme == "custom" ? "#12342A" : null
                });
                host.Background = (Brush)Application.Current.FindResource("WindowBackgroundBrush");
                host.Width = 1000; host.Height = 720;
                VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
                var suffix = $"{theme}-{(int)(96 * scale)}dpi";
                VerifyPacks(host, fixture, suffix, scale);
                VerifyMods(host, fixture, suffix, scale);
            }
            VerifyKeyboardNavigation(host);
            VerifyCompletedTaskActions(host, fixture);
            VerifyVanilla(host, fixture);
        }
        finally
        {
            host.Child = null; host.Width = width; host.Height = height;
            host.Background = originalBackground;
            VisualTreeHelper.SetRootDpi(host, new DpiScale(1, 1));
            Wait(fixture.StopAsync());
            Application.Current.Resources.Clear(); Application.Current.Resources.MergedDictionaries.Clear();
            foreach (var dictionary in resources) Application.Current.Resources.MergedDictionaries.Add(dictionary);
            foreach (var item in values) Application.Current.Resources[item.Key] = item.Value;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    /// <summary>预览事件不得抢走另一行按钮的 Enter；列表项本身仍可按 Enter 打开。</summary>
    private static void VerifyKeyboardNavigation(Border host)
    {
        var fixture = new CatalogPageFixture();
        Wait(fixture.InitializeAsync());
        using var source = new HwndSource(new HwndSourceParameters("EML catalog keyboard regression")
        {
            Width = 0, Height = 0, WindowStyle = 0
        });
        try
        {
            var packs = new InstanceDownloadsView { DataContext = fixture };
            host.Child = packs; Layout(host);
            VerifyProjectEnter((ListBox)packs.FindName("ProjectsList"), source,
                () => fixture.Downloads.IsDetailOpen, () => fixture.Downloads.SelectedProject,
                () => fixture.Downloads.BackToResultsCommand.Execute(null), host);

            var mods = new OnlineModsView { DataContext = fixture.Mods };
            host.Child = mods; Layout(host);
            VerifyProjectEnter((ListBox)mods.FindName("ProjectResults"), source,
                () => fixture.Mods.IsDetailOpen, () => fixture.Mods.SelectedProject,
                () => fixture.Mods.BackToResultsCommand.Execute(null), host);
        }
        finally { host.Child = null; Wait(fixture.StopAsync()); }
    }

    /// <summary>使用实际控件的路由事件和原生命令，分别验证按钮与列表行的键盘入口。</summary>
    private static void VerifyProjectEnter(ListBox list, PresentationSource source,
        Func<bool> isDetailOpen, Func<object?> selectedProject, Action back, Border host)
    {
        back(); list.SelectedIndex = 0; Layout(host);
        var first = list.Items[0]; var second = list.Items[1];
        var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(second));
        var button = Descendants(row).OfType<Button>().Single(item => ReferenceEquals(item.CommandParameter, second));
        Assert.Same(first, selectedProject()); Assert.False(isDetailOpen());

        var buttonPreview = RaiseKey(button, source, Keyboard.PreviewKeyDownEvent, Key.Enter);
        Assert.False(buttonPreview.Handled, "列表不能截走行内按钮的 Enter。");
        Assert.False(isDetailOpen()); Assert.Same(first, selectedProject());
        var buttonKey = RaiseKey(button, source, Keyboard.KeyDownEvent, Key.Enter);
        Assert.True(buttonKey.Handled);
        Assert.True(isDetailOpen()); Assert.Same(second, selectedProject());

        back(); list.SelectedIndex = 0; Layout(host);
        row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(first));
        var arrow = RaiseKey(row, source, Keyboard.PreviewKeyDownEvent, Key.Down);
        Assert.False(arrow.Handled); Assert.False(isDetailOpen());
        var rowEnter = RaiseKey(row, source, Keyboard.PreviewKeyDownEvent, Key.Enter);
        Assert.True(rowEnter.Handled);
        Assert.True(isDetailOpen()); Assert.Same(first, selectedProject());
        back(); Layout(host);
    }

    /// <summary>合成按键只进入生产事件路由，不向桌面或真实玩家窗口发送输入。</summary>
    private static KeyEventArgs RaiseKey(UIElement target, PresentationSource source, RoutedEvent routedEvent, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = routedEvent };
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>任务结束后不把当前选择伪装成旧任务详情；结果文字与新项目导航保持独立。</summary>
    private static void VerifyCompletedTaskActions(Border host, CatalogPageFixture fixture)
    {
        var packs = new InstanceDownloadsView { DataContext = fixture };
        host.Child = packs; Layout(host);
        fixture.Downloads.OpenDetailsCommand.Execute(fixture.Downloads.SelectedProject);
        Wait(fixture.Downloads.InstallCommand.ExecuteAsync(null));
        fixture.Downloads.BackToResultsCommand.Execute(null);
        fixture.Downloads.SelectedProject = fixture.Downloads.Projects[1];
        Wait(Task.WhenAll(fixture.Downloads.VersionsTask, fixture.Downloads.DetailsTask)); Layout(host);
        Assert.True(fixture.Downloads.HasInstallActivity);
        Assert.Equal(Visibility.Collapsed, Descendants(packs).OfType<Button>().Single(button => Equals(button.Content, "查看安装详情")).Visibility);

        var mods = new OnlineModsView { DataContext = fixture.Mods };
        host.Child = mods; Layout(host);
        Wait(fixture.Mods.InstallCommand.ExecuteAsync(null));
        fixture.Mods.BackToResultsCommand.Execute(null);
        fixture.Mods.SelectedProject = fixture.Mods.Projects[1];
        Wait(fixture.Mods.DetailTask); Wait(fixture.Mods.VersionTask); Layout(host);
        Assert.True(fixture.Mods.HasInstallActivity);
        Assert.Equal(Visibility.Collapsed, Descendants(mods).OfType<Button>().Single(button => Equals(button.Content, "查看安装详情")).Visibility);
    }

    /// <summary>官方原版的筛选和详情共享导航，平台介绍不应错误显示。</summary>
    private static void VerifyVanilla(Border host, CatalogPageFixture fixture)
    {
        var model = fixture.Downloads;
        model.ProviderIndex = 2; Wait(model.SearchTask);
        var view = new InstanceDownloadsView { DataContext = fixture };
        host.Child = view; Layout(host);
        Assert.True(model.IsMinecraft); Assert.False(model.IsDetailOpen);
        model.OpenDetailsCommand.Execute(model.Projects[0]); Wait(model.VersionsTask); Layout(host);
        Assert.True(model.IsDetailOpen); Assert.NotNull(model.SelectedRelease);
        var description = (TextBox)view.FindName("ProjectDescriptionText");
        Assert.Contains(Ancestors(description).OfType<UIElement>(), item => item.Visibility == Visibility.Collapsed);
        var release = model.SelectedRelease;
        model.BackToResultsCommand.Execute(null); model.OpenDetailsCommand.Execute(model.SelectedProject);
        Assert.Same(release, model.SelectedRelease);
    }

    /// <summary>检查实际父级可见性，不用未显示窗口的 IsVisible 作为判断。</summary>
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        while (VisualTreeHelper.GetParent(element) is { } parent) { yield return parent; element = parent; }
    }

    /// <summary>从实际详情按钮打开整合包，返回保留精确版本；原版共享同一导航。</summary>
    private static void VerifyPacks(Border host, CatalogPageFixture fixture, string suffix, double scale)
    {
        var model = fixture.Downloads;
        model.BackToResultsCommand.Execute(null);
        var view = new InstanceDownloadsView { DataContext = fixture };
        host.Child = view; Layout(host);
        var list = (ListBox)view.FindName("ProjectsList");
        var browse = (Grid)view.FindName("BrowsePage");
        var detail = (Grid)view.FindName("DetailsPage");
        Assert.Equal(Visibility.Visible, browse.Visibility); Assert.Equal(Visibility.Collapsed, detail.Visibility);
        Assert.False(model.IsDetailOpen);
        Assert.True(list.ActualWidth > view.ActualWidth * .85);
        Write(host, $"packs-list-{suffix}.png", scale);
        Click(Descendants(list).OfType<Button>().First(button => button.Command == model.OpenDetailsCommand));
        Wait(Task.WhenAll(model.VersionsTask, model.DetailsTask)); Layout(host);
        Assert.True(model.IsDetailOpen); Assert.Equal(Visibility.Collapsed, browse.Visibility); Assert.Equal(Visibility.Visible, detail.Visibility);
        var selector = Descendants(detail).OfType<ComboBox>().Single(combo =>
            BindingOperations.GetBinding(combo, Selector.SelectedItemProperty)?.Path.Path == "SelectedRelease");
        selector.SelectedItem = model.Releases[1]; Layout(host);
        var selected = model.SelectedRelease;
        var description = (TextBox)view.FindName("ProjectDescriptionText");
        Assert.True(description.IsReadOnly); Assert.Contains("English description", description.Text);
        Write(host, $"packs-details-{suffix}.png", scale);
        Click((Button)view.FindName("BackToResultsButton")); Layout(host);
        Assert.False(model.IsDetailOpen); Assert.Same(selected, model.SelectedRelease);
        Assert.Equal(Visibility.Visible, browse.Visibility); Assert.Equal(Visibility.Collapsed, detail.Visibility);
        model.OpenDetailsCommand.Execute(model.SelectedProject); Layout(host);
        Assert.Same(selected, selector.SelectedItem);
        VerifySmallDetail(host, view, "下载并创建实例");
        model.BackToResultsCommand.Execute(null); Layout(host);
    }

    /// <summary>模组详情保留版本、可选依赖和生成的清单，正文页不再与搜索结果抢宽度。</summary>
    private static void VerifyMods(Border host, CatalogPageFixture fixture, string suffix, double scale)
    {
        var model = fixture.Mods;
        model.BackToResultsCommand.Execute(null);
        var view = new OnlineModsView { DataContext = model };
        host.Child = view; Layout(host);
        var list = (ListBox)view.FindName("ProjectResults");
        var browse = (Grid)view.FindName("ResultsPage");
        var detail = (Grid)view.FindName("DetailsPage");
        Assert.Equal(Visibility.Visible, browse.Visibility); Assert.Equal(Visibility.Collapsed, detail.Visibility);
        Assert.True(list.ActualWidth > view.ActualWidth * .85);
        Write(host, $"mods-list-{suffix}.png", scale);
        Click(Descendants(list).OfType<Button>().First(button => button.Command == model.OpenDetailsCommand));
        Wait(model.DetailTask); Wait(model.VersionTask); Layout(host);
        Assert.Equal(Visibility.Visible, detail.Visibility); Assert.Equal(Visibility.Collapsed, browse.Visibility);
        Write(host, $"mods-overview-{suffix}.png", scale);
        var tabs = (TabControl)view.FindName("DetailTabs");
        tabs.SelectedIndex = 1; Layout(host);
        var selector = Descendants(detail).OfType<ComboBox>().Single(combo =>
            BindingOperations.GetBinding(combo, Selector.SelectedItemProperty)?.Path.Path == "SelectedVersion");
        selector.SelectedItem = model.Versions[1]; Wait(model.VersionTask); Layout(host);
        model.OptionalDependencies[0].IsSelected = true;
        Wait(model.PreviewCommand.ExecuteAsync(null)); Layout(host);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); Layout(host);
        var installScroll = (ScrollViewer)view.FindName("InstallScroll");
        var heading = (TextBlock)view.FindName("PlanHeading");
        var headingBounds = heading.TransformToAncestor(installScroll).TransformBounds(new Rect(heading.RenderSize));
        Assert.True(headingBounds.Top >= -1 && headingBounds.Bottom <= installScroll.ActualHeight + 1,
            "生成清单后应能立即看到清单标题与摘要。");
        var plan = model.Plan; var version = model.SelectedVersion;
        Assert.NotNull(plan);
        Write(host, $"mods-install-{suffix}.png", scale);
        Click((Button)view.FindName("BackButton")); Layout(host);
        Assert.False(model.IsDetailOpen); Assert.Same(plan, model.Plan); Assert.Same(version, model.SelectedVersion);
        Assert.True(model.OptionalDependencies[0].IsSelected);
        model.OpenDetailsCommand.Execute(model.SelectedProject); Layout(host);
        Assert.Same(plan, model.Plan); Assert.Equal(1, tabs.SelectedIndex);
        VerifySmallDetail(host, view, "确认下载并安装");
        model.BackToResultsCommand.Execute(null); Layout(host);
    }

    /// <summary>小内容区仍能看见主安装动作，详情正文以滚动承载而非挤压字体。</summary>
    private static void VerifySmallDetail(Border host, UserControl view, string buttonText)
    {
        host.Width = 720; host.Height = 520; Layout(host);
        var button = Descendants(view).OfType<Button>().Single(item => Equals(item.Content, buttonText));
        var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
        Assert.True(button.Visibility == Visibility.Visible && bounds.Top >= 0 && bounds.Bottom <= view.ActualHeight + 1, buttonText + " 应可见。");
        Assert.True(bounds.Left >= 0 && bounds.Right <= view.ActualWidth + 1);
        host.Width = 1000; host.Height = 720; Layout(host);
    }

    /// <summary>从真实绑定入口执行，不绕过命令的可用性约束。</summary>
    private static void Click(Button button)
    {
        Assert.NotNull(button.Command); Assert.True(button.Command.CanExecute(button.CommandParameter));
        button.Command.Execute(button.CommandParameter);
    }

    /// <summary>等待生产 ViewModel 的异步完成，同时允许 STA 处理绑定回调。</summary>
    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Send,
                (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            Dispatcher.PushFrame(frame); timeout.Stop();
            Assert.True(task.IsCompleted, "目录任务应在测试时间内完成。");
        }
        task.GetAwaiter().GetResult();
    }

    /// <summary>刷新生产控件模板和当前布局。</summary>
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(root.Width, root.Height)); root.Arrange(new Rect(0, 0, root.Width, root.Height)); root.UpdateLayout();
    }

    /// <summary>只遍历当前渲染树，不创建隐藏标签页的测试替身。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }

    /// <summary>指定输出目录时保存真实视图证据，不将截图加入发布包。</summary>
    private static void Write(FrameworkElement root, string name, double scale)
    {
        var directory = Environment.GetEnvironmentVariable("EML_TEXTBOX_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }
}
