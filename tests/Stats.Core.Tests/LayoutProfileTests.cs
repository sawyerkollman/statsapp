using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class LayoutProfileTests
{
    [Fact]
    public void Restore_PreservesSharedDisplayPreferences_AndReplacesArrangement()
    {
        var settings = new AppSettings
        {
            DashboardMetrics = { "cpu", "gpu" },
            OverlayMetrics = { "cpu" },
            DashboardLayoutMode = DashboardLayoutMode.Grid,
            CoreMatrixX = 12,
            CoreMatrixY = 24,
        };
        settings.TilePrefs["cpu"] = new() { Name = "CPU package", Max = 100, Kind = TileKind.Gauge, Size = TileSize.L, X = 16, Y = 32 };
        var profile = LayoutProfile.Capture(settings, "Gaming");
        settings.DashboardMetrics.Clear();
        settings.TilePrefs["cpu"] = new() { Name = "Renamed", Max = 105, Kind = TileKind.Bar, Size = TileSize.S, X = 1, Y = 2 };
        settings.TilePrefs["new"] = new() { Name = "New", Max = 20 };

        profile.Restore(settings);

        Assert.Equal(new[] { "cpu", "gpu" }, settings.DashboardMetrics);
        Assert.Equal(TileKind.Gauge, settings.TilePrefs["cpu"].Kind);
        Assert.Equal(TileSize.L, settings.TilePrefs["cpu"].Size);
        Assert.Equal("Renamed", settings.TilePrefs["cpu"].Name);
        Assert.Equal(105f, settings.TilePrefs["cpu"].Max);
        Assert.Null(settings.TilePrefs["new"].X);
        Assert.Equal("New", settings.TilePrefs["new"].Name);
    }

    [Fact]
    public void Capture_DeepCopiesArrangementWithoutDisplayPreferences()
    {
        var settings = new AppSettings();
        settings.TilePrefs["cpu"] = new() { Name = "CPU", Max = 100, X = 12, Y = 24 };

        var profile = LayoutProfile.Capture(settings, "Desk");
        settings.TilePrefs["cpu"].X = 99;

        Assert.Null(profile.TilePrefs["cpu"].Name);
        Assert.Null(profile.TilePrefs["cpu"].Max);
        Assert.Equal(12, profile.TilePrefs["cpu"].X);
    }
}
