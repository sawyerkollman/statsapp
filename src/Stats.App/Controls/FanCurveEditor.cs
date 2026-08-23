using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Stats.App.Helpers;
using Stats.Core.Fans;
using Stats.Core.Settings;

namespace Stats.App.Controls;

/// <summary>Temperature→percent curve with draggable vertices. X: 20–100 °C, Y: 0–100 %.
/// Edits are committed to <see cref="Points"/> on mouse-up (drag), double-click (add), right-click (remove).</summary>
public sealed class FanCurveEditor : FrameworkElement
{
    private const double TempMin = 20, TempMax = 100;
    private const double PadL = 34, PadR = 10, PadT = 8, PadB = 22;
    private const double HitRadius = 10, DotRadius = 5.5;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(ObservableCollection<FanPoint>), typeof(FanCurveEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));
    public static readonly DependencyProperty MinPercentProperty = DependencyProperty.Register(
        nameof(MinPercent), typeof(double), typeof(FanCurveEditor), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaxPercentProperty = DependencyProperty.Register(
        nameof(MaxPercent), typeof(double), typeof(FanCurveEditor), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LiveTempProperty = DependencyProperty.Register(
        nameof(LiveTemp), typeof(double), typeof(FanCurveEditor), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LiveTargetProperty = DependencyProperty.Register(
        nameof(LiveTarget), typeof(double), typeof(FanCurveEditor), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LineBrushProperty = RegisterBrush(nameof(LineBrush), Color.FromRgb(0xE6, 0x8A, 0x2E));
    public static readonly DependencyProperty PointBrushProperty = RegisterBrush(nameof(PointBrush), Color.FromRgb(0xF0, 0xF0, 0xF0));
    public static readonly DependencyProperty AxisBrushProperty = RegisterBrush(nameof(AxisBrush), Color.FromRgb(0x3A, 0x3A, 0x40));
    public static readonly DependencyProperty TextBrushProperty = RegisterBrush(nameof(TextBrush), Color.FromRgb(0x9A, 0x9A, 0x9E));
    public static readonly DependencyProperty FloorBrushProperty = RegisterBrush(nameof(FloorBrush), Color.FromArgb(0x40, 0xE0, 0x5A, 0x4F));
    public static readonly DependencyProperty MarkerBrushProperty = RegisterBrush(nameof(MarkerBrush), Color.FromRgb(0x4F, 0xA3, 0xE0));

    // Named RegisterBrush (not Brush) so it does not shadow the System.Windows.Media.Brush type inside this class.
    private static DependencyProperty RegisterBrush(string name, Color c) => DependencyProperty.Register(
        name, typeof(Brush), typeof(FanCurveEditor), new FrameworkPropertyMetadata(new SolidColorBrush(c), FrameworkPropertyMetadataOptions.AffectsRender));

    public ObservableCollection<FanPoint>? Points { get => (ObservableCollection<FanPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public double MinPercent { get => (double)GetValue(MinPercentProperty); set => SetValue(MinPercentProperty, value); }
    public double MaxPercent { get => (double)GetValue(MaxPercentProperty); set => SetValue(MaxPercentProperty, value); }
    public double LiveTemp { get => (double)GetValue(LiveTempProperty); set => SetValue(LiveTempProperty, value); }
    public double LiveTarget { get => (double)GetValue(LiveTargetProperty); set => SetValue(LiveTargetProperty, value); }
    public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush PointBrush { get => (Brush)GetValue(PointBrushProperty); set => SetValue(PointBrushProperty, value); }
    public Brush AxisBrush { get => (Brush)GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush FloorBrush { get => (Brush)GetValue(FloorBrushProperty); set => SetValue(FloorBrushProperty, value); }
    public Brush MarkerBrush { get => (Brush)GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }

    private List<FanPoint> _work = new();   // working copy during a drag
    private int _dragIndex = -1;
    private readonly Typeface _typeface = new("Segoe UI");

    public FanCurveEditor()
    {
        Focusable = true;
        MinHeight = 120;
        MinWidth = 200;
        ToolTip = "Drag points · double-click to add · right-click to remove";
        // Unloaded isn't raised on app shutdown, and Loaded can fire more than once for the same instance (e.g.
        // re-parenting without an intervening unload) — unsubscribe first so the subscription stays idempotent.
        Loaded += (_, _) => { ThemeManager.Changed -= OnThemeChanged; ThemeManager.Changed += OnThemeChanged; SyncFloorBrush(); };
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    // LineBrush/PointBrush/AxisBrush/TextBrush are already routed through shared theme brushes (bound via
    // StaticResource in FansWindow.xaml), so WPF repaints them on its own. FloorBrush is palette-derived (a
    // translucent tint of CritBrush, marking the channel's floor) but is never bound from XAML, so it needs an
    // explicit resync here. MarkerBrush (the live-temperature indicator) is a fixed decorative blue unrelated to
    // any of the 11 palette colours — it stays hardcoded on purpose so the "you are here" marker keeps reading
    // distinctly from the accent-coloured curve line in every preset.
    private void OnThemeChanged() { SyncFloorBrush(); InvalidateVisual(); }

    private void SyncFloorBrush()
    {
        // Assign a new brush rather than mutating one in place: the DP's default value is frozen by WPF at
        // registration, and FloorBrush is never bound from XAML, so on first use this getter would otherwise
        // always be that frozen default — mutating it throws.
        var crit = ThemeManager.Get("CritBrush");
        FloorBrush = new SolidColorBrush(Color.FromArgb(0x40, crit.R, crit.G, crit.B));
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (FanCurveEditor)d;
        if (e.OldValue is ObservableCollection<FanPoint> old) old.CollectionChanged -= self.OnCollectionChanged;
        if (e.NewValue is ObservableCollection<FanPoint> now) now.CollectionChanged += self.OnCollectionChanged;
        self._dragIndex = -1;
        self.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_dragIndex < 0) InvalidateVisual();
    }

    private IReadOnlyList<FanPoint> Current => _dragIndex >= 0 ? _work : (IReadOnlyList<FanPoint>?)Points ?? Array.Empty<FanPoint>();

    // ---- geometry ----
    private Rect Plot => new(PadL, PadT, Math.Max(1, ActualWidth - PadL - PadR), Math.Max(1, ActualHeight - PadT - PadB));
    private double X(double temp) => Plot.Left + (Math.Clamp(temp, TempMin, TempMax) - TempMin) / (TempMax - TempMin) * Plot.Width;
    private double Y(double pct) => Plot.Bottom - Math.Clamp(pct, 0, 100) / 100.0 * Plot.Height;
    private double TempAt(double x) => TempMin + Math.Clamp((x - Plot.Left) / Plot.Width, 0, 1) * (TempMax - TempMin);
    private double PctAt(double y) => Math.Clamp((Plot.Bottom - y) / Plot.Height, 0, 1) * 100.0;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 140 : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var plot = Plot;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // hit-test surface
        var axisPen = new Pen(AxisBrush, 1);

        // grid + labels every 20 °C / 25 %
        for (double t = TempMin; t <= TempMax; t += 20)
        {
            double x = X(t);
            dc.DrawLine(axisPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            DrawText(dc, $"{t:F0}°", new Point(x - 8, plot.Bottom + 4));
        }
        for (double p = 0; p <= 100; p += 25)
        {
            double y = Y(p);
            dc.DrawLine(axisPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, $"{p:F0}%", new Point(2, y - 7));
        }

        // floor shading (channel min)
        if (MinPercent > 0)
            dc.DrawRectangle(FloorBrush, null, new Rect(plot.Left, Y(MinPercent), plot.Width, plot.Bottom - Y(MinPercent)));

        var pts = Current;
        if (pts.Count >= 2)
        {
            var sorted = pts.OrderBy(p => p.TempC).ToList();
            var linePen = new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round };
            // flat extensions beyond the ends
            dc.DrawLine(linePen, new Point(plot.Left, Y(sorted[0].Percent)), new Point(X(sorted[0].TempC), Y(sorted[0].Percent)));
            for (int i = 1; i < sorted.Count; i++)
                dc.DrawLine(linePen, new Point(X(sorted[i - 1].TempC), Y(sorted[i - 1].Percent)), new Point(X(sorted[i].TempC), Y(sorted[i].Percent)));
            dc.DrawLine(linePen, new Point(X(sorted[^1].TempC), Y(sorted[^1].Percent)), new Point(plot.Right, Y(sorted[^1].Percent)));
            foreach (var p in sorted)
                dc.DrawEllipse(PointBrush, new Pen(LineBrush, 1.5), new Point(X(p.TempC), Y(p.Percent)), DotRadius, DotRadius);
        }

        // live marker
        if (!double.IsNaN(LiveTemp))
        {
            double x = X(LiveTemp);
            var markerPen = new Pen(MarkerBrush, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(markerPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            if (!double.IsNaN(LiveTarget))
                dc.DrawEllipse(MarkerBrush, null, new Point(x, Y(LiveTarget)), 4, 4);
        }
    }

    private void DrawText(DrawingContext dc, string text, Point at)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 10, TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, at);
    }

    // ---- interaction ----
    private int HitTestPoint(Point pos)
    {
        var pts = Current;
        int best = -1; double bestD = HitRadius * HitRadius;
        for (int i = 0; i < pts.Count; i++)
        {
            double dx = X(pts[i].TempC) - pos.X, dy = Y(pts[i].Percent) - pos.Y;
            double d = dx * dx + dy * dy;
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (Points is null) return;
        var pos = e.GetPosition(this);
        if (e.ClickCount == 2)
        {
            if (HitTestPoint(pos) < 0 && Points.Count < FanCurve.MaxPoints && Plot.Contains(pos))
            {
                var np = new FanPoint((float)Math.Round(TempAt(pos.X)), (float)Math.Round(Math.Max(MinPercent, PctAt(pos.Y))));
                if (Points.All(p => Math.Abs(p.TempC - np.TempC) >= 1f)) Points.Add(np);
            }
            e.Handled = true;
            return;
        }
        int idx = HitTestPoint(pos);
        if (idx < 0) return;
        _work = Points.ToList();
        _dragIndex = idx;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragIndex < 0 || Points is null) return;
        var pos = e.GetPosition(this);
        double temp = Math.Round(TempAt(pos.X));
        double pct = Math.Round(Math.Max(MinPercent, PctAt(pos.Y)));
        // keep strict ordering against neighbours (sorted by temp in _work)
        var order = _work.Select((p, i) => (p, i)).OrderBy(x => x.p.TempC).ToList();
        int rank = order.FindIndex(x => x.i == _dragIndex);
        if (rank > 0) temp = Math.Max(temp, order[rank - 1].p.TempC + 1);
        if (rank < order.Count - 1) temp = Math.Min(temp, order[rank + 1].p.TempC - 1);
        _work[_dragIndex] = new FanPoint((float)temp, (float)pct);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragIndex < 0 || Points is null) return;
        int idx = _dragIndex;
        var committed = _work[idx];
        _dragIndex = -1;
        ReleaseMouseCapture();
        if (idx < Points.Count && Points[idx] != committed) Points[idx] = committed; // single Replace → one commit
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (Points is null || Points.Count <= FanCurve.MinPoints) return;
        int idx = HitTestPoint(e.GetPosition(this));
        if (idx >= 0) { Points.RemoveAt(idx); e.Handled = true; }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragIndex >= 0) { _dragIndex = -1; InvalidateVisual(); }
    }
}
