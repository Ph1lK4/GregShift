using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using System.IO;
using EML.App.Controls;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.Tests;

/// <summary>由现有唯一 STA/Application 调用；本地画布不得创建浏览器或读写实例。</summary>
internal static class GtnhFlowCanvasChecks
{
    internal static void Run(Border host)
    {
        var canvas = new GtnhFlowCanvas();
        host.Child = canvas;
        var graph = new GtnhFlowView("zh", "画布回归", [
            new("i1", "负坐标的始发原料", "start", -1, -2),
            new("r1", "化学反应釜\nLV", "recipe", 0, 0),
            new("i2", "含有很长名称的最终产物，完整文字可由节点提示读取", "end", 1, 1),
            new("i3", "同坐标也可分别访问", "default", 1, 1)],
            [new("e1", "i1", "r1", "prime", 0), new("e2", "r1", "i2", "default", .3)],
            [new("route:prime", "主干路线", ["i1", "r1"], ["e1"])]);
        canvas.View = graph;
        Layout(host);
        Assert.False(canvas.GraphBounds.IsEmpty);
        Assert.InRange(canvas.Zoom, .15, 1);
        Assert.False(canvas.GetNodeViewportBounds("i2").IntersectsWith(canvas.GetNodeViewportBounds("i3")));
        AssertInside(canvas, canvas.GetNodeViewportBounds("i1"));
        AssertInside(canvas, canvas.GetNodeViewportBounds("i3"));
        Render(canvas);
        canvas.ResetView();
        Assert.Equal(1, canvas.Zoom);
        for (var index = 0; index < 25; index++) canvas.ZoomIn();
        Assert.Equal(3, canvas.Zoom);
        for (var index = 0; index < 50; index++) canvas.ZoomOut();
        Assert.Equal(.15, canvas.Zoom);
        canvas.ResetView(); canvas.PanBy(new Vector(-120, -100));
        var zoom = canvas.Zoom; var offset = canvas.PanOffset;
        canvas.HighlightGroup = "route:prime";
        Assert.Equal(zoom, canvas.Zoom);
        Assert.Equal(offset, canvas.PanOffset);
        Render(canvas);
        canvas.HighlightGroup = null;
        canvas.PanBy(new Vector(double.MaxValue / 2, double.MaxValue / 2));
        Assert.True(double.IsFinite(canvas.PanOffset.X));
        Assert.True(double.IsFinite(canvas.PanOffset.Y));
        canvas.FitView(); Render(canvas);
        canvas.View = null;
        Assert.True(canvas.GraphBounds.IsEmpty);
        Assert.True(canvas.GetNodeViewportBounds("i1").IsEmpty);
        Render(canvas);
        var crowdedNodes = Enumerable.Range(0, 2048)
            .Select(index => new GtnhFlowNode($"dense:{index}", $"重复坐标节点 {index}", "default", 0, 0)).ToArray();
        canvas.View = new GtnhFlowView("zh", "有界大图", crowdedNodes, [], []);
        Layout(host);
        for (var index = 1; index < crowdedNodes.Length; index++)
            Assert.False(canvas.GetNodeViewportBounds(crowdedNodes[index - 1].Id)
                .IntersectsWith(canvas.GetNodeViewportBounds(crowdedNodes[index].Id)));
        Assert.True(double.IsFinite(canvas.GraphBounds.Width));
        Render(canvas);
        VerifyMedia(host, canvas);
        canvas.View = null;
        host.Child = null;
    }

    private static void VerifyMedia(Border host, GtnhFlowCanvas canvas)
    {
        const string address = "https://huiji-public.huijistatic.com/gtnh/uploads/1/12/Icon_test.png";
        var icon = new GtnhFlowIcon(address, 2, .05);
        var pixels = Enumerable.Range(0, 32 * 16).SelectMany(index => index % 32 < 16 ?
            new byte[] { 0, 0, 255, 255 } : new byte[] { 255, 0, 0, 255 }).ToArray();
        var bitmap = BitmapSource.Create(32, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 128);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream(); encoder.Save(png);
        var data = new Dictionary<string, byte[]> { [address] = png.ToArray(), ["corrupt"] = [1, 2, 3] };
        var renderer = new GtnhFlowIconRenderer(data);
        Assert.Equal(1, renderer.Count);
        var first = Assert.IsAssignableFrom<BitmapSource>(renderer.Frame(icon));
        var next = Assert.IsAssignableFrom<BitmapSource>(renderer.Frame(icon, .051));
        Assert.Equal(16, first.PixelWidth);
        Assert.True(first.IsFrozen && next.IsFrozen);
        Assert.NotSame(first, next);
        Assert.Same(first, renderer.Frame(icon, .101));
        Assert.Null(renderer.Frame(icon with { Frames = 3 }));
        Assert.Null(renderer.Frame(icon with { Address = "missing" }));
        Assert.Null(renderer.Frame(icon with { FrameSeconds = double.NaN }));

        var recipe = new GtnhFlowRecipe("蒸馏室", 348, 168,
            [new(100, 40, [new("编程电路2", "NC", icon)]),
             new(100, 118, [new("石油", "50L", icon)]), new(208, 118, [new("含硫轻燃油", "50L", null)])],
            ["总计：720 EU", "使用：24 EU/t (LV)", "时间：1.5 秒"]);
        canvas.IconData = data;
        canvas.View = new("zh", "真实配方媒体", [new("i", "石油", "start", 0, 0, icon),
            new("r", "蒸馏室\nLV", "recipe", 1, 0, Recipes: [recipe]),
            new("empty", "无详情机器", "recipe", 2, 0)], [], []);
        Layout(host); Render(canvas);
        Assert.Equal(1, canvas.DecodedIconCount);
        Assert.IsType<TextBlock>(canvas.CreateNodeDetails("i"));
        var card = Assert.IsType<GtnhFlowRecipeCard>(canvas.CreateNodeDetails("r"));
        Assert.Equal(1, card.RecipeCount);
        Assert.False(card.IsPlaying);
        host.Child = card; Layout(host);
        Assert.InRange(card.ActualWidth, 1, 380);
        var text = VisualChildren(card).OfType<TextBlock>().Select(block => block.Text).ToArray();
        Assert.Contains(text, value => value.Contains("石油（50L）"));
        Assert.Contains("使用：24 EU/t (LV)", text);
        Assert.Contains(text, value => value.Contains("实时加工进度"));
        foreach (var scale in new[] { 1d, 2d })
        {
            VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
            Layout(host);
            Assert.Equal(scale, VisualTreeHelper.GetDpi(card).DpiScaleX);
            Assert.InRange(card.ActualWidth, 1, 380);
            var snapshot = new RenderTargetBitmap((int)(card.ActualWidth * scale), (int)(card.ActualHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            snapshot.Render(card);
        }
        VisualTreeHelper.SetRootDpi(host, new DpiScale(1, 1));
        card.SetPlayback(false);
        Assert.False(card.IsPlaying);
        var missing = Assert.IsType<GtnhFlowRecipeCard>(canvas.CreateNodeDetails("empty"));
        host.Child = missing; Layout(host);
        Assert.Equal(0, missing.RecipeCount);
        Assert.Contains(VisualChildren(missing).OfType<TextBlock>(), block => block.Text.Contains("原站未提供"));
        host.Child = canvas; Layout(host);
        canvas.IconData = new Dictionary<string, byte[]> { [address] = [0, 1] };
        Assert.Equal(0, canvas.DecodedIconCount); Render(canvas);
        canvas.Visibility = Visibility.Collapsed;
        Assert.False(canvas.IsIconAnimationRunning);
        Assert.False(canvas.IsNodeTipOpen);
        canvas.Visibility = Visibility.Visible;
        VerifyPlaybackLifecycle(canvas.CreateNodeDetails("r"));
    }

    private static void VerifyPlaybackLifecycle(FrameworkElement content)
    {
        var card = Assert.IsType<GtnhFlowRecipeCard>(content);
        var root = new Border { Child = card, Width = 404, Height = 520 };
        using var source = new HwndSource(new HwndSourceParameters("GTNH recipe lifecycle test")
        { Width = 404, Height = 520, PositionX = -32000, PositionY = -32000, WindowStyle = 0 });
        source.RootVisual = root;
        try
        {
            Layout(root); Flush();
            Assert.True(card.IsLoaded);
            card.SetPlayback(true); Flush();
            Assert.Equal(SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast, card.IsPlaying);
            card.Visibility = Visibility.Collapsed; Flush();
            Assert.False(card.IsPlaying);
            card.Visibility = Visibility.Visible; Flush();
            Assert.Equal(SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast, card.IsPlaying);
            root.Child = null; Flush();
            Assert.False(card.IsLoaded);
            Assert.False(card.IsPlaying);
        }
        finally { root.Child = null; source.RootVisual = null; Flush(); }

        static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in VisualChildren(child)) yield return nested;
        }
    }

    private static void Layout(Border host)
    {
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));
        host.UpdateLayout();
    }
    private static void Render(GtnhFlowCanvas canvas)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.ActualWidth), (int)Math.Ceiling(canvas.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
    }
    private static void AssertInside(FrameworkElement owner, Rect bounds)
    {
        Assert.True(bounds.Left >= -1 && bounds.Right <= owner.ActualWidth + 1);
        Assert.True(bounds.Top >= -1 && bounds.Bottom <= owner.ActualHeight + 1);
    }
}
