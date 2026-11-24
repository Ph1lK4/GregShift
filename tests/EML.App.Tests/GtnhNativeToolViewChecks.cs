using System.IO;
using System.Windows;
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

internal static class GtnhNativeToolViewChecks
{
    internal static void Run(Border host)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "GregShift-NativeToolTests-" + Guid.NewGuid().ToString("N"));
        var width = host.Width; var height = host.Height;
        var dpi = VisualTreeHelper.GetDpi(host);
        var original = host.Resources;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        using var model = new GtnhToolsViewModel { ToolsDataFolder = temporary };
        var page = new GtnhToolsView { DataContext = model };
        var browser = (GtnhToolBrowser)page.FindName("Browser");
        var planner = (GtnhPlannerPane)page.FindName("Planner");
        try
        {
            host.Width = 1080; host.Height = 760; host.Child = page; Flush(host);
            Assert.True(model.IsToolDirectory); Assert.False(browser.IsBrowserCreated);
            var directory = (ItemsControl)page.FindName("ToolDirectory");
            Assert.Equal(7, directory.Items.Count);
            model.OpenToolCommand.Execute(model.Tools[3]); Flush(host);
            var ore = (GtnhOreRegexPane)page.FindName("OreRegex");
            Assert.True(ore.IsVisible == page.IsVisible);
            Assert.Equal(348, ore.ViewModel.Materials.Count); Assert.NotEmpty(ore.ViewModel.Results);
            ore.ViewModel.SearchText = "钻石"; Assert.Equal("Diamond", Assert.Single(ore.ViewModel.Materials).Id);
            ore.ViewModel.SearchText = "Nickel";
            Assert.Contains(ore.ViewModel.Materials, material => material.Id == "Nickel");
            Assert.DoesNotContain(ore.ViewModel.Materials, material => material.Id == "Diamond");
            ore.ViewModel.SearchText = ""; ore.ViewModel.CharacterLimit = 256; ore.ViewModel.CommonRouteId = "MMC";
            FindButton(ore, "保存方案").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var oreFile = Path.Combine(temporary, "ore-processing.settings.json"); Assert.True(File.Exists(oreFile));
            var restoredOre = new GtnhOreRegexPane(); restoredOre.LoadCatalog(GtnhNativeToolData.LoadOres()); restoredOre.ConfigureStorage(temporary);
            Assert.Equal(256, restoredOre.ViewModel.CharacterLimit); Assert.Equal("MMC", restoredOre.ViewModel.CommonRouteId);

            model.OpenToolCommand.Execute(model.Tools[4]); Flush(host);
            var research = (GtnhResearchHelperPane)page.FindName("ResearchHelper");
            Assert.Equal(69, research.ViewModel.Aspects.Count);
            var endpoint = Descendants(research).OfType<ComboBox>().First();
            endpoint.SetCurrentValue(Selector.SelectedItemProperty, research.ViewModel.Aspects.Single(aspect => aspect.Id == "aer"));
            Flush(host); Assert.Equal("aer", research.ViewModel.StartAspect?.Id);
            research.ViewModel.EndAspect = research.ViewModel.Aspects.Single(aspect => aspect.Id == "terra");
            research.ViewModel.IsWeighted = true; research.ViewModel.Aspects[0].WeightText = "0.25";
            FindButton(research, "保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var researchFile = Path.Combine(temporary, "research-helper.settings.json"); Assert.True(File.Exists(researchFile));
            var restoredResearch = new GtnhResearchHelperPane(); restoredResearch.ConfigureStorage(temporary);
            restoredResearch.LoadData(GtnhNativeToolData.LoadResearchJson());
            Assert.Equal("aer", restoredResearch.ViewModel.StartAspect?.Id); Assert.Equal("terra", restoredResearch.ViewModel.EndAspect?.Id);
            Assert.Equal("0.25", restoredResearch.ViewModel.Aspects[0].WeightText); Assert.True(restoredResearch.ViewModel.IsWeighted);
            research.ViewModel.Aspects[0].WeightText = "NaN";
            var saved = File.ReadAllText(researchFile);
            FindButton(research, "保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("保存失败", research.ViewModel.Status); Assert.Equal(saved, File.ReadAllText(researchFile));
            Assert.Contains(research.ViewModel.Aspects[0].Label, research.ViewModel.Status);
            Assert.True(research.ViewModel.HasInvalidWeights);
            research.ViewModel.SearchText = "不存在的要素";
            FindButton(research, "定位无效权重").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(host);
            Assert.Equal("", research.ViewModel.SearchText);
            Assert.Same(research.ViewModel.Aspects[0], ((ListBox)research.FindName("AspectList")).SelectedItem);
            research.ViewModel.Aspects[0].WeightText = "0.25";
            Assert.False(research.ViewModel.HasInvalidWeights);
            Wait(research.ViewModel.CalculateCommand.ExecuteAsync(null));
            Assert.True(research.ViewModel.HasResult);

            foreach (var dark in new[] { true, false })
            {
                var resources = new ResourceDictionary();
                foreach (var entry in AppearancePalette.Create(dark)) resources[entry.Key] = new SolidColorBrush(entry.Value);
                resources["WallpaperPanelBrush"] = resources["SurfaceBrush"];
                host.Resources = resources; host.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
                foreach (var scale in new[] { 1d, 2d })
                {
                    VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
                    foreach (var index in new[] { -1, 3, 4 })
                    {
                        if (index < 0) model.BackToToolsCommand.Execute(null); else model.OpenToolCommand.Execute(model.Tools[index]);
                        Flush(host);
                        Assert.False(browser.IsBrowserCreated); Assert.False(browser.IsActive); Assert.False(planner.Browser.IsActive);
                        Preview(host, $"gtnh-tools-{(index < 0 ? "directory" : index == 3 ? "ore" : "research")}-{(dark ? "dark" : "light")}-{scale * 100:0}.png", scale);
                    }
                }
            }
            host.Width = 720; host.Height = 520;
            VisualTreeHelper.SetRootDpi(host, new DpiScale(1, 1));
            foreach (var index in new[] { 3, 4 })
            {
                model.OpenToolCommand.Execute(model.Tools[index]); Flush(host);
                var pane = index == 3 ? (FrameworkElement)ore : research;
                var scroller = (ScrollViewer)pane.FindName("ToolScroll");
                Assert.True(scroller.ScrollableHeight > 0);
                var list = Descendants(pane).OfType<ListBox>().First();
                Assert.True(list.ActualHeight >= 100, $"Native tool {index} list height was {list.ActualHeight}.");
                Preview(host, $"gtnh-tools-{(index == 3 ? "ore" : "research")}-small.png", 1);
            }
            foreach (var index in new[] { 5, 6 })
            {
                model.OpenToolCommand.Execute(model.Tools[index]); Flush(host);
                Assert.Equal(model.Tools[index].Address!.AbsoluteUri, browser.RequestedAddress);
                Assert.Equal(Visibility.Collapsed, ((DockPanel)page.FindName("BrowserAddressBar")).Visibility);
                Assert.False(browser.IsBrowserCreated);
            }
        }
        finally
        {
            host.Child = null; browser.Dispose(); planner.Browser.Dispose();
            host.Width = width; host.Height = height; host.Resources = original; VisualTreeHelper.SetRootDpi(host, dpi);
            SynchronizationContext.SetSynchronizationContext(previousContext);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }
    private static Button FindButton(DependencyObject root, string text) => Assert.Single(Descendants(root).OfType<Button>(), button => Equals(button.Content, text));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Flush(Border host)
    {
        host.Measure(new Size(host.Width, host.Height)); host.Arrange(new Rect(0, 0, host.Width, host.Height)); host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { })); host.UpdateLayout();
    }
    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var timeout = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Send,
                (_, _) => frame.Continue = false, dispatcher);
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            Dispatcher.PushFrame(frame); timeout.Stop();
            Assert.True(task.IsCompleted, "研究连接链应在测试时限内完成。");
        }
        task.GetAwaiter().GetResult();
    }
    private static void Preview(FrameworkElement root, string name, double scale)
    {
        var directory = Environment.GetEnvironmentVariable("EML_TEXTBOX_PREVIEW_DIRECTORY"); if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
