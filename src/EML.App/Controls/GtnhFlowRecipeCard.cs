using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.Controls;

/// <summary>以原站槽位位置和原文参数绘制配方，不从图表连线推算数量或输入输出。</summary>
internal sealed class GtnhFlowRecipeCard : ScrollViewer
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly List<RecipeBoard> _boards = [];
    private readonly List<ProgressBar> _progress = [];
    private bool _playbackRequested;

    internal GtnhFlowRecipeCard(GtnhFlowNode node, GtnhFlowIconRenderer icons)
    {
        MaxWidth = 380;
        MaxHeight = Math.Min(560, Math.Max(180, SystemParameters.WorkArea.Height * .65));
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        CanContentScroll = false;
        var panel = new StackPanel { Margin = new Thickness(2) };
        Content = panel;
        var header = Text(node.Label.Replace('\n', ' '), 16, "TextBrush");
        header.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(header);
        panel.Children.Add(Text("原站配方 · 按原槽位排列", 11, "MutedTextBrush", new Thickness(0, 4, 0, 12)));
        var recipes = node.Recipes ?? [];
        if (recipes.Count == 0)
            panel.Children.Add(Text("原站未提供此节点的配方详情。", 13, "MutedTextBrush"));
        foreach (var recipe in recipes)
        {
            var group = new StackPanel();
            var title = Text(recipe.Title, 14, "TextBrush", new Thickness(0, 0, 0, 8));
            title.FontWeight = FontWeights.SemiBold;
            group.Children.Add(title);
            var board = new RecipeBoard(recipe, icons);
            _boards.Add(board);
            var viewbox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = board,
                HorizontalAlignment = HorizontalAlignment.Left };
            group.Children.Add(viewbox);
            var items = recipe.Slots.SelectMany(slot => slot.Items).Select(item => item.Name +
                (string.IsNullOrWhiteSpace(item.Annotation) ? "" : "（" + item.Annotation + "）")).Distinct();
            group.Children.Add(Text(string.Join(" · ", items), 11.5, "MutedTextBrush", new Thickness(0, 9, 0, 8)));
            var progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = .35, Height = 3, Margin = new Thickness(0, 2, 0, 10) };
            progress.SetResourceReference(Control.ForegroundProperty, "AccentTextBrush");
            progress.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
            _progress.Add(progress); group.Children.Add(progress);
            foreach (var information in recipe.Information)
                group.Children.Add(Text(information, 12.5, "TextBrush", new Thickness(0, 0, 0, 3)));
            var border = new Border { Child = group, Padding = new Thickness(12), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 10) };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            panel.Children.Add(border);
        }
        if (recipes.Count > 0)
            panel.Children.Add(Text("动画仅辅助阅读，不代表游戏中的实时加工进度。", 11, "MutedTextBrush"));
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdatePlayback();
    }

    internal bool IsPlaying => _timer.IsEnabled;
    internal int RecipeCount => _boards.Count;
    internal void SetPlayback(bool enabled) { _playbackRequested = enabled; UpdatePlayback(); }

    private static TextBlock Text(string text, double size, string resource, Thickness margin = default)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin };
        block.SetResourceReference(TextBlock.ForegroundProperty, resource);
        return block;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    { SystemParameters.StaticPropertyChanged += OnSystemChanged; UpdatePlayback(); }
    private void OnUnloaded(object sender, RoutedEventArgs args)
    { SystemParameters.StaticPropertyChanged -= OnSystemChanged; _timer.Stop(); _clock.Stop(); }
    private void OnSystemChanged(object? sender, PropertyChangedEventArgs args) => UpdatePlayback();
    private void UpdatePlayback()
    {
        if (_playbackRequested && IsLoaded && IsVisible && _boards.Count > 0 &&
            SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        { _clock.Start(); _timer.Start(); }
        else
        { _timer.Stop(); _clock.Stop(); }
    }
    private void OnTick(object? sender, EventArgs args)
    {
        if (!IsLoaded || !IsVisible || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        { UpdatePlayback(); return; }
        var seconds = _clock.Elapsed.TotalSeconds;
        foreach (var board in _boards) board.SetTime(seconds);
        foreach (var progress in _progress) progress.Value = seconds % 2 / 2;
    }

    private sealed class RecipeBoard : Control
    {
        private static readonly DependencyProperty SlotBrushProperty = DependencyProperty.Register("SlotBrush", typeof(Brush),
            typeof(RecipeBoard), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
        private static readonly DependencyProperty MutedBrushProperty = DependencyProperty.Register("MutedBrush", typeof(Brush),
            typeof(RecipeBoard), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        private readonly GtnhFlowRecipe _recipe;
        private readonly GtnhFlowIconRenderer _icons;
        private double _seconds;

        internal RecipeBoard(GtnhFlowRecipe recipe, GtnhFlowIconRenderer icons)
        {
            _recipe = recipe; _icons = icons;
            Width = recipe.Width; Height = recipe.Height;
            ClipToBounds = true;
            SetResourceReference(ForegroundProperty, "TextBrush");
            SetResourceReference(BorderBrushProperty, "BorderBrush");
            SetResourceReference(SlotBrushProperty, "RaisedSurfaceBrush");
            SetResourceReference(MutedBrushProperty, "MutedTextBrush");
            SetResourceReference(FontFamilyProperty, "InterfaceFontFamily");
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            System.Windows.Automation.AutomationProperties.SetName(this, recipe.Title + " 配方槽位");
        }

        internal void SetTime(double seconds) { _seconds = seconds; InvalidateVisual(); }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            foreach (var slot in _recipe.Slots)
            {
                if (slot.Items.Count == 0) continue;
                var item = slot.Items[(int)(_seconds / 1.2) % slot.Items.Count];
                var bounds = new Rect(slot.X - 2, slot.Y - 2, 36, 36);
                context.DrawRoundedRectangle((Brush)GetValue(SlotBrushProperty), new Pen(BorderBrush, 1), bounds, 3, 3);
                if (!_icons.Draw(context, item.Icon, new Rect(slot.X, slot.Y, 32, 32), _seconds))
                {
                    var shortName = item.Name.Length > 2 ? item.Name[..2] : item.Name;
                    context.DrawText(Format(shortName, 11, (Brush)GetValue(MutedBrushProperty), 30, 30),
                        new Point(slot.X + 1, slot.Y + 7));
                }
                if (!string.IsNullOrWhiteSpace(item.Annotation))
                {
                    var amount = Format(item.Annotation, 11, Foreground, 52, 18);
                    context.DrawRoundedRectangle((Brush)GetValue(SlotBrushProperty), null,
                        new Rect(slot.X - 2, slot.Y + 23, Math.Max(34, Math.Min(52, amount.Width + 5)), 16), 2, 2);
                    context.DrawText(amount, new Point(slot.X, slot.Y + 23));
                }
            }
        }

        private FormattedText Format(string text, double size, Brush brush, double width, double height) => new(text,
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = width, MaxTextHeight = height, Trimming = TextTrimming.CharacterEllipsis };
    }
}
