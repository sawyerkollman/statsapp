using Stats.Core.Alerts;

namespace Stats.Core.Tests;

public class AlertEventTests
{
    private static readonly DateTime T0 = new(2026, 9, 11, 12, 0, 0);

    [Fact]
    public void NotificationTitle_IsDisplayNameFollowedByCritical()
    {
        var evt = new AlertEvent(T0, "cpu.temp", "CPU Package", "°C", 96f, 92f, LowerIsWorse: false);
        Assert.Equal("CPU Package critical", evt.NotificationTitle);
    }

    [Fact]
    public void NotificationBody_HigherIsWorse_FormatsPeakHoldAndThreshold()
    {
        var evt = new AlertEvent(T0, "cpu.temp", "CPU Package", "°C", 96f, 92f, LowerIsWorse: false);
        Assert.Equal("96 °C for 10 s (crit ≥ 92)", evt.NotificationBody(10));
    }

    [Fact]
    public void NotificationBody_LowerIsWorse_UsesLessOrEqual()
    {
        var evt = new AlertEvent(T0, "fps.avg", "FPS", "fps", 20f, 30f, LowerIsWorse: true);
        Assert.Equal("20 fps for 10 s (crit ≤ 30)", evt.NotificationBody(10));
    }

    [Fact]
    public void NotificationBody_BytesPerSecond_ScalesLikeTiles()
    {
        var evt = new AlertEvent(T0, "net.down", "Download", "B/s", 1_500_000f, 1_000_000f, LowerIsWorse: false);
        Assert.Equal("1.5 MB/s for 10 s (crit ≥ 1000000)", evt.NotificationBody(10));
    }

    [Fact]
    public void NotificationBody_FractionalThreshold_KeepsOneDecimal()
    {
        var evt = new AlertEvent(T0, "cpu.temp", "CPU Package", "°C", 96f, 92.5f, LowerIsWorse: false);
        Assert.Equal("96 °C for 10 s (crit ≥ 92.5)", evt.NotificationBody(10));
    }

    [Fact]
    public void NotificationBody_UsesTheHoldSecondsArgument()
    {
        var evt = new AlertEvent(T0, "cpu.temp", "CPU Package", "°C", 96f, 92f, LowerIsWorse: false);
        Assert.Equal("96 °C for 45 s (crit ≥ 92)", evt.NotificationBody(45));
    }
}
