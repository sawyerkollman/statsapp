using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Media;
using Stats.App.Views;

namespace Stats.UiPreview.Tests;

public class DashboardDragCoordinateTests
{
    [Fact]
    public void FreeAndCoreDrags_ResolveTheScaledCanvasCoordinateSpace()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var canvas = new Canvas { LayoutTransform = new ScaleTransform(1.3, 1.3) };
                var tileContainer = new ContentControl();
                var coreContainer = new ContentControl();
                canvas.Children.Add(tileContainer);
                canvas.Children.Add(coreContainer);
                var method = typeof(DashboardWindow).GetMethod("ParentCanvas", BindingFlags.Static | BindingFlags.NonPublic)!;
                Assert.Same(canvas, method.Invoke(null, [tileContainer]));
                Assert.Same(canvas, method.Invoke(null, [coreContainer]));
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
