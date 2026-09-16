using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using Stats.App.Helpers;
using Stats.UiPreview;

namespace Stats.UiPreview.Tests;

public class NeonThemeTests
{
    [Fact]
    public void Apply_ReplacesNeonResources_AndResetsThemForAnOldTheme() => RunSta(() =>
    {
        var app = PreviewApp.EnsureCreated();
        ThemeManager.Apply("Synthwave", null);

        var synthBrush = Assert.IsType<SolidColorBrush>(app.Resources["NeonSecondaryBrush"]);
        Assert.True(synthBrush.IsFrozen);
        Assert.Equal(Visibility.Visible, app.Resources["NeonOverviewVisibility"]);
        Assert.Equal(ColorFrom("#FF52E5FF"), synthBrush.Color);
        Assert.Equal(synthBrush.Color, Assert.IsType<Color>(app.Resources["NeonSecondaryColor"]));

        ThemeManager.Apply("Light", null);

        var lightBrush = Assert.IsType<SolidColorBrush>(app.Resources["NeonSecondaryBrush"]);
        Assert.True(lightBrush.IsFrozen);
        Assert.NotSame(synthBrush, lightBrush);
        Assert.Equal(Visibility.Collapsed, app.Resources["NeonOverviewVisibility"]);
        Assert.Equal(ColorFrom("#FFD0D0D6"), lightBrush.Color);
        Assert.Equal(lightBrush.Color, Assert.IsType<Color>(app.Resources["NeonSecondaryColor"]));
    });

    private static Color ColorFrom(string hex) => (Color)ColorConverter.ConvertFromString(hex);

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
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
