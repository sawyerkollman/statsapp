using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public sealed class BetaSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StatsTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ReceiveBetaUpdates_DefaultsOff_AndRoundTrips()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{\"CheckForUpdatesAutomatically\":true}");
        var service = new SettingsService(_directory);
        Assert.False(service.Load().ReceiveBetaUpdates);

        service.Save(new AppSettings { ReceiveBetaUpdates = true });
        Assert.True(new SettingsService(_directory).Load().ReceiveBetaUpdates);
    }

    [Fact]
    public void ReceiveBetaUpdates_WritesThrough_SavesAndRaisesUpdateChannel()
    {
        var settings = new AppSettings();
        var saves = 0;
        SettingsChange? change = null;
        var viewModel = new SettingsViewModel(settings, Array.Empty<MetricDefinition>(), () => saves++);
        viewModel.Changed += value => change = value;

        viewModel.ReceiveBetaUpdates = true;

        Assert.True(settings.ReceiveBetaUpdates);
        Assert.Equal(1, saves);
        Assert.Equal(SettingsChange.UpdateChannel, change);
    }
}
