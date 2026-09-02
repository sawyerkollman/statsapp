using Stats.Core.Alerts;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class AlertLogViewModelTests
{
    private static AlertEvent Evt(string id, string name, string unit, float peak, float threshold, bool lowerIsWorse = false, DateTime? at = null) =>
        new(at ?? new DateTime(2026, 9, 2, 14, 30, 5), id, name, unit, peak, threshold, lowerIsWorse);

    [Fact]
    public void Add_InsertsNewestFirst()
    {
        var log = new AlertLogViewModel();
        log.Add(Evt("cpu.temp", "CPU", "°C", 96f, 92f));
        log.Add(Evt("gpu.temp", "GPU", "°C", 90f, 88f));

        Assert.Equal(new[] { "gpu.temp", "cpu.temp" }, log.Rows.Select(r => r.MetricId));
    }

    [Fact]
    public void Add_CapsAt200Rows_DroppingOldest()
    {
        var log = new AlertLogViewModel();
        for (int i = 0; i < 205; i++) log.Add(Evt($"m{i}", "M", "°C", 10f, 5f));

        Assert.Equal(200, log.Rows.Count);
        Assert.Equal("m204", log.Rows[0].MetricId); // newest kept
        Assert.Equal("m5", log.Rows[^1].MetricId);  // the oldest 5 were dropped
    }

    [Fact]
    public void Row_TimeTextIsHHmmss()
    {
        var log = new AlertLogViewModel();
        log.Add(Evt("cpu.temp", "CPU", "°C", 96f, 92f, at: new DateTime(2026, 9, 2, 9, 5, 3)));

        Assert.Equal("09:05:03", log.Rows[0].TimeText);
    }

    [Fact]
    public void Row_PeakAndThresholdText_FormatWithUnitAndDirection()
    {
        var log = new AlertLogViewModel();
        log.Add(Evt("cpu.temp", "CPU Package", "°C", 96f, 92f));
        log.Add(Evt("fps.avg", "FPS", "fps", 20f, 30f, lowerIsWorse: true));

        var fps = log.Rows.Single(r => r.MetricId == "fps.avg");
        var cpu = log.Rows.Single(r => r.MetricId == "cpu.temp");
        Assert.Equal("96 °C", cpu.PeakText);
        Assert.Equal("≥ 92 °C", cpu.ThresholdText);
        Assert.Equal("20 fps", fps.PeakText);
        Assert.Equal("≤ 30 fps", fps.ThresholdText);
    }

    [Fact]
    public void Row_DurationText_IsOngoingUntilComplete()
    {
        var log = new AlertLogViewModel();
        log.Add(Evt("cpu.temp", "CPU", "°C", 96f, 92f));

        Assert.Equal("ongoing", log.Rows[0].DurationText);
        Assert.False(log.Rows[0].IsComplete);
    }

    [Theory]
    [InlineData(5, "5s")]
    [InlineData(72, "1m 12s")]
    [InlineData(3661, "1h 1m")]
    public void Complete_SetsDurationText(int seconds, string expected)
    {
        var log = new AlertLogViewModel();
        log.Add(Evt("cpu.temp", "CPU", "°C", 96f, 92f));

        log.Complete("cpu.temp", TimeSpan.FromSeconds(seconds));

        Assert.Equal(expected, log.Rows[0].DurationText);
        Assert.True(log.Rows[0].IsComplete);
    }

    [Fact]
    public void Complete_UnknownMetricId_IsNoOp()
    {
        var log = new AlertLogViewModel();
        log.Add(Evt("cpu.temp", "CPU", "°C", 96f, 92f));

        log.Complete("gpu.temp", TimeSpan.FromSeconds(10));

        Assert.Equal("ongoing", log.Rows[0].DurationText);
    }

    [Fact]
    public void Complete_OnlyCompletesTheOngoingRow_ForThatMetric()
    {
        var log = new AlertLogViewModel();
        log.Add(Evt("cpu.temp", "CPU", "°C", 90f, 92f)); // older, already-completed episode
        log.Rows[0].Complete(TimeSpan.FromSeconds(30));
        log.Add(Evt("cpu.temp", "CPU", "°C", 96f, 92f)); // newer, ongoing episode

        log.Complete("cpu.temp", TimeSpan.FromSeconds(45));

        Assert.Equal("45s", log.Rows[0].DurationText); // the newest, still-ongoing row
        Assert.Equal("30s", log.Rows[1].DurationText); // the older one is untouched
    }

    [Fact]
    public void ClearCommand_EmptiesRows()
    {
        var log = new AlertLogViewModel();
        log.Add(Evt("cpu.temp", "CPU", "°C", 96f, 92f));

        log.ClearCommand.Execute(null);

        Assert.Empty(log.Rows);
    }
}
