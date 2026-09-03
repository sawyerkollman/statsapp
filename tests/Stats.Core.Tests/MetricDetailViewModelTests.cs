using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class MetricDetailViewModelTests
{
    private static MetricDefinition CpuTemp => new("cpu.temp", "Tctl", MetricGroup.Cpu, "Ryzen", "°C", "F1");
    private static MetricDefinition Fps => new("game.fps", "FPS", MetricGroup.Game, "PresentMon", "fps", "F0");

    private static AppSettings SettingsWithDefaults() =>
        new() { ThresholdRules = ThresholdDefaults.Rules(), PollIntervalSeconds = 2.0 };

    [Fact]
    public void Refresh_PopulatesHeaderAndStats()
    {
        var h = new MetricHistory(10);
        h.Add(80f); h.Add(90f); h.Add(94f);
        var vm = new MetricDetailViewModel(CpuTemp, h, SettingsWithDefaults());

        Assert.Equal("Tctl", vm.Title);
        Assert.Equal("94.0 °C", vm.CurrentText);
        Assert.Equal(Severity.Crit, vm.Severity); // Cpu °C rule: warn 85, crit 92
        Assert.Equal("80.0 °C", vm.MinText);
        Assert.Equal("94.0 °C", vm.MaxText);
        Assert.Equal("88.0 °C", vm.AvgText);
    }

    [Fact]
    public void Values_IncludesGapsAsNaN()
    {
        var h = new MetricHistory(10);
        h.Add(1f); h.Add(null); h.Add(3f);
        var vm = new MetricDetailViewModel(CpuTemp, h, SettingsWithDefaults());

        Assert.Equal(3, vm.Values.Length);
        Assert.True(float.IsNaN(vm.Values[1]));
    }

    [Fact]
    public void SecondsPerSample_ComesFromSettings()
    {
        var h = new MetricHistory(10);
        var settings = SettingsWithDefaults();
        settings.PollIntervalSeconds = 5.0;
        var vm = new MetricDetailViewModel(CpuTemp, h, settings);

        Assert.Equal(5.0, vm.SecondsPerSample);
    }

    [Fact]
    public void TimeAxisLabels_FiveLabels_OldestToNow()
    {
        var h = new MetricHistory(10);
        for (int i = 0; i < 10; i++) h.Add(i);
        var settings = SettingsWithDefaults();
        settings.PollIntervalSeconds = 60; // 1 min/sample, 9 intervals across 10 samples = 9 min total
        var vm = new MetricDetailViewModel(CpuTemp, h, settings);

        Assert.Equal(5, vm.TimeAxisLabels.Count);
        Assert.Equal("-9m", vm.TimeAxisLabels[0]);
        Assert.Equal("now", vm.TimeAxisLabels[^1]);
    }

    [Fact]
    public void TimeAxisLabels_FewerThanTwoSamples_IsJustNow()
    {
        var h = new MetricHistory(10);
        h.Add(1f);
        var vm = new MetricDetailViewModel(CpuTemp, h, SettingsWithDefaults());

        Assert.Equal(new[] { "now" }, vm.TimeAxisLabels);
    }

    [Fact]
    public void YAxisLabels_MinMidMax_IgnoreGaps()
    {
        var h = new MetricHistory(10);
        h.Add(10f); h.Add(null); h.Add(30f); h.Add(20f);
        var vm = new MetricDetailViewModel(CpuTemp, h, SettingsWithDefaults());

        Assert.Equal(new[] { "30.0 °C", "20.0 °C", "10.0 °C" }, vm.YAxisLabels);
    }

    [Fact]
    public void YAxisLabels_AllGaps_ShowsPlaceholders()
    {
        var h = new MetricHistory(4);
        h.Add(null); h.Add(float.NaN);
        var vm = new MetricDetailViewModel(CpuTemp, h, SettingsWithDefaults());

        Assert.Equal(new[] { "—", "—", "—" }, vm.YAxisLabels);
    }

    [Fact]
    public void WarnCrit_FromGroupRule_WhenNoOverride()
    {
        var h = new MetricHistory(10);
        var vm = new MetricDetailViewModel(CpuTemp, h, SettingsWithDefaults());

        Assert.Equal(85f, vm.WarnValue);
        Assert.Equal(92f, vm.CritValue);
        Assert.False(vm.LowerIsWorse);
    }

    [Fact]
    public void WarnCrit_FromOverride_TakesPrecedenceOverGroupRule()
    {
        var h = new MetricHistory(10);
        var settings = SettingsWithDefaults();
        settings.ThresholdOverrides["cpu.temp"] = new ThresholdRule { Warn = 70, Crit = 80 };
        var vm = new MetricDetailViewModel(CpuTemp, h, settings);

        Assert.Equal(70f, vm.WarnValue);
        Assert.Equal(80f, vm.CritValue);
    }

    [Fact]
    public void WarnCrit_LowerIsWorse_RespectedForFpsRule()
    {
        var h = new MetricHistory(10);
        var vm = new MetricDetailViewModel(Fps, h, SettingsWithDefaults());

        Assert.True(vm.LowerIsWorse);
        Assert.Equal(60f, vm.WarnValue);
        Assert.Equal(30f, vm.CritValue);
    }

    [Fact]
    public void HoverText_ReportsValueAndTimeAgo()
    {
        var h = new MetricHistory(10);
        var settings = SettingsWithDefaults();
        settings.PollIntervalSeconds = 1.0;
        h.Add(80f); h.Add(90f); h.Add(94f);
        var vm = new MetricDetailViewModel(CpuTemp, h, settings);

        Assert.Equal("94.0 °C at now", vm.HoverText(2));
        Assert.Equal("90.0 °C at -1s", vm.HoverText(1));
        Assert.Equal("80.0 °C at -2s", vm.HoverText(0));
    }

    [Fact]
    public void HoverText_Gap_ReportsDash()
    {
        var h = new MetricHistory(10);
        h.Add(1f); h.Add(null);
        var vm = new MetricDetailViewModel(CpuTemp, h, SettingsWithDefaults());

        Assert.Equal("— at now", vm.HoverText(1));
    }

    [Fact]
    public void HoverText_OutOfRange_ReturnsEmpty()
    {
        var h = new MetricHistory(10);
        h.Add(1f);
        var vm = new MetricDetailViewModel(CpuTemp, h, SettingsWithDefaults());

        Assert.Equal("", vm.HoverText(-1));
        Assert.Equal("", vm.HoverText(5));
    }

    [Fact]
    public void SetTarget_RetargetsToNewMetric()
    {
        var h1 = new MetricHistory(10);
        h1.Add(80f);
        var h2 = new MetricHistory(10);
        h2.Add(144f);
        var vm = new MetricDetailViewModel(CpuTemp, h1, SettingsWithDefaults());
        Assert.Equal("Tctl", vm.Title);

        vm.SetTarget(Fps, h2);

        Assert.Equal("FPS", vm.Title);
        Assert.Equal("144 fps", vm.CurrentText);
    }
}
