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
            SetValues(chart, b);
            a[0] = 10; a[1] = 12; a[2] = 10;
            SetValues(chart, a);
            Render(chart);
            Assert.Equal(12f, (float)chart.GetType().GetField("_cachedMax", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!);
        }
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
