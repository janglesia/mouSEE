using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace app.UI;

public enum ClickAction
{
    None,
    DoubleClick,
    RightClick,
    Keyboard,
    Cancel,
    Scroll,
    Drag,
    LeftClick
}

public partial class ActionWheel : UserControl
{
    public record WheelItem(ClickAction Action, string Label, string Description, string Icon, bool IsActive = true);

    private const double OuterRadius = 150;
    private const double InnerRadius = 52;
    private const double Center = 160; // Center of 320x320 canvas

    private static readonly Brush SliceBg = Frozen("#D9101010");
    private static readonly Brush SliceHoverBg = Frozen("#F2262626");
    private static readonly Brush DisabledSliceBg = Frozen("#66101010");
    private static readonly Brush BorderBrushColor = Frozen("#40FFFFFF");
    private static readonly Brush AccentYellow = new SolidColorBrush(UiColors.Yellow);
    private static readonly Brush TextBrush = Frozen("#FFFFFF");
    private static readonly Brush DimTextBrush = Frozen("#66FFFFFF");

    // Exact clockwise order from image: 8 slots starting at Top (0 deg)
    private readonly List<WheelItem> _items =
    [
        new(ClickAction.DoubleClick, "Double Click", "Double-click at target point", "2x"),  // Top (0°)
        new(ClickAction.RightClick,  "Right Click",  "Open context menu",             "R"),   // Top-Right (45°)
        new(ClickAction.Keyboard,    "Keyboard",     "Open on-screen keyboard",       "KB"),  // Right (90°)
        new(ClickAction.None,        "",             "",                              "", false), // Bottom-Right (135° empty slot)
        new(ClickAction.Cancel,      "Cancel",       "Dismiss wheel",                 "✕"),   // Bottom (180°)
        new(ClickAction.Scroll,      "Scroll",       "Toggle scroll mode",            "↕"),   // Bottom-Left (225°)
        new(ClickAction.Drag,        "Drag",         "Hold left click to drag",       "✋"),  // Left (270°)
        new(ClickAction.LeftClick,   "Left Click",   "Standard single left click",    "⇱")    // Top-Left (315°)
    ];

    private readonly List<Path> _wedgePaths = [];
    private Path? _dwellArc;

    private int _hoveredIndex = -1;
    private long _hoverStartMs;

    public int DwellMs { get; set; } = 900;
    public Point TargetPosition { get; private set; }
    public bool IsOpen => Visibility == Visibility.Visible;
    public event Action<ClickAction, Point>? ActionSelected;

    public ActionWheel()
    {
        AccentYellow.Freeze();
        InitializeComponent();
        BuildWheel();
    }

    public void OpenAt(Point canvasPoint)
    {
        TargetPosition = canvasPoint;
        _hoveredIndex = -1;
        ResetHighlight();

        Canvas.SetLeft(this, canvasPoint.X - Width / 2);
        Canvas.SetTop(this, canvasPoint.Y - Height / 2);
        Visibility = Visibility.Visible;
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        _hoveredIndex = -1;
        ResetHighlight();
    }

    public void UpdateGaze(Point gazeCanvasPoint, long nowMs)
    {
        if (!IsOpen) return;

        double dx = gazeCanvasPoint.X - TargetPosition.X;
        double dy = gazeCanvasPoint.Y - TargetPosition.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // Center deadzone or outside wheel resets hover
        if (dist < InnerRadius || dist > OuterRadius + 40)
        {
            if (_hoveredIndex != -1)
            {
                _hoveredIndex = -1;
                ResetHighlight();
            }
            return;
        }

        // Angle: 0 at top, clockwise 0..360
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI + 90.0;
        if (angle < 0) angle += 360.0;

        double sweep = 360.0 / _items.Count; // Exactly 45 deg
        double shifted = (angle + sweep / 2.0) % 360.0;
        int index = (int)(shifted / sweep);

        // Disregard the empty slot
        if (!_items[index].IsActive)
        {
            if (_hoveredIndex != -1)
            {
                _hoveredIndex = -1;
                ResetHighlight();
            }
            return;
        }

        if (index != _hoveredIndex)
        {
            _hoveredIndex = index;
            _hoverStartMs = nowMs;
            HighlightSlice(index);
        }

        double progress = Math.Clamp((double)(nowMs - _hoverStartMs) / DwellMs, 0.0, 1.0);
        UpdateDwellArc(index, progress);

        if (progress >= 1.0)
        {
            var chosen = _items[index].Action;
            var target = TargetPosition;
            Close();
            if (chosen != ClickAction.Cancel && chosen != ClickAction.None)
                ActionSelected?.Invoke(chosen, target);
        }
    }

    private void BuildWheel()
    {
        WheelCanvas.Children.Clear();
        _wedgePaths.Clear();

        int count = _items.Count;
        double sweep = 360.0 / count;
        double gap = 2.0;

        for (int i = 0; i < count; i++)
        {
            var item = _items[i];
            double startAngle = i * sweep - sweep / 2.0 + gap / 2.0;
            double endAngle = (i + 1) * sweep - sweep / 2.0 - gap / 2.0;

            var path = new Path
            {
                Data = CreateDonutSlice(startAngle, endAngle, InnerRadius, OuterRadius),
                Fill = item.IsActive ? SliceBg : DisabledSliceBg,
                Stroke = item.IsActive ? BorderBrushColor : Brushes.Transparent,
                StrokeThickness = 1.5
            };
            _wedgePaths.Add(path);
            WheelCanvas.Children.Add(path);

            if (!item.IsActive) continue;

            double midAngle = (startAngle + endAngle) / 2.0;
            double labelRadius = (InnerRadius + OuterRadius) / 2.0;
            Point labelPos = PolarToCartesian(midAngle, labelRadius);

            var stack = new StackPanel { Orientation = Orientation.Vertical, IsHitTestVisible = false };
            stack.Children.Add(new TextBlock
            {
                Text = item.Icon,
                Foreground = AccentYellow,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = item.Label,
                Foreground = TextBrush,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });

            stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(stack, labelPos.X - stack.DesiredSize.Width / 2);
            Canvas.SetTop(stack, labelPos.Y - stack.DesiredSize.Height / 2);
            WheelCanvas.Children.Add(stack);
        }

        _dwellArc = new Path
        {
            Stroke = AccentYellow,
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        WheelCanvas.Children.Add(_dwellArc);
    }

    private void HighlightSlice(int activeIndex)
    {
        for (int i = 0; i < _wedgePaths.Count; i++)
        {
            if (!_items[i].IsActive) continue;
            _wedgePaths[i].Fill = i == activeIndex ? SliceHoverBg : SliceBg;
            _wedgePaths[i].Stroke = i == activeIndex ? AccentYellow : BorderBrushColor;
        }
        CenterTitle.Text = _items[activeIndex].Label;
        CenterSubtitle.Text = "Release to trigger";
        CenterSubtitle.Foreground = AccentYellow;
    }

    private void ResetHighlight()
    {
        for (int i = 0; i < _wedgePaths.Count; i++)
        {
            if (!_items[i].IsActive) continue;
            _wedgePaths[i].Fill = SliceBg;
            _wedgePaths[i].Stroke = BorderBrushColor;
        }
        if (_dwellArc is not null) _dwellArc.Data = null;
        CenterTitle.Text = "mouSEE";
        CenterSubtitle.Text = "Center";
        CenterSubtitle.Foreground = Frozen("#50C878"); // Emerald green matching concept graphic
    }

    private void UpdateDwellArc(int index, double progress)
    {
        if (_dwellArc is null || progress <= 0.01) return;

        double sweep = 360.0 / _items.Count;
        double startAngle = index * sweep - sweep / 2.0 + 2.0;
        double currentEnd = startAngle + (sweep - 4.0) * progress;

        _dwellArc.Data = CreateArc(startAngle, currentEnd, OuterRadius - 4);
    }

    private static Geometry CreateDonutSlice(double startAngle, double endAngle, double innerR, double outerR)
    {
        Point p1 = PolarToCartesian(startAngle, outerR);
        Point p2 = PolarToCartesian(endAngle, outerR);
        Point p3 = PolarToCartesian(endAngle, innerR);
        Point p4 = PolarToCartesian(startAngle, innerR);

        bool largeArc = (endAngle - startAngle) > 180.0;

        var fig = new PathFigure { StartPoint = p1, IsClosed = true };
        fig.Segments.Add(new ArcSegment(p2, new Size(outerR, outerR), 0, largeArc, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(p3, true));
        fig.Segments.Add(new ArcSegment(p4, new Size(innerR, innerR), 0, largeArc, SweepDirection.Counterclockwise, true));

        return new PathGeometry([fig]);
    }

    private static Geometry CreateArc(double startAngle, double endAngle, double radius)
    {
        Point p1 = PolarToCartesian(startAngle, radius);
        Point p2 = PolarToCartesian(endAngle, radius);
        bool largeArc = (endAngle - startAngle) > 180.0;

        var fig = new PathFigure { StartPoint = p1, IsClosed = false };
        fig.Segments.Add(new ArcSegment(p2, new Size(radius, radius), 0, largeArc, SweepDirection.Clockwise, true));
        return new PathGeometry([fig]);
    }

    private static Point PolarToCartesian(double angleDeg, double radius)
    {
        double rad = (angleDeg - 90.0) * Math.PI / 180.0;
        return new Point(Center + radius * Math.Cos(rad), Center + radius * Math.Sin(rad));
    }

    private static Brush Frozen(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public void TriggerHoveredSlice()
{
    if (!IsOpen || _hoveredIndex < 0 || _hoveredIndex >= _items.Count) return;

    var chosen = _items[_hoveredIndex].Action;
    var target = TargetPosition;
    Close();

    if (chosen != ClickAction.Cancel && chosen != ClickAction.None)
    {
        ActionSelected?.Invoke(chosen, target);
    }
}

}