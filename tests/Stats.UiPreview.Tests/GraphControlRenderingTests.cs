using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Stats.App.Controls;

namespace Stats.UiPreview.Tests;

public class GraphControlRenderingTests
{
    [Fact]
    public void Charts_RebuildWhenAMutableBufferReturnsAfterSkippedRender() => RunSta(() =>
    {
        foreach (FrameworkElement chart in new FrameworkElement[] { new Sparkline(), new HistoryChart() })
        {
            chart.Measure(new Size(300, 150));
            chart.Arrange(new Rect(0, 0, 300, 150));
            float[] a = [1, 2, 1];
            float[] b = [5, 6, 5];
            SetValues(chart, a);
            Render(chart);
            SetValues(chart, b); // no render before the next poll buffer arrives
            a[0] = 10; a[1] = 12; a[2] = 10;
            SetValues(chart, a);
            Render(chart);
            Assert.Equal(12f, (float)chart.GetType().GetField("_cachedMax", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!);
        }
    });

    [Fact]
    public void Histogram_RebuildsWhenAMutableBufferReturnsWithSameSumAndMaximum() => RunSta(() =>
    {
        var chart = new HistogramBars();
        chart.Measure(new Size(300, 150));
        chart.Arrange(new Rect(0, 0, 300, 150));
        int[] a = [8, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        chart.Bins = a;
        Render(chart);
        var field = typeof(HistogramBars).GetField("_barsGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var first = field.GetValue(chart);
        chart.Bins = new[] { 5, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        a[0] = 2; a[1] = 8;
        chart.Bins = a;
        Render(chart);
        Assert.NotSame(first, field.GetValue(chart));
    });

    private static void SetValues(FrameworkElement chart, IReadOnlyList<float> values) =>
        chart.GetType().GetProperty("Values")!.SetValue(chart, values);

    private static void Render(FrameworkElement chart)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        chart.GetType().GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chart, [dc]);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
