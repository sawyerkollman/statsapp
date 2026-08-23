using Stats.Core.Fans;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class GameModeSwitcherTests
{
    private sealed class FakeBackend : IFanControlBackend
    {
        public List<FanChannel> Chans = new() { new("/c/0", "Fan #1", "ITE", null, null, 0, 100) };
        public List<(string Id, float? Pct)> Writes = new(); // SetAuto is recorded as a null Pct
        public IReadOnlyList<FanChannel> Channels => Chans;
        public void SetPercent(string id, float p) => Writes.Add((id, p));
        public void SetAuto(string id) => Writes.Add((id, null));
    }

    private static readonly DateTime T0 = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
    private static SensorSnapshot Snap(float? fps) => new(new Dictionary<string, float?> { ["fps.avg"] = fps }, T0);

    private static (GameModeSwitcher Sw, FanController C, AppSettings S, FakeBackend B) Make(bool enabled = true)
    {
        var s = new AppSettings { GameModeEnabled = enabled, GameModeGamingProfile = "Gaming", GameModeDesktopProfile = "Silent" };
        s.FanProfiles.Add(new FanProfile { Name = "Gaming", Channels = { ["/c/0"] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 90 } } });
        s.FanProfiles.Add(new FanProfile { Name = "Silent", Channels = { ["/c/0"] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 20 } } });
        var b = new FakeBackend();
        var c = new FanController(b, s, () => { });
        return (new GameModeSwitcher(c, s), c, s, b);
    }

    [Fact]
    public void EntersGamingAfterFiveSeconds_NotBefore()
    {
        var (sw, c, s, _) = Make();
        for (int t = 0; t <= 4; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.False(sw.IsGaming); Assert.Null(s.ActiveFanProfile);
        sw.Tick(Snap(120), T0.AddSeconds(5));
        Assert.True(sw.IsGaming); Assert.Equal("Gaming", s.ActiveFanProfile);
        Assert.Equal(90f, s.FanChannels["/c/0"].ManualPercent);
    }

    [Fact]
    public void GamingStatusText_ShowsLocalTime_NotUtc()
    {
        var (sw, _, s, _) = Make();
        var enterAtUtc = T0.AddSeconds(5);
        for (int t = 0; t <= 5; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.True(sw.IsGaming);
        var expectedLocal = enterAtUtc.ToLocalTime();
        Assert.Contains($"since {expectedLocal:HH:mm}", sw.StatusText);
    }

    [Fact]
    public void ExitsAfterTwentySeconds_FlappingIgnored()
    {
        var (sw, _, s, _) = Make();
        for (int t = 0; t <= 5; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.True(sw.IsGaming);
        sw.Tick(Snap(null), T0.AddSeconds(6));
        sw.Tick(Snap(null), T0.AddSeconds(20));   // 14 s inactive → still gaming
        Assert.True(sw.IsGaming);
        sw.Tick(Snap(200), T0.AddSeconds(21));     // active again resets the exit timer
        sw.Tick(Snap(null), T0.AddSeconds(22));
        sw.Tick(Snap(null), T0.AddSeconds(41));    // 19 s
        Assert.True(sw.IsGaming);
        sw.Tick(Snap(null), T0.AddSeconds(42));    // 20 s → exit
        Assert.False(sw.IsGaming); Assert.Equal("Silent", s.ActiveFanProfile);
        Assert.Equal(20f, s.FanChannels["/c/0"].ManualPercent);
    }

    [Fact]
    public void LowFps_OrNaN_CountsAsInactive()
    {
        var (sw, _, _, _) = Make();
        for (int t = 0; t <= 10; t++) sw.Tick(Snap(9.9f), T0.AddSeconds(t));
        Assert.False(sw.IsGaming);
        for (int t = 11; t <= 20; t++) sw.Tick(Snap(float.NaN), T0.AddSeconds(t));
        Assert.False(sw.IsGaming);
    }

    [Fact]
    public void Disabled_NeverTransitions()
    {
        var (sw, _, s, _) = Make(enabled: false);
        for (int t = 0; t <= 30; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.False(sw.IsGaming); Assert.Null(s.ActiveFanProfile);
        Assert.Equal("Game mode: off", sw.StatusText);
    }

    [Fact]
    public void MissingProfile_TransitionsWithoutApplying()
    {
        var (sw, _, s, _) = Make(); s.GameModeGamingProfile = "Nope";
        for (int t = 0; t <= 5; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.True(sw.IsGaming); Assert.Null(s.ActiveFanProfile);
        Assert.Contains("profile not found", sw.StatusText);
    }

    [Fact]
    public void ExitTransition_DesktopProfileLeavingTheChannelAuto_ActuallyReleasesIt()
    {
        var (sw, c, s, b) = Make();
        s.FanControlEnabled = true;
        s.FanProfiles[1] = new FanProfile { Name = "Silent" }; // Desktop profile omits /c/0 → Auto
        for (int t = 0; t <= 5; t++) { sw.Tick(Snap(120), T0.AddSeconds(t)); c.Tick(Snap(120), T0.AddSeconds(t)); }
        Assert.True(sw.IsGaming);
        Assert.Contains(b.Writes, w => w.Pct == 90f);              // the gaming profile really drove the fan
        for (int t = 6; t <= 26; t++) { sw.Tick(Snap(null), T0.AddSeconds(t)); c.Tick(Snap(null), T0.AddSeconds(t)); }
        Assert.False(sw.IsGaming);
        Assert.Contains(b.Writes, w => w.Pct is null);             // …and the exit handed it back to the device
        Assert.Equal(FanChannelStatus.Idle, c.Views().Single().Status);
    }

    [Fact]
    public void ReEnteringGaming_WhenThatProfileIsAlreadyActive_DoesNotReapplyIt()
    {
        var (sw, _, s, _) = Make();
        s.GameModeDesktopProfile = null;                            // nothing applied on exit: Gaming stays active
        for (int t = 0; t <= 5; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.Equal("Gaming", s.ActiveFanProfile);
        for (int t = 6; t <= 26; t++) sw.Tick(Snap(null), T0.AddSeconds(t));
        Assert.False(sw.IsGaming);
        s.FanChannels["/c/0"].ManualPercent = 55;                   // channel edit made on the desktop
        for (int t = 27; t <= 33; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.True(sw.IsGaming);
        Assert.Equal(55f, s.FanChannels["/c/0"].ManualPercent);     // re-apply skipped: the edit survives
    }

    [Fact]
    public void AppliesOncePerTransition()
    {
        var (sw, _, s, _) = Make();
        for (int t = 0; t <= 5; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        s.FanChannels["/c/0"].ManualPercent = 55; // user tweak after apply (ActiveFanProfile cleared by user edit path is via controller; here simulate stale)
        for (int t = 6; t <= 60; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.Equal(55f, s.FanChannels["/c/0"].ManualPercent); // not re-applied while still gaming
    }
}
