using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EML.Core.Minecraft.Gtnh.Tools;

namespace EML.App.Controls;

/// <summary>只绘制经过验证的流程图数据；画布不访问网络、文件或远端代码。</summary>
public sealed class GtnhFlowCanvas : Control
{
    public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(
        nameof(View), typeof(GtnhFlowView), typeof(GtnhFlowCanvas), new FrameworkPropertyMetadata(null, OnViewChanged));
    public static readonly DependencyProperty HighlightGroupProperty = DependencyProperty.Register(
        nameof(HighlightGroup), typeof(string), typeof(GtnhFlowCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGroupChanged));
    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(
        nameof(IconData), typeof(IReadOnlyDictionary<string, byte[]>), typeof(GtnhFlowCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnIconDataChanged));
    private static readonly DependencyPropertyKey ZoomPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Zoom), typeof(double), typeof(GtnhFlowCanvas), new PropertyMetadata(1d));
    public static readonly DependencyProperty ZoomProperty = ZoomPropertyKey.DependencyProperty;

    private static readonly DependencyProperty AccentProperty = PaletteProperty("Accent");
    private static readonly DependencyProperty AccentSurfaceProperty = PaletteProperty("AccentSurface");
    private static readonly DependencyProperty MutedProperty = PaletteProperty("Muted");
    private static readonly DependencyProperty RaisedProperty = PaletteProperty("Raised");
    private static readonly DependencyProperty SuccessProperty = PaletteProperty("Success");
    private static readonly DependencyProperty SuccessSurfaceProperty = PaletteProperty("SuccessSurface");
    private static readonly DependencyProperty SecondaryProperty = PaletteProperty("Secondary");
    private static readonly DependencyProperty InfoSurfaceProperty = PaletteProperty("InfoSurface");

    private const double NodeWidth = 156, NodeHeight = 88, PaddingSize = 36;
    private const double MinimumZoom = .15, MaximumZoom = 3;
    private readonly Dictionary<string, NodeLayout> _nodes = new(StringComparer.Ordinal);
    private readonly List<NodeLayout> _nodeOrder = [];
    private readonly ToolTip _nodeTip = new() { Placement = PlacementMode.Bottom, MaxWidth = 420, Padding = new Thickness(12), StaysOpen = true };
    private readonly DispatcherTimer _openTipDelay, _closeTipDelay, _iconTimer;
    private readonly Stopwatch _iconClock = new();
    private GtnhFlowIconRenderer _icons = new(null);
    private HashSet<string>? _groupNodes, _groupEdges;
    private Rect _worldBounds = Rect.Empty;
    private Vector _offset;
    private Point? _dragStart;
    private Vector _dragOffset;
    private string? _hoveredNode, _selectedNode;
    private bool _dragged, _fitOnResize = true;

    public GtnhFlowCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        SetResourceReference(FontFamilyProperty, "InterfaceFontFamily");
        SetResourceReference(AccentProperty, "AccentTextBrush");
        SetResourceReference(AccentSurfaceProperty, "AccentSoftBrush");
        SetResourceReference(MutedProperty, "MutedTextBrush");
        SetResourceReference(RaisedProperty, "RaisedSurfaceBrush");
        SetResourceReference(SuccessProperty, "SuccessBrush");
        SetResourceReference(SuccessSurfaceProperty, "SuccessSoftBrush");
        SetResourceReference(SecondaryProperty, "SecondaryAccentBrush");
        SetResourceReference(InfoSurfaceProperty, "InfoSoftBrush");
        AutomationProperties.SetName(this, "交互流程图");
        AutomationProperties.SetHelpText(this, "滚轮缩放，拖动平移；加减键缩放，0 恢复标准，方向键平移，F6 依次选择节点，回车查看节点详情。");
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        ToolTipService.SetShowDuration(this, 20000);
        ToolTipService.SetIsEnabled(this, false);
        ToolTip = _nodeTip;
        _nodeTip.SetResourceReference(BackgroundProperty, "RaisedSurfaceBrush");
        _nodeTip.SetResourceReference(ForegroundProperty, "TextBrush");
        _nodeTip.SetResourceReference(BorderBrushProperty, "BorderBrush");
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        _openTipDelay = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(280) };
        _openTipDelay.Tick += (_, _) => { _openTipDelay.Stop(); if (_hoveredNode is not null && IsLoaded && IsVisible) _nodeTip.IsOpen = true; };
        _closeTipDelay = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _closeTipDelay.Tick += (_, _) => { _closeTipDelay.Stop(); if (!_nodeTip.IsMouseOver && !IsMouseOver) CloseNodeTip(); };
        _iconTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _iconTimer.Tick += (_, _) => { UpdateNodeAnimation(); if (_iconTimer.IsEnabled) InvalidateVisual(); };
        _nodeTip.Opened += (_, _) => { if (_nodeTip.Content is GtnhFlowRecipeCard card) card.SetPlayback(true); };
        _nodeTip.Closed += (_, _) => { if (_nodeTip.Content is GtnhFlowRecipeCard card) card.SetPlayback(false); };
        _nodeTip.MouseEnter += (_, _) => _closeTipDelay.Stop();
        _nodeTip.MouseLeave += (_, _) => _closeTipDelay.Start();
        Loaded += (_, _) => { SystemParameters.StaticPropertyChanged += OnSystemChanged; UpdateNodeAnimation(); };
        Unloaded += (_, _) => { SystemParameters.StaticPropertyChanged -= OnSystemChanged; StopInteraction(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopInteraction(); else UpdateNodeAnimation(); };
    }

    public GtnhFlowView? View { get => (GtnhFlowView?)GetValue(ViewProperty); set => SetValue(ViewProperty, value); }
    public string? HighlightGroup { get => (string?)GetValue(HighlightGroupProperty); set => SetValue(HighlightGroupProperty, value); }
    public IReadOnlyDictionary<string, byte[]>? IconData { get => (IReadOnlyDictionary<string, byte[]>?)GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }
    public double Zoom => (double)GetValue(ZoomProperty);
    public event EventHandler? ZoomChanged;
    internal Rect GraphBounds => _worldBounds;
    internal Vector PanOffset => _offset;
    internal bool IsIconAnimationRunning => _iconTimer.IsEnabled;
    internal bool IsNodeTipOpen => _nodeTip.IsOpen;
    internal int DecodedIconCount => _icons.Count;

    private static void OnIconDataChanged(DependencyObject target, DependencyPropertyChangedEventArgs change)
    {
        var canvas = (GtnhFlowCanvas)target;
        canvas.CloseNodeTip();
        canvas._icons = new GtnhFlowIconRenderer(change.NewValue as IReadOnlyDictionary<string, byte[]>);
        canvas.UpdateNodeAnimation(); canvas.InvalidateVisual();
    }

    private void OnSystemChanged(object? sender, PropertyChangedEventArgs args) => UpdateNodeAnimation();
    private void UpdateNodeAnimation()
    {
        var icon = _nodes.GetValueOrDefault(_hoveredNode ?? "")?.Node.Icon;
        if (IsLoaded && IsVisible && _dragStart is null && icon is { Frames: > 1 } &&
            _icons.Frame(icon) is not null && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        { _iconClock.Start(); _iconTimer.Start(); }
        else { _iconTimer.Stop(); _iconClock.Stop(); }
    }

    private void CloseNodeTip()
    {
        _openTipDelay.Stop(); _closeTipDelay.Stop();
        if (_nodeTip.Content is GtnhFlowRecipeCard card) card.SetPlayback(false);
        _nodeTip.IsOpen = false;
        _nodeTip.Content = null;
    }

    private void StopInteraction()
    {
        EndDrag(); _hoveredNode = null; _iconTimer.Stop(); _iconClock.Stop(); CloseNodeTip();
    }

    public void ZoomIn() => ZoomAt(Zoom * 1.2, ViewportCenter);
    public void ZoomOut() => ZoomAt(Zoom / 1.2, ViewportCenter);

    /// <summary>标准大小始终为 100%，不因分组切换而重新定位。</summary>
    public void ResetView()
    {
        CloseNodeTip();
        _fitOnResize = false;
        SetZoom(1);
        CenterGraph();
    }

    /// <summary>完整显示图表，包括节点边缘；大图可继续放大和拖动阅读。</summary>
    public void FitView()
    {
        CloseNodeTip();
        _fitOnResize = true;
        if (_worldBounds.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0) return;
        SetZoom(Math.Min(1, Math.Min(ActualWidth / _worldBounds.Width, ActualHeight / _worldBounds.Height)));
        CenterGraph();
    }

    private Point ViewportCenter => new(ActualWidth / 2, ActualHeight / 2);
    private static DependencyProperty PaletteProperty(string name) => DependencyProperty.Register(name, typeof(Brush),
        typeof(GtnhFlowCanvas), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));
    private Brush Palette(DependencyProperty property) => (Brush)GetValue(property);

    private static void OnViewChanged(DependencyObject target, DependencyPropertyChangedEventArgs change)
    {
        var canvas = (GtnhFlowCanvas)target;
        canvas.StopInteraction();
        canvas._nodes.Clear(); canvas._nodeOrder.Clear();
        canvas._hoveredNode = null; canvas._selectedNode = null;
        canvas._worldBounds = Rect.Empty;
        if (change.NewValue is GtnhFlowView { Nodes.Count: > 0 } view)
            canvas.ArrangeNodes(view);
        canvas.UpdateGroup();
        canvas.SetZoom(1);
        canvas._offset = default;
        canvas.FitView();
        canvas.InvalidateVisual();
        AutomationProperties.SetName(canvas, canvas.View is { } current ? $"{current.Title}，{current.Nodes.Count} 个节点，{current.Edges.Count} 条连接" : "交互流程图");
    }

    private void ArrangeNodes(GtnhFlowView view)
    {
        // 保留原坐标的顺序和对齐；缩小无意义的大段空白，并为相邻节点留出文字空间。
        var xs = ArrangeAxis(view.Nodes.Select(node => node.X), 212, NodeWidth + 20);
        var ys = ArrangeAxis(view.Nodes.Select(node => node.Y), 128, NodeHeight + 20);
        var occupiedRows = new Dictionary<double, List<HorizontalSpan>>();
        foreach (var node in view.Nodes)
        {
            if (!xs.TryGetValue(node.X, out var x) || !ys.TryGetValue(node.Y, out var y) || _nodes.ContainsKey(node.Id)) continue;
            if (!occupiedRows.TryGetValue(y, out var occupied))
                occupiedRows.Add(y, occupied = []);
            x = PlaceInRow(occupied, x);
            var bounds = new Rect(x + PaddingSize, y + PaddingSize, NodeWidth, NodeHeight);
            var layout = new NodeLayout(node, bounds);
            _nodes.Add(node.Id, layout); _nodeOrder.Add(layout);
            if (_worldBounds.IsEmpty) _worldBounds = bounds;
            else _worldBounds.Union(bounds);
        }
        if (!_worldBounds.IsEmpty)
            _worldBounds = new Rect(0, 0, _worldBounds.Right + PaddingSize, _worldBounds.Bottom + PaddingSize);
    }

    private static double PlaceInRow(List<HorizontalSpan> occupied, double requested)
    {
        // 各行已由 ArrangeAxis 分开。合并已占用的横向区间，重复坐标无需反复扫描所有节点。
        var low = 0; var high = occupied.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (occupied[middle].End <= requested) low = middle + 1;
            else high = middle;
        }
        var index = low;
        var x = requested;
        var width = NodeWidth + 20;
        while (index < occupied.Count && occupied[index].Start < x + width)
        {
            x = occupied[index].End;
            index++;
        }
        var start = x; var end = x + width;
        if (index > 0 && occupied[index - 1].End >= start)
        {
            start = occupied[index - 1].Start;
            occupied.RemoveAt(--index);
        }
        if (index < occupied.Count && occupied[index].Start <= end)
        {
            end = occupied[index].End;
            occupied.RemoveAt(index);
        }
        occupied.Insert(index, new HorizontalSpan(start, end));
        return x;
    }

    private static Dictionary<double, double> ArrangeAxis(IEnumerable<double> coordinates, double scale, double minimumGap)
    {
        var values = coordinates.Where(double.IsFinite).Distinct().Order().ToArray();
        var positions = new Dictionary<double, double>();
        var position = 0d;
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0) position += Math.Clamp((values[index] - values[index - 1]) * scale, minimumGap, scale * 4);
            positions.Add(values[index], position);
        }
        return positions;
    }

    private static void OnGroupChanged(DependencyObject target, DependencyPropertyChangedEventArgs change) => ((GtnhFlowCanvas)target).UpdateGroup();
    private void UpdateGroup()
    {
        var group = View?.Groups.FirstOrDefault(item => item.Id == HighlightGroup);
        _groupNodes = group is null ? null : new HashSet<string>(group.Nodes, StringComparer.Ordinal);
        _groupEdges = group is null ? null : new HashSet<string>(group.Edges, StringComparer.Ordinal);
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_fitOnResize) FitView();
        else
        {
            _offset += new Vector((sizeInfo.NewSize.Width - sizeInfo.PreviousSize.Width) / 2,
                (sizeInfo.NewSize.Height - sizeInfo.PreviousSize.Height) / 2);
            ClampOffset(); InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(Background, null, new Rect(RenderSize));
        if (View is null || _nodes.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        context.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        context.PushTransform(new MatrixTransform(Zoom, 0, 0, Zoom, _offset.X, _offset.Y));
        var viewport = new Rect(-_offset.X / Zoom, -_offset.Y / Zoom, ActualWidth / Zoom, ActualHeight / Zoom);
        viewport.Inflate(24, 24);
        foreach (var edge in View.Edges)
        {
            if (!_nodes.TryGetValue(edge.Source, out var source) || !_nodes.TryGetValue(edge.Target, out var target)) continue;
            var bounds = Rect.Union(source.Bounds, target.Bounds); bounds.Inflate(200, 200);
            if (!bounds.IntersectsWith(viewport)) continue;
            var selected = _groupEdges is null || _groupEdges.Contains(edge.Id);
            context.PushOpacity(selected ? 1 : .22);
            DrawEdge(context, edge, source.Bounds, target.Bounds);
            context.Pop();
        }
        foreach (var node in _nodeOrder)
        {
            if (!node.Bounds.IntersectsWith(viewport)) continue;
            context.PushOpacity(_groupNodes is null || _groupNodes.Contains(node.Node.Id) ? 1 : .3);
            DrawNode(context, node);
            context.Pop();
        }
        context.Pop(); context.Pop();
        if (IsKeyboardFocusWithin)
            context.DrawRoundedRectangle(null, new Pen(Palette(AccentProperty), 1) { DashStyle = DashStyles.Dot },
                new Rect(2, 2, Math.Max(0, ActualWidth - 4), Math.Max(0, ActualHeight - 4)), 4, 4);
    }

    private void DrawNode(DrawingContext context, NodeLayout layout)
    {
        var node = layout.Node; var bounds = layout.Bounds;
        var selected = node.Id == _selectedNode || node.Id == _hoveredNode;
        var fill = node.Kind switch
        {
            "recipe" => Palette(AccentSurfaceProperty), "start" => Palette(InfoSurfaceProperty),
            "end" => Palette(SuccessSurfaceProperty), _ => Palette(RaisedProperty)
        };
        var line = selected ? Palette(AccentProperty) : node.Kind == "end" ? Palette(SuccessProperty) : BorderBrush;
        context.DrawRoundedRectangle(fill, new Pen(line, selected ? 2 : 1.2), bounds, node.Kind == "recipe" ? 5 : 12, node.Kind == "recipe" ? 5 : 12);
        var caption = Format(KindName(node.Kind), 11.5, Palette(MutedProperty), NodeWidth - 22, 18);
        context.DrawText(caption, new Point(bounds.Left + 11, bounds.Top + 8));
        var hasIcon = _icons.Draw(context, node.Icon, new Rect(bounds.Left + 11, bounds.Top + 35, 24, 24),
            node.Id == _hoveredNode ? _iconClock.Elapsed.TotalSeconds : 0);
        var indent = hasIcon ? 47 : 11;
        var label = Format(node.Label, 14, Foreground, NodeWidth - indent - 11, NodeHeight - 35);
        label.SetFontWeight(node.Kind == "recipe" ? FontWeights.SemiBold : FontWeights.Normal);
        context.DrawText(label, new Point(bounds.Left + indent, bounds.Top + 29));
    }

    private FormattedText Format(string text, double size, Brush brush, double width, double height) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
    { MaxTextWidth = width, MaxTextHeight = height, Trimming = TextTrimming.CharacterEllipsis };

    private void DrawEdge(DrawingContext context, GtnhFlowEdge edge, Rect source, Rect target)
    {
        var sourceCenter = Center(source); var targetCenter = Center(target);
        var start = Boundary(source, targetCenter); var end = Boundary(target, sourceCenter);
        var delta = end - start;
        var bend = double.IsFinite(edge.Curveness) ? Math.Clamp(edge.Curveness, -2, 2) : 0;
        Point first, second;
        if (source == target)
        {
            start = new Point(source.Right, source.Top + 20); end = new Point(source.Right, source.Bottom - 20);
            first = start + new Vector(55, -35); second = end + new Vector(55, 35);
        }
        else if (Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            first = start + new Vector(delta.X * .4, bend * 70);
            second = end - new Vector(delta.X * .4, -bend * 70);
        }
        else
        {
            first = start + new Vector(bend * 70, delta.Y * .4);
            second = end - new Vector(-bend * 70, delta.Y * .4);
        }
        var brush = edge.Kind switch { "prime" => Palette(AccentProperty), "selective" => Palette(SecondaryProperty), _ => Palette(MutedProperty) };
        var pen = new Pen(brush, edge.Kind == "prime" ? 2.2 : 1.5);
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open()) { drawing.BeginFigure(start, false, false); drawing.BezierTo(first, second, end, true, false); }
        geometry.Freeze(); context.DrawGeometry(null, pen, geometry);
        var direction = end - second;
        if (direction.LengthSquared < .001) direction = targetCenter - sourceCenter;
        if (direction.LengthSquared < .001) return;
        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var arrow = new StreamGeometry();
        using (var drawing = arrow.Open())
        {
            drawing.BeginFigure(end, true, true);
            drawing.LineTo(end - direction * 9 + normal * 4, true, false);
            drawing.LineTo(end - direction * 9 - normal * 4, true, false);
        }
        arrow.Freeze(); context.DrawGeometry(brush, null, arrow);
    }

    private static Point Center(Rect bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    private static Point Boundary(Rect bounds, Point destination)
    {
        var center = Center(bounds); var delta = destination - center;
        if (delta.LengthSquared < .001) return center;
        var scale = Math.Min(Math.Abs(delta.X) < .001 ? double.PositiveInfinity : bounds.Width / 2 / Math.Abs(delta.X),
            Math.Abs(delta.Y) < .001 ? double.PositiveInfinity : bounds.Height / 2 / Math.Abs(delta.Y));
        return center + delta * scale;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (View is null) return;
        ZoomAt(Zoom * Math.Pow(1.15, e.Delta / 120d), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (View is null) return;
        Focus();
        if (e.ClickCount == 2) { FitView(); e.Handled = true; return; }
        _dragStart = e.GetPosition(this); _dragOffset = _offset; _dragged = false;
        CloseNodeTip(); UpdateNodeAnimation();
        CaptureMouse(); e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        if (_dragStart is { } origin && IsMouseCaptured)
        {
            var distance = position - origin;
            if (distance.Length > 3) _dragged = true;
            if (_dragged)
            {
                _fitOnResize = false; _offset = _dragOffset + distance;
                ClampOffset(); Cursor = Cursors.SizeAll; InvalidateVisual();
            }
            return;
        }
        var node = HitNode(position);
        if (_hoveredNode == node?.Node.Id) return;
        _hoveredNode = node?.Node.Id;
        _iconClock.Restart();
        Cursor = node is null ? Cursors.Hand : Cursors.Arrow;
        CloseNodeTip();
        if (node is not null) { SetNodeTip(node); _openTipDelay.Start(); }
        UpdateNodeAnimation();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart is null) return;
        var node = _dragged ? null : HitNode(e.GetPosition(this));
        EndDrag();
        if (node is not null) SelectNode(node, center: false);
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (IsMouseCaptured) return;
        _hoveredNode = null; _openTipDelay.Stop(); _closeTipDelay.Start(); UpdateNodeAnimation(); InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    { base.OnLostMouseCapture(e); _dragStart = null; Cursor = Cursors.Hand; }

    private void EndDrag()
    {
        _dragStart = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        CloseNodeTip(); UpdateNodeAnimation(); Cursor = Cursors.Hand;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (View is null) return;
        switch (e.Key)
        {
            case Key.Add: case Key.OemPlus: ZoomIn(); break;
            case Key.Subtract: case Key.OemMinus: ZoomOut(); break;
            case Key.D0: case Key.NumPad0: ResetView(); break;
            case Key.Home: FitView(); break;
            case Key.Left: PanBy(new Vector(64, 0)); break;
            case Key.Right: PanBy(new Vector(-64, 0)); break;
            case Key.Up: PanBy(new Vector(0, 64)); break;
            case Key.Down: PanBy(new Vector(0, -64)); break;
            case Key.Escape: CloseNodeTip(); _selectedNode = null; InvalidateVisual(); break;
            case Key.F6:
                if (_nodeOrder.Count > 0)
                {
                    var current = _nodeOrder.FindIndex(node => node.Node.Id == _selectedNode);
                    var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                    if (current < 0 && step < 0) current = 0;
                    SelectNode(_nodeOrder[(current + step + _nodeOrder.Count) % _nodeOrder.Count], center: true);
                }
                break;
            case Key.Enter:
                if (_nodeOrder.Count > 0) SelectNode(_nodes.GetValueOrDefault(_selectedNode ?? "") ?? _nodeOrder[0], center: true);
                break;
            default: return;
        }
        e.Handled = true;
    }

    private void SelectNode(NodeLayout node, bool center)
    {
        _selectedNode = node.Node.Id;
        if (center)
        {
            _fitOnResize = false;
            _offset = (Vector)ViewportCenter - (Vector)Center(node.Bounds) * Zoom;
            ClampOffset();
        }
        CloseNodeTip(); SetNodeTip(node); _nodeTip.IsOpen = true;
        AutomationProperties.SetItemStatus(this, node.Node.Label + "，" + KindName(node.Node.Kind));
        UIElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        InvalidateVisual();
    }

    private void SetNodeTip(NodeLayout node)
    {
        // 手动管理提示的延迟与生命周期，防止仍在悬停时被默认 ToolTip 时限关闭。
        ToolTipService.SetIsEnabled(this, false);
        _nodeTip.Content = CreateNodeDetails(node.Node.Id);
        _nodeTip.PlacementTarget = this;
        _nodeTip.PlacementRectangle = GetNodeViewportBounds(node.Node.Id);
        _nodeTip.VerticalOffset = 6;
    }

    internal FrameworkElement CreateNodeDetails(string id)
    {
        if (!_nodes.TryGetValue(id, out var node)) return new TextBlock();
        if (node.Node.Kind == "recipe") return new GtnhFlowRecipeCard(node.Node, _icons);
        var neighbours = View?.Edges.Where(edge => edge.Source == node.Node.Id || edge.Target == node.Node.Id)
            .Select(edge => _nodes.GetValueOrDefault(edge.Source == node.Node.Id ? edge.Target : edge.Source)?.Node.Label)
            .Where(label => label is not null).Distinct().Take(12).ToArray() ?? [];
        var text = node.Node.Label + "\n" + KindName(node.Node.Kind);
        if (neighbours.Length > 0) text += "\n\n相连节点：\n" + string.Join("\n", neighbours);
        return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 370 };
    }

    private NodeLayout? HitNode(Point point)
    {
        var world = new Point((point.X - _offset.X) / Zoom, (point.Y - _offset.Y) / Zoom);
        return _nodeOrder.LastOrDefault(node => node.Bounds.Contains(world));
    }

    internal Rect GetNodeViewportBounds(string id)
    {
        if (!_nodes.TryGetValue(id, out var node)) return Rect.Empty;
        return new Rect(node.Bounds.Left * Zoom + _offset.X, node.Bounds.Top * Zoom + _offset.Y, node.Bounds.Width * Zoom, node.Bounds.Height * Zoom);
    }

    internal void PanBy(Vector movement)
    {
        CloseNodeTip(); _fitOnResize = false; _offset += movement; ClampOffset(); InvalidateVisual();
    }

    private void ZoomAt(double requested, Point anchor)
    {
        if (_worldBounds.IsEmpty) return;
        _fitOnResize = false;
        var world = new Point((anchor.X - _offset.X) / Zoom, (anchor.Y - _offset.Y) / Zoom);
        SetZoom(requested);
        _offset = (Vector)anchor - (Vector)world * Zoom;
        ClampOffset(); CloseNodeTip(); InvalidateVisual();
    }

    private void SetZoom(double requested)
    {
        var zoom = Math.Clamp(double.IsFinite(requested) ? requested : 1, MinimumZoom, MaximumZoom);
        if (Math.Abs(Zoom - zoom) < .000001) return;
        SetValue(ZoomPropertyKey, zoom); ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CenterGraph()
    {
        if (_worldBounds.IsEmpty) { _offset = default; InvalidateVisual(); return; }
        _offset = new Vector((ActualWidth - _worldBounds.Width * Zoom) / 2, (ActualHeight - _worldBounds.Height * Zoom) / 2);
        ClampOffset(); InvalidateVisual();
    }

    private void ClampOffset()
    {
        if (_worldBounds.IsEmpty) { _offset = default; return; }
        static double Clamp(double offset, double graph, double viewport) => graph <= viewport ? (viewport - graph) / 2 : Math.Clamp(offset, viewport - graph, 0);
        _offset = new Vector(Clamp(_offset.X, _worldBounds.Width * Zoom, ActualWidth), Clamp(_offset.Y, _worldBounds.Height * Zoom, ActualHeight));
    }

    private static string KindName(string kind) => kind switch
    { "recipe" => "加工 / 机器", "start" => "始发原料", "end" => "最终产物", _ => "中间产物" };

    protected override AutomationPeer OnCreateAutomationPeer() => new FlowCanvasAutomationPeer(this);

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    { base.OnGotKeyboardFocus(e); InvalidateVisual(); }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    { base.OnLostKeyboardFocus(e); if (!_nodeTip.IsMouseOver) CloseNodeTip(); InvalidateVisual(); }
    private sealed class FlowCanvasAutomationPeer(GtnhFlowCanvas owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetClassNameCore() => nameof(GtnhFlowCanvas);
    }

    private sealed record NodeLayout(GtnhFlowNode Node, Rect Bounds);
    private readonly record struct HorizontalSpan(double Start, double End);
}
