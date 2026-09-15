using Stats.Core.Alerts;

namespace Stats.Core.Tests;

public class AlertNotificationPolicyTests
{
    private static readonly DateTime T0 = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Cooldowns_AreTenAndSixtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), AlertNotificationPolicy.GlobalCooldown);
        Assert.Equal(TimeSpan.FromSeconds(60), AlertNotificationPolicy.PerMetricCooldown);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ShouldNotify_NotificationsOff_ReturnsFalse_ForEveryOtherCombination(bool skipWhenForeground, bool dashboardIsForeground)
    {
        Assert.False(AlertNotificationPolicy.ShouldNotify(false, skipWhenForeground, dashboardIsForeground));
    }

    [Fact]
    public void ShouldNotify_SkipWhenForeground_DashboardForeground_ReturnsFalse()
    {
        Assert.False(AlertNotificationPolicy.ShouldNotify(true, true, true));
    }

    [Fact]
    public void ShouldNotify_SkipWhenForeground_DashboardNotForeground_ReturnsTrue()
    {
        Assert.True(AlertNotificationPolicy.ShouldNotify(true, true, false));
    }

    [Fact]
    public void ShouldNotify_NotSkipping_DashboardForeground_ReturnsTrue()
    {
        Assert.True(AlertNotificationPolicy.ShouldNotify(true, false, true));
    }

    [Fact]
    public void TryAccept_FirstNotification_Accepted()
    {
        var policy = new AlertNotificationPolicy();
        Assert.True(policy.TryAccept("cpu.temp", T0));
    }

    [Fact]
    public void TryAccept_SecondMetricInsideGlobalCooldown_Refused_ThenAcceptedAtTenSeconds()
    {
        var policy = new AlertNotificationPolicy();
        Assert.True(policy.TryAccept("cpu.temp", T0));
        Assert.False(policy.TryAccept("gpu.temp", T0.AddSeconds(9)));
        Assert.True(policy.TryAccept("gpu.temp", T0.AddSeconds(10)));
    }

    [Fact]
    public void TryAccept_SameMetricInsidePerMetricCooldown_Refused_ThenAcceptedAtSixtySeconds()
    {
        var policy = new AlertNotificationPolicy();
        Assert.True(policy.TryAccept("cpu.temp", T0));
        Assert.False(policy.TryAccept("cpu.temp", T0.AddSeconds(30)));
        Assert.False(policy.TryAccept("cpu.temp", T0.AddSeconds(59)));
        Assert.True(policy.TryAccept("cpu.temp", T0.AddSeconds(60)));
    }

    [Fact]
    public void TryAccept_RefusedCallDoesNotExtendEitherCooldown()
    {
        var policy = new AlertNotificationPolicy();
        Assert.True(policy.TryAccept("cpu.temp", T0));

        // Refused by the global cool-down; must not stamp gpu.temp.
        Assert.False(policy.TryAccept("gpu.temp", T0.AddSeconds(5)));
        Assert.True(policy.TryAccept("gpu.temp", T0.AddSeconds(10)));

        // Refused by the per-metric cool-down (cpu.temp's stamp is still T0); must not move it forward.
        Assert.False(policy.TryAccept("cpu.temp", T0.AddSeconds(59)));
        Assert.True(policy.TryAccept("cpu.temp", T0.AddSeconds(60)));
    }

    [Fact]
    public void TryAccept_GlobalCooldownRestartsOnEachAcceptedNotification()
    {
        var policy = new AlertNotificationPolicy();
        Assert.True(policy.TryAccept("cpu.temp", T0));
        Assert.True(policy.TryAccept("gpu.temp", T0.AddSeconds(10)));
        Assert.False(policy.TryAccept("mem.used", T0.AddSeconds(15)));
        Assert.True(policy.TryAccept("mem.used", T0.AddSeconds(20)));
    }

    [Fact]
    public void Reset_ForgetsAllStamps()
    {
        var policy = new AlertNotificationPolicy();
        Assert.True(policy.TryAccept("cpu.temp", T0));

        policy.Reset();

        Assert.True(policy.TryAccept("cpu.temp", T0.AddSeconds(1)));
    }
}
