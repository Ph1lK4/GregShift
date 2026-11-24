using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EML.App.Controls;
using EML.App.Services;
using EML.App.ViewModels;
using EML.App.Views;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.Tests;

/// <summary>复用唯一 STA/Application；声明式图表来自内存服务，不初始化浏览器或访问站点。</summary>
internal static class GtnhToolsViewChecks
{
    internal static void Run(Border host)
    {
        var service = new FlowChartPreview();
        using var model = new GtnhToolsViewModel(service) { BrowserDataFolder = @"C:\EML-test-only\cache\gtnh-tools\webview2" };
        model.ShowFlowChartsCommand.Execute(null);
        var page = new GtnhToolsView { DataContext = model };
        var browser = Assert.IsType<GtnhToolBrowser>(page.FindName("Browser"));
        var planner = Assert.IsType<GtnhPlannerPane>(page.FindName("Planner"));
        try
        {
            host.Child = page;
            Flush(host);
            var charts = Assert.IsType<ListBox>(page.FindName("ChartList"));
            var browse = Assert.IsType<Grid>(page.FindName("BrowsePage"));
            var detail = Assert.IsType<Grid>(page.FindName("DetailsPage"));
            var canvas = Assert.IsType<GtnhFlowCanvas>(page.FindName("FlowCanvas"));
            Assert.Equal(GtnhToolsCatalog.FlowCharts.Count, charts.Items.Count);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(charts));
            Assert.Equal(Visibility.Visible, browse.Visibility);
            Assert.Equal(Visibility.Collapsed, detail.Visibility);
            Assert.False(browser.IsBrowserCreated);
            Assert.False(browser.IsActive);
            Assert.False(planner.Browser.IsBrowserCreated);
            Assert.False(planner.Browser.IsActive);
            Assert.False(IsVisibleWithin(planner, page));

            // 键盘选择或筛选只改变目录，不触发读取或跳到原站。
            charts.SelectedItem = model.Charts.First(chart => chart.IsCreated);
            Flush(host);
            Assert.Same(model.SelectedChart, charts.SelectedItem);
            Assert.True(model.IsChartList);
            Assert.Equal(0, service.Reads);
            Assert.Null(browser.RequestedAddress);
            var open = Assert.Single(Descendants(charts).OfType<Button>(), button =>
                ReferenceEquals(button.Command, model.OpenDetailsCommand) && ReferenceEquals(button.CommandParameter, model.SelectedChart));
            Assert.Same(model.SelectedChart, open.CommandParameter);

            // 红链保留目录位置；明确打开后显示本地等待页，不请求编辑页面。
            var missing = model.Charts.Single(chart => chart.Name == "钢");
            var originalPosition = model.Charts.IndexOf(missing);
            charts.SelectedItem = missing;
            Flush(host);
            Assert.False(model.IsMissingChart);
            model.OpenDetailsCommand.Execute(missing);
            Flush(host);
            Assert.True(model.IsMissingChart);
            Assert.Equal(Visibility.Visible, detail.Visibility);
            Assert.Equal(Visibility.Collapsed, browse.Visibility);
            Assert.Null(browser.RequestedAddress);
            Assert.Contains("未创建，等待更新", VisibleText(page));
            Assert.Contains(VisibleText(page), text => text.Contains("原站还没有创建这张流程图"));
            Assert.Equal(0, service.Reads);
            Assert.Same(missing, model.Charts[originalPosition]);
            model.BackToChartsCommand.Execute(null);
            Flush(host);

            var search = Assert.IsType<TextBox>(page.FindName("SearchInput"));
            Assert.Equal("搜索流程图", AutomationProperties.GetName(search));
            search.SetCurrentValue(TextBox.TextProperty, "不存在的流程图测试条目");
            Flush(host);
            Assert.Empty(charts.Items.Cast<object>());
            Assert.Contains("没有匹配的流程图。", VisibleText(page));
            Assert.DoesNotContain("未创建，等待更新", VisibleText(page));
            Assert.Equal(0, service.Reads);

            var sections = Assert.Single(Descendants(page).OfType<ComboBox>(), combo =>
                AutomationProperties.GetName(combo) == "流程图目录分组");
            search.SetCurrentValue(TextBox.TextProperty, "硅");
            sections.SetCurrentValue(Selector.SelectedItemProperty, "工艺流程图");
            Flush(host);
            Assert.Equal("硅", model.SearchText);
            Assert.All(model.Charts, chart => Assert.Equal("工艺流程图", chart.Section));
            Assert.All(model.Charts, chart => Assert.Contains("硅", chart.Name));
            search.SetCurrentValue(TextBox.TextProperty, " ");
            Flush(host);
            Assert.Equal(" ", model.SearchText);
            Assert.Equal(GtnhToolsCatalog.FlowCharts.Count(chart => chart.Section == "工艺流程图"), charts.Items.Count);
            var selected = model.Charts.Last(chart => chart.IsCreated);
            charts.SelectedItem = selected;
            charts.ScrollIntoView(selected);
            Flush(host);
            var scroll = Assert.Single(Descendants(charts).OfType<ScrollViewer>());
            // 离屏 host 没有 Loaded/BringIntoView 路由，直接模拟滚动条操作。
            scroll.ScrollToBottom();
            Flush(host);
            var savedOffset = scroll.VerticalOffset;
            Assert.True(savedOffset > 0, $"目录必须实际发生滚动：items={charts.Items.Count}, extent={scroll.ExtentHeight}, viewport={scroll.ViewportHeight}。");

            model.OpenDetailsCommand.Execute(selected);
            Flush(host);
            Assert.True(model.ChartLoadTask.IsCompletedSuccessfully);
            Assert.Equal(1, service.Reads);
            Assert.True(model.HasGraph);
            Assert.True(model.ShowCanvas);
            Assert.Same(model.CurrentView, canvas.View);
            Assert.Equal("zh", canvas.View!.Language);
            Assert.True(IsVisibleWithin(canvas, page));
            Assert.False(browser.IsBrowserCreated);
            Assert.Null(browser.RequestedAddress);
            Assert.Contains(VisibleText(page), text => text.Contains(GtnhFlowChartContent.LicenseName));
            VerifyDpiLayout(host, page, model, charts, canvas);

            var groups = Assert.Single(Descendants(page).OfType<ComboBox>(), combo =>
                AutomationProperties.GetName(combo) == "高亮流程图分组");
            groups.SetCurrentValue(Selector.SelectedItemProperty, model.Groups.Single(group => group.Id == "route:prime"));
            Flush(host);
            Assert.Equal("route:prime", canvas.HighlightGroup);
            var language = Assert.Single(Descendants(page).OfType<CheckBox>(), check => Equals(check.Content, "English"));
            Assert.True(language.IsEnabled);
            language.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Flush(host);
            Assert.True(model.IsEnglish);
            Assert.Equal("en", canvas.View!.Language);
            Assert.Same(model.CurrentView, canvas.View);
            Assert.Equal("route:prime", canvas.HighlightGroup);

            var source = Assert.Single(Descendants(page).OfType<TextBox>(), box =>
                AutomationProperties.GetName(box) == "流程图声明式数据");
            model.ToggleSourceCommand.Execute(null);
            Flush(host);
            Assert.True(model.ShowSource);
            Assert.False(model.ShowCanvas);
            Assert.True(source.IsReadOnly);
            Assert.Equal(service.Payload, source.Text);
            Assert.True(IsVisibleWithin(source, page));
            Assert.False(IsVisibleWithin(canvas, page));
            model.ToggleSourceCommand.Execute(null);
            Flush(host);
            Assert.True(IsVisibleWithin(canvas, page));

            // 目录始终挂载，返回后不重建列表、不丢筛选或滚动位置。
            model.BackToChartsCommand.Execute(null);
            Flush(host);
            Assert.True(model.IsChartList);
            Assert.Same(charts, page.FindName("ChartList"));
            Assert.Equal(" ", search.Text);
            Assert.Equal("工艺流程图", sections.SelectedItem);
            Assert.InRange(scroll.VerticalOffset, savedOffset - .01, savedOffset + .01);
            Assert.Null(browser.RequestedAddress);

            // 数据读取失败使用原生可复制错误，重试仍由同一个服务读取。
            service.FailNext = true;
            model.OpenDetailsCommand.Execute(selected);
            Flush(host);
            Assert.True(model.ShowChartError);
            Assert.False(model.HasBrowser);
            Assert.Contains("这张流程图暂时无法在本地显示", VisibleText(page));
            AssertReadOnlyError(page, model.Error);
            var retryChart = Assert.Single(Descendants(page).OfType<Button>(), button => Equals(button.Content, "重试读取"));
            Assert.Same(model.RefreshChartCommand, retryChart.Command);
            retryChart.Command.Execute(null);
            Flush(host);
            Assert.True(model.HasGraph);
            Assert.False(model.HasError);
            Assert.Equal(1, service.Refreshes);
            model.BackToChartsCommand.Execute(null);
            Flush(host);

            var quest = ToolButton(page, "在线任务书");
            Assert.Same(model.OpenToolCommand, quest.Command);
            quest.Command.Execute(quest.CommandParameter);
            Flush(host);
            Assert.True(model.IsQuestBook);
            Assert.Equal(GtnhToolsCatalog.QuestBookUri.AbsoluteUri, browser.RequestedAddress);
            var addressBar = Assert.IsType<DockPanel>(page.FindName("BrowserAddressBar"));
            Assert.Equal(Visibility.Collapsed, addressBar.Visibility);
            Assert.DoesNotContain(Descendants(page).OfType<TextBox>(), text => IsVisibleWithin(text, page) && text.IsReadOnly &&
                text.Text == GtnhToolsCatalog.QuestBookUri.AbsoluteUri);
            var copyAddress = Assert.Single(Descendants(page).OfType<Button>(), button => Equals(button.Content, "复制链接"));
            Assert.False(IsVisibleWithin(copyAddress, page));
            Assert.True(IsVisibleWithin(Assert.Single(Descendants(page).OfType<Button>(), button => Equals(button.Content, "返回")), page));
            Assert.True(IsVisibleWithin(Assert.Single(Descendants(page).OfType<Button>(), button => Equals(button.Content, "刷新") && IsVisibleWithin(button, page)), page));

            // 运行时缺失或网络失败时，任务书仍保留原生导航和可复制错误。
            model.UpdateBrowserState(model.RequestedAddress, false, false, false, "测试：未安装 WebView2 运行时。", true);
            Flush(host);
            Assert.Contains("网页暂时无法显示", VisibleText(page));
            AssertReadOnlyError(page, model.Error);
            var retry = Assert.Single(Descendants(page).OfType<Button>(), button => Equals(button.Content, "重试加载") && IsVisibleWithin(button, page));
            var runtime = Assert.Single(Descendants(page).OfType<Button>(), button => Equals(button.Content, "WebView2 安装帮助 ↗"));
            Assert.True(IsVisibleWithin(retry, page));
            Assert.True(IsVisibleWithin(runtime, page));
            AssertInside(page, retry);
            AssertInside(page, runtime);
            model.BackToToolsCommand.Execute(null); Flush(host);
            var flow = ToolButton(page, "工艺流程图");
            Assert.True(IsVisibleWithin(flow, page));
            flow.Command.Execute(flow.CommandParameter);
            Flush(host);
            Assert.True(model.IsChartList);
            Assert.False(model.HasError);
            Assert.Null(browser.RequestedAddress);
            Assert.Equal(" ", model.SearchText);
            Assert.Equal("工艺流程图", model.SelectedSection);
            Assert.InRange(scroll.VerticalOffset, savedOffset - .01, savedOffset + .01);
            Assert.False(browser.IsBrowserCreated);

            // 规划器保持独立 profile；未激活工具页不能建立浏览器或访问网页。
            var plannerTab = ToolButton(page, "产线规划器");
            Assert.Same(model.OpenToolCommand, plannerTab.Command);
            plannerTab.Command.Execute(plannerTab.CommandParameter);
            Flush(host);
            Assert.True(model.IsPlanner);
            Assert.True(IsVisibleWithin(planner, page));
            Assert.False(IsVisibleWithin(browse, page));
            Assert.False(IsVisibleWithin(detail, page));
            Assert.Null(browser.RequestedAddress);
            Assert.False(browser.IsActive);
            Assert.True(planner.Browser.PlannerMode);
            Assert.Equal("https://gtnhplanner.com/", planner.Browser.RequestedAddress);
            Assert.Equal(@"C:\EML-test-only\cache\gtnh-tools\planner-webview2", planner.Browser.UserDataFolder);
            Assert.NotEqual(browser.UserDataFolder, planner.Browser.UserDataFolder);
            Assert.False(planner.Browser.IsActive);
            Assert.False(planner.Browser.IsBrowserCreated);
            var plannerSearch = Assert.IsType<ComboBox>(planner.FindName("ItemSearch"));
            Assert.True(plannerSearch.IsEditable);
            Assert.Equal("中文或英文物品搜索", AutomationProperties.GetName(plannerSearch));
            VerifyPlannerRepeatedSearch(host, plannerSearch);
            VerifyPlannerDpiLayout(host, page, planner);
            flow.Command.Execute(flow.CommandParameter);
            Flush(host);
            Assert.True(model.IsChartList);
            Assert.False(IsVisibleWithin(planner, page));
            Assert.Same(charts, page.FindName("ChartList"));
            Assert.Same(selected, model.SelectedChart);
            Assert.Equal(" ", model.SearchText);
            Assert.Equal("工艺流程图", model.SelectedSection);
            Assert.InRange(scroll.VerticalOffset, savedOffset - .01, savedOffset + .01);
            Assert.False(planner.Browser.IsActive);
            Assert.False(planner.Browser.IsBrowserCreated);

            // 地址只在任务书隐藏；流程图原站仍保留来源和手动复制入口。
            model.OpenDetailsCommand.Execute(selected);
            model.ViewOriginalCommand.Execute(null);
            Flush(host);
            Assert.True(model.IsViewingOriginal);
            Assert.Equal(Visibility.Visible, addressBar.Visibility);
            Assert.True(IsVisibleWithin(copyAddress, page));
            Assert.Contains(Descendants(page).OfType<TextBox>(), text => IsVisibleWithin(text, page) && text.IsReadOnly &&
                text.Text == selected.Address.AbsoluteUri);
            Assert.False(browser.IsBrowserCreated);
        }
        finally
        {
            host.Child = null;
            browser.Dispose();
            planner.Browser.Dispose();
        }
    }

    private static Button ToolButton(FrameworkElement page, string name) => Assert.Single(Descendants(page).OfType<Button>(),
        button => AutomationProperties.GetName(button) == name);

    private static void VerifyPlannerRepeatedSearch(Border host, ComboBox search)
    {
        search.ApplyTemplate();
        var editor = Assert.IsType<TextBox>(search.Template.FindName("PART_EditableTextBox", search));
        foreach (var (query, english) in new[] { ("铁锭", "Iron Ingot"), ("铜锭", "Copper Ingot"), ("Tin Ingot", "Tin Ingot"), ("金锭", "Gold Ingot") })
        {
            editor.SelectAll();
            editor.SelectedText = query;
            Flush(host);
            Assert.Equal(query, editor.Text);
            Assert.Equal(query, search.Text);
            Assert.Null(search.SelectedItem);
            var candidate = Assert.Single(search.Items.OfType<GtnhPlannerSearchItem>(), item => item.English == english);
            search.SelectedItem = candidate;
            Flush(host);
            Assert.Same(candidate, search.SelectedItem);
            Assert.Equal(candidate.Label, editor.Text);
        }
        // 清空和再次逐字输入不能继续引用上一次选中的金锭。
        editor.Clear();
        Flush(host);
        Assert.Null(search.SelectedItem);
        Assert.Empty(search.Items);
        editor.Text = "铁";
        Flush(host);
        editor.CaretIndex = editor.Text.Length;
        editor.SelectedText = "锭";
        var selection = (editor.SelectionStart, editor.SelectionLength);
        Flush(host);
        Assert.Equal("铁锭", editor.Text);
        Assert.Equal(selection, (editor.SelectionStart, editor.SelectionLength));
        Assert.Contains(search.Items.OfType<GtnhPlannerSearchItem>(), item => item.English == "Iron Ingot");
    }

    private static void VerifyPlannerDpiLayout(Border host, GtnhToolsView page, GtnhPlannerPane planner)
    {
        // 离屏 host 不在 Application.Windows 中；局部资源能让已挂载树确实收到换色通知。
        var resources = host.Resources;
        var keys = AppearancePalette.Create(true).Keys.Append("WallpaperPanelBrush").ToArray();
        var previous = keys.ToDictionary(key => key, key => resources.Contains(key) ? resources[key] : null);
        var originalDpi = VisualTreeHelper.GetDpi(host);
        var originalBackground = host.ReadLocalValue(Border.BackgroundProperty);
        try
        {
            host.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
            foreach (var dark in new[] { true, false })
            {
                var palette = AppearancePalette.Create(dark);
                foreach (var color in palette) resources[color.Key] = new SolidColorBrush(color.Value);
                resources["WallpaperPanelBrush"] = new SolidColorBrush(palette["SurfaceBrush"]);
                foreach (var scale in new[] { 1d, 2d })
                {
                    VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
                    Flush(host);
                    Assert.Same(resources["TextBrush"], planner.Foreground);
                    Assert.Equal(scale, VisualTreeHelper.GetDpi(planner).DpiScaleX);
                    foreach (var name in new[] { "ChineseToggle", "ItemSearch", "SearchButton", "ImportButton", "ExportButton", "StatusText", "SearchGuideTitle", "SearchGuideSteps", "SearchGuideBoundary" })
                    {
                        var control = Assert.IsAssignableFrom<FrameworkElement>(planner.FindName(name));
                        Assert.True(IsVisibleWithin(control, page));
                        AssertInside(page, control);
                    }
                    Assert.False(planner.Browser.IsBrowserCreated);
                    WritePreview(host, $"gtnh-planner-{(dark ? "dark" : "light")}-{(int)(scale * 100)}.png", scale);
                }
            }
        }
        finally
        {
            foreach (var item in previous) if (item.Value is null) resources.Remove(item.Key); else resources[item.Key] = item.Value;
            VisualTreeHelper.SetRootDpi(host, originalDpi);
            if (originalBackground == DependencyProperty.UnsetValue) host.ClearValue(Border.BackgroundProperty);
            else host.SetValue(Border.BackgroundProperty, originalBackground);
            Flush(host);
        }
    }

    private static void VerifyDpiLayout(Border host, GtnhToolsView page, GtnhToolsViewModel model,
        ListBox charts, GtnhFlowCanvas canvas)
    {
        var originalDpi = VisualTreeHelper.GetDpi(host);
        var originalBackground = host.ReadLocalValue(Border.BackgroundProperty);
        var originalDetail = model.IsDetailOpen;
        var scroll = Assert.Single(Descendants(charts).OfType<ScrollViewer>());
        var originalOffset = scroll.VerticalOffset;
        var layoutProblems = new List<string>();
        void CheckInside(FrameworkElement element)
        {
            var bounds = element.TransformToAncestor(page).TransformBounds(new Rect(element.RenderSize));
            if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Left < -1 || bounds.Right > page.ActualWidth + 1 ||
                bounds.Top < -1 || bounds.Bottom > page.ActualHeight + 1)
                layoutProblems.Add($"{VisualTreeHelper.GetDpi(host).DpiScaleX:P0} {element.GetType().Name} " +
                    $"{(element as ContentControl)?.Content}: {bounds}; 页面 {page.ActualWidth}×{page.ActualHeight}。");
        }
        try
        {
            host.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
            foreach (var scale in new[] { 1d, 2d })
            {
                VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
                // 仅切换可见布局，不重新读取数据，保持当前语言、分组与图表对象。
                model.IsDetailOpen = false;
                Flush(host);
                Assert.Equal(scale, VisualTreeHelper.GetDpi(host).DpiScaleX);
                Assert.Equal(scale, VisualTreeHelper.GetDpi(page).DpiScaleX);
                var search = Assert.IsType<TextBox>(page.FindName("SearchInput"));
                Assert.True(IsVisibleWithin(search, page));
                CheckInside(search);
                CheckInside(charts);
                var sections = Assert.Single(Descendants(page).OfType<ComboBox>(), combo =>
                    AutomationProperties.GetName(combo) == "流程图目录分组");
                CheckInside(sections);
                model.IsDetailOpen = true;
                Flush(host);
                var back = Assert.Single(Descendants(page).OfType<Button>(), button => ReferenceEquals(button.Command, model.BackToChartsCommand));
                Assert.True(IsVisibleWithin(back, page));
                CheckInside(back);
                foreach (var control in Descendants(page).OfType<Control>().Where(control =>
                    IsVisibleWithin(control, page) && (control is ComboBox || control is CheckBox ||
                    control is Button { ToolTip: "缩小" or "放大" } ||
                    control is Button button && (Equals(button.Content, "适应窗口") || ReferenceEquals(button.Command, model.ToggleSourceCommand)))))
                    CheckInside(control);
                Assert.True(IsVisibleWithin(canvas, page));
                CheckInside(canvas);
                Assert.True(canvas.ActualWidth > 300 && canvas.ActualHeight > 80);
                Assert.Same(model.CurrentView, canvas.View);
                WritePreview(host, $"gtnh-tools-native-{(int)(scale * 100)}.png", scale);
            }
            Assert.True(layoutProblems.Count == 0, string.Join(Environment.NewLine, layoutProblems));
        }
        finally
        {
            model.IsDetailOpen = originalDetail;
            VisualTreeHelper.SetRootDpi(host, originalDpi);
            if (originalBackground == DependencyProperty.UnsetValue) host.ClearValue(Border.BackgroundProperty);
            else host.SetValue(Border.BackgroundProperty, originalBackground);
            Flush(host);
            scroll.ScrollToVerticalOffset(originalOffset);
            Flush(host);
        }
    }

    private static void WritePreview(FrameworkElement root, string fileName, double scale)
    {
        var directory = Environment.GetEnvironmentVariable("EML_TEXTBOX_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale),
            (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, fileName));
        encoder.Save(output);
    }

    private static void AssertReadOnlyError(FrameworkElement page, string message)
    {
        var error = Assert.Single(Descendants(page).OfType<TextBox>(), text => text.Text == message && IsVisibleWithin(text, page));
        Assert.True(error.IsReadOnly);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static IEnumerable<string> VisibleText(FrameworkElement page) => Descendants(page).OfType<TextBlock>()
        .Where(text => IsVisibleWithin(text, page)).Select(text => text.Text);

    private static bool IsVisibleWithin(DependencyObject child, DependencyObject ancestor)
    {
        for (var current = child; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(current, ancestor)) return true;
        }
        return false;
    }

    private static void AssertInside(FrameworkElement page, FrameworkElement element)
    {
        var bounds = element.TransformToAncestor(page).TransformBounds(new Rect(element.RenderSize));
        var description = $"{element.GetType().Name} {(element as ContentControl)?.Content}: {bounds}; 页面 {page.ActualWidth}×{page.ActualHeight}。";
        Assert.True(bounds.Width > 0 && bounds.Height > 0, description);
        Assert.True(bounds.Left >= -1 && bounds.Right <= page.ActualWidth + 1, description);
        Assert.True(bounds.Top >= -1 && bounds.Bottom <= page.ActualHeight + 1, description);
    }

    private static void Flush(Border host)
    {
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
        host.UpdateLayout();
    }

    private sealed class FlowChartPreview : IGtnhFlowChartService
    {
        internal string Payload => "{\"schemaVersion\":1,\"views\":{\"zh\":{\"title\":\"本地测试数据\"}}}";
        internal int Reads { get; private set; }
        internal int Refreshes { get; private set; }
        internal bool FailNext { get; set; }

        public Task<GtnhFlowChartContent> LoadAsync(GtnhFlowChart chart, bool refresh = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            if (refresh) Refreshes++;
            if (FailNext)
            {
                FailNext = false;
                return Task.FromException<GtnhFlowChartContent>(new InvalidOperationException("测试读取失败；没有访问网络。"));
            }
            GtnhFlowView View(string language) => new(language, language == "zh" ? "本地图表" : "Local graph",
                [new("i1", language == "zh" ? "原料" : "Input", "start", 0, 0), new("r1", "LV", "recipe", 1, 1)],
                [new("e1", "i1", "r1", "prime", 0)],
                [new("route:prime", language == "zh" ? "主干路线" : "Main route", ["i1", "r1"], ["e1"])]);
            var content = new GtnhFlowChartContent(new GtnhFlowDocument([View("zh"), View("en")]), Payload,
                chart.Address.AbsoluteUri, 123, DateTimeOffset.Parse("2026-11-22T00:00:00Z"), false);
            return Task.FromResult(content);
        }
    }
}
