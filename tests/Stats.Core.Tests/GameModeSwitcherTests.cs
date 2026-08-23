using Stats.Core.Fans;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class GameModeSwitcherTests
{
    private sealed class FakeBackend : IFanControlBackend
    {
        public List<FanChannel> Chans = new() { new("/c/0", "Fan #1", "ITE", null, null, 0, 100) };
        public IReadOnlyList<FanChannel> Channels => Chans;
        public void SetPercent(string id, float p) { }
        public void SetAuto(string id) { }
    }

    private static readonly DateTime T0 = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
    private static SensorSnapshot Snap(float? fps) => new(new Dictionary<string, float?> { ["fps.avg"] = fps }, T0);

    private static (GameModeSwitcher Sw, FanController C, AppSettings S) Make(bool enabled = true)
    {
        var s = new AppSettings { GameModeEnabled = enabled, GameModeGamingProfile = "Gaming", GameModeDesktopProfile = "Silent" };
        s.FanProfiles.Add(new FanProfile { Name = "Gaming", Channels = { ["/c/0"] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 90 } } });
        s.FanProfiles.Add(new FanProfile { Name = "Silent", Channels = { ["/c/0"] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 20 } } });
        var c = new FanController(new FakeBackend(), s, () => { });
        return (new GameModeSwitcher(c, s), c, s);
    }

    [Fact]
    public void EntersGamingAfterFiveSeconds_NotBefore()
    {
        var (sw, c, s) = Make();
        for (int t = 0; t <= 4; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.False(sw.IsGaming); Assert.Null(s.ActiveFanProfile);
        sw.Tick(Snap(120), T0.AddSeconds(5));
        Assert.True(sw.IsGaming); Assert.Equal("Gaming", s.ActiveFanProfile);
        Assert.Equal(90f, s.FanChannels["/c/0"].ManualPercent);
    }

    [Fact]
    public void ExitsAfterTwentySeconds_FlappingIgnored()
    {
        var (sw, _, s) = Make();
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
        var (sw, _, _) = Make();
        for (int t = 0; t <= 10; t++) sw.Tick(Snap(9.9f), T0.AddSeconds(t));
        Assert.False(sw.IsGaming);
        for (int t = 11; t <= 20; t++) sw.Tick(Snap(float.NaN), T0.AddSeconds(t));
        Assert.False(sw.IsGaming);
    }

    [Fact]
    public void Disabled_NeverTransitions()
    {
        var (sw, _, s) = Make(enabled: false);
        for (int t = 0; t <= 30; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.False(sw.IsGaming); Assert.Null(s.ActiveFanProfile);
        Assert.Equal("Game mode: off", sw.StatusText);
    }

    [Fact]
    public void MissingProfile_TransitionsWithoutApplying()
    {
        var (sw, _, s) = Make(); s.GameModeGamingProfile = "Nope";
        for (int t = 0; t <= 5; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.True(sw.IsGaming); Assert.Null(s.ActiveFanProfile);
        Assert.Contains("profile not found", sw.StatusText);
    }

    [Fact]
    public void AppliesOncePerTransition()
    {
        var (sw, _, s) = Make();
        for (int t = 0; t <= 5; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        s.FanChannels["/c/0"].ManualPercent = 55; // user tweak after apply (ActiveFanProfile cleared by user edit path is via controller; here simulate stale)
        for (int t = 6; t <= 60; t++) sw.Tick(Snap(120), T0.AddSeconds(t));
        Assert.Equal(55f, s.FanChannels["/c/0"].ManualPercent); // not re-applied while still gaming
    }
}
