using System.Reflection;
using System.Windows;
using System.Windows.Data;
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

    [Fact]
    public void TimestampCursor_SetCurrentValuePreservesTwoWayBindingAcrossCharts() => RunSta(() =>
    {
        var source = new CursorSource();
        var first = Chart();
        var second = Chart();
        BindingOperations.SetBinding(first, HistoryChart.CursorTimeUtcProperty, new Binding(nameof(CursorSource.Cursor)) { Source = source, Mode = BindingMode.TwoWay });
        BindingOperations.SetBinding(second, HistoryChart.CursorTimeUtcProperty, new Binding(nameof(CursorSource.Cursor)) { Source = source, Mode = BindingMode.TwoWay });
        var cursor = new DateTime(2026, 9, 15, 12, 0, 0, 500, DateTimeKind.Utc);

        first.SetCurrentValue(HistoryChart.CursorTimeUtcProperty, cursor);

        Assert.True(BindingOperations.IsDataBound(first, HistoryChart.CursorTimeUtcProperty));
        Assert.True(BindingOperations.IsDataBound(second, HistoryChart.CursorTimeUtcProperty));
        Assert.Equal(cursor, source.Cursor);
        Assert.Equal(cursor, second.CursorTimeUtc);
    });

    private static HistoryChart Chart()
    {
        var start = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        return new HistoryChart
        {
            Values = [1, 2],
            TimesUtc = [start, start.AddSeconds(1)],
            AxisStartUtc = start,
            AxisEndUtc = start.AddSeconds(1)
        };
    }

    [Fact]
    public void TimestampChart_RendersOneSample_AndCursorDoesNotRebuildGeometry() => RunSta(() =>
    {
        var chart = Chart();
        chart.Values = new[] { 42f };
        chart.TimesUtc = new[] { chart.AxisStartUtc!.Value };
        chart.Measure(new Size(300, 150));
        chart.Arrange(new Rect(0, 0, 300, 150));
        Render(chart);
        var type = typeof(HistoryChart);
        Assert.True((bool)type.GetField("_hasData", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!);
        var field = type.GetField("_lineGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var geometry = field.GetValue(chart);
        chart.SetCurrentValue(HistoryChart.CursorTimeUtcProperty, chart.AxisStartUtc.Value.AddMilliseconds(500));
        Render(chart);
        Assert.Same(geometry, field.GetValue(chart));
    });

    private sealed class CursorSource : System.ComponentModel.INotifyPropertyChanged
    {
        private DateTime? _cursor;
        public DateTime? Cursor { get => _cursor; set { _cursor = value; PropertyChanged?.Invoke(this, new(nameof(Cursor))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

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
