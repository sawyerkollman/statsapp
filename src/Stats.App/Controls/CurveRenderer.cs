using System.Windows;
using System.Windows.Media;
using Stats.Core.Metrics;

namespace Stats.App.Controls;

/// <summary>Shared line/fill geometry helpers for <see cref="Sparkline"/> and <see cref="HistoryChart"/> — the
/// two controls were carrying verbatim-duplicate copies of these methods (review N3), differing only in the
/// fill's top-stop alpha when <see cref="GraphStyle.Effects"/> is off/on. Both controls call these from their own
/// OnRender after their own X(i)/Y(v) projection; nothing here is WPF-window- or control-specific.</summary>
internal static class CurveRenderer
{
    /// <summary>Maximal runs of consecutive finite (non-NaN) samples, oldest first — a NaN sample (a recorded
    /// gap; see MetricHistory.Add) ends one run and starts the search for the next, so callers can draw each run
    /// as its own polyline/fill figure and leave a visible break at every gap.</summary>
    public static IEnumerable<(int Start, int Length)> FiniteRuns(IReadOnlyList<float> values)
    {
        int start = -1;
        for (int i = 0; i < values.Count; i++)
        {
            if (float.IsNaN(values[i]))
            {
                if (start >= 0) { yield return (start, i - start); start = -1; }
            }
            else if (start < 0) start = i;
        }
        if (start >= 0) yield return (start, values.Count - start);
    }

    /// <summary>Decimated pixel points for one finite run (same stride logic and final-sample fix-up as before),
    /// shared by the fill and line figures so a smoothed fill hugs the same curve as the line.</summary>
    public static List<Point> RunPoints(int start, int last, int stride, Func<int, Point> at)
    {
        var pts = new List<Point>();
        for (int i = start; i <= last; i += stride) pts.Add(at(i));
        if ((last - start) % stride != 0) pts.Add(at(last));
        return pts;
    }

    /// <summary>Emits the curve for one run's decimated points into an open figure (the first point is assumed
    /// already placed by BeginFigure/a leading LineTo) — monotone-cubic Béziers when smoothing is on and the run
    /// has 3+ points, otherwise straight LineTo segments. A single-point run (n &lt;= 1) draws nothing more — see
    /// <see cref="Sparkline"/>/<see cref="HistoryChart"/>'s line pass for the single-point dot (review S4).</summary>
    public static void EmitCurve(StreamGeometryContext ctx, List<Point> pts, bool smooth, bool isStroked)
    {
        int n = pts.Count;
        if (n <= 1) return;
        if (!smooth || n == 2)
        {
            for (int i = 1; i < n; i++) ctx.LineTo(pts[i], isStroked, false);
            return;
        }

        var xs = new double[n];
        var ys = new double[n];
        for (int i = 0; i < n; i++) { xs[i] = pts[i].X; ys[i] = pts[i].Y; }
        var tangents = new double[n];
        CurveSmoothing.MonotoneTangents(xs, ys, tangents);
        for (int i = 1; i < n; i++)
        {
            var (c1x, c1y, c2x, c2y) = CurveSmoothing.BezierControlPoints(xs[i - 1], ys[i - 1], tangents[i - 1], xs[i], ys[i], tangents[i]);
            ctx.BezierTo(new Point(c1x, c1y), new Point(c2x, c2y), pts[i], isStroked, true);
        }
    }

    /// <summary>Gradient fill brush from <paramref name="stroke"/>'s colour (or Orange for a non-solid brush),
    /// top-stop alpha <paramref name="topAlphaPlain"/>/<paramref name="topAlphaEffects"/> depending on
    /// <paramref name="effects"/>, fading to transparent at the bottom. Frozen.</summary>
    public static Brush FillBrushFor(Brush stroke, byte topAlphaPlain, byte topAlphaEffects, bool effects)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        byte top = effects ? topAlphaEffects : topAlphaPlain;
        var b = new LinearGradientBrush(
            Color.FromArgb(top, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);
        b.Freeze();
        return b;
    }

    /// <summary>Wide, low-alpha (0x38) pen of <paramref name="stroke"/>'s colour drawn under the main line for
    /// the glow effect. Frozen.</summary>
    public static Pen GlowPenFor(Brush stroke)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        var brush = new SolidColorBrush(Color.FromArgb(0x38, c.R, c.G, c.B));
        brush.Freeze();
        var pen = new Pen(brush, 4.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    /// <summary>Thin pen of <paramref name="stroke"/>'s colour at the given <paramref name="alpha"/> for the
    /// last-value pulse ring. Frozen.</summary>
    public static Pen PulsePenFor(Brush stroke, byte alpha)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        var pen = new Pen(brush, 1.5);
        pen.Freeze();
        return pen;
    }

    /// <summary>Fill brush for <see cref="HistogramBars"/>' bars, from <paramref name="stroke"/>'s colour (or
    /// Orange for a non-solid brush): plain → a flat 0xCC-alpha solid brush; <paramref name="effects"/> → a
    /// vertical gradient (0xE6 at the top fading to 0x99 at the bottom), same shape as <see cref="FillBrushFor"/>.
    /// Frozen.</summary>
    public static Brush BarBrushFor(Brush stroke, bool effects)
    {
        var c = stroke is SolidColorBrush sc ? sc.Color : Colors.Orange;
        if (!effects)
        {
            var flat = new SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B));
            flat.Freeze();
            return flat;
        }
        var gradient = new LinearGradientBrush(
            Color.FromArgb(0xE6, c.R, c.G, c.B), Color.FromArgb(0x99, c.R, c.G, c.B), 90);
        gradient.Freeze();
        return gradient;
    }
}
