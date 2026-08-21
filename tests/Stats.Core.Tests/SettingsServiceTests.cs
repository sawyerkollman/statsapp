using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "StatsTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var svc = new SettingsService(_dir);
        var s = svc.Load();
        Assert.Equal(1.0, s.PollIntervalSeconds);
        Assert.Empty(s.DashboardMetrics);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings
        {
            PollIntervalSeconds = 2.5,
            DashboardMetrics = { "a", "b" },
            OverlayMetrics = { "c" },
            MetricLimits = { ["a"] = 150f },
            OverlayOpacity = 0.5,
            WindowWidth = 1200,
        };
        svc.Save(s);
        var loaded = new SettingsService(_dir).Load();
        Assert.Equal(2.5, loaded.PollIntervalSeconds);
        Assert.Equal(new[] { "a", "b" }, loaded.DashboardMetrics);
        Assert.Equal(new[] { "c" }, loaded.OverlayMetrics);
        Assert.Equal(150f, loaded.MetricLimits["a"]);
        Assert.Equal(0.5, loaded.OverlayOpacity);
        Assert.Equal(1200, loaded.WindowWidth);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not valid json !!!");
        var s = new SettingsService(_dir).Load();
        Assert.Equal(1.0, s.PollIntervalSeconds);
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(60.0, 5.0)]
    public void Load_ClampsPollIntervalToSpecRange(double stored, double expected)
    {
        var svc = new SettingsService(_dir);
        svc.Save(new AppSettings { PollIntervalSeconds = stored });
        Assert.Equal(expected, svc.Load().PollIntervalSeconds);
    }
}
