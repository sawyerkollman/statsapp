using Stats.Core.Fans;
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

    [Fact]
    public void Load_FileLockedByAnotherHandle_ReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{\"PollIntervalSeconds\": 3.0}");
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var s = new SettingsService(_dir).Load();
        Assert.Equal(1.0, s.PollIntervalSeconds); // defaults, not a crash
    }

    [Fact]
    public void Load_SeedsThresholdRulesWhenEmpty()
    {
        var s = new SettingsService(_dir).Load();
        Assert.Equal(5, s.ThresholdRules.Count);
        var cpu = s.ThresholdRules.First(r => r.Group == Stats.Core.Metrics.MetricGroup.Cpu && r.Unit == "°C");
        Assert.Equal(85f, cpu.Warn);
        Assert.Equal(92f, cpu.Crit);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsV11Fields_WithStringEnums()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings
        {
            HistoryWindowMinutes = 15,
            ShowCoreMatrix = false,
            OverlayOrientation = OverlayOrientation.Vertical,
            OverlayFontScale = 1.4,
            OverlayClickThrough = true,
            OverlayHotkey = "F9",
            CollapsedGroups = { "Storage" },
            PeaksWidth = 640,
        };
        s.PrefFor("cpu.ppt").Kind = TileKind.Gauge;
        s.PrefFor("cpu.ppt").Size = TileSize.L;
        s.PrefFor("cpu.ppt").Name = "PPT";
        s.PrefFor("cpu.ppt").Max = 150f;
        s.ThresholdOverrides["cpu.temp"] = new ThresholdRule { Warn = 70, Crit = 80 };
        svc.Save(s);

        var json = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.Contains("\"Kind\": \"Gauge\"", json);      // enums as strings, not ints
        Assert.Contains("\"OverlayOrientation\": \"Vertical\"", json);

        var l = new SettingsService(_dir).Load();
        Assert.Equal(15, l.HistoryWindowMinutes);
        Assert.False(l.ShowCoreMatrix);
        Assert.Equal(OverlayOrientation.Vertical, l.OverlayOrientation);
        Assert.Equal(1.4, l.OverlayFontScale);
        Assert.True(l.OverlayClickThrough);
        Assert.Equal("F9", l.OverlayHotkey);
        Assert.Equal(new[] { "Storage" }, l.CollapsedGroups);
        Assert.Equal(640, l.PeaksWidth);
        var pref = l.TilePrefs["cpu.ppt"];
        Assert.Equal(TileKind.Gauge, pref.Kind);
        Assert.Equal(TileSize.L, pref.Size);
        Assert.Equal("PPT", pref.Name);
        Assert.Equal(150f, pref.Max);
        Assert.Equal(80f, l.ThresholdOverrides["cpu.temp"].Crit);
    }

    [Fact]
    public void Load_V1File_GetsDefaultsForNewFields()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            "{\"PollIntervalSeconds\":1.0,\"DefaultsApplied\":true,\"DashboardMetrics\":[\"a\"],\"OverlayMetrics\":[],\"MetricLimits\":{},\"OverlayOpacity\":0.85}");
        var l = new SettingsService(_dir).Load();
        Assert.Equal(new[] { "a" }, l.DashboardMetrics);
        Assert.Empty(l.TilePrefs);
        Assert.Equal(2, l.HistoryWindowMinutes);
        Assert.True(l.ShowCoreMatrix);
        Assert.Equal("Ctrl+Shift+O", l.OverlayHotkey);
        Assert.Equal(5, l.ThresholdRules.Count);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(7, 5)]
    [InlineData(600, 60)]
    public void Load_SnapsHistoryWindowToAllowedValues(int stored, int expected)
    {
        var svc = new SettingsService(_dir);
        svc.Save(new AppSettings { HistoryWindowMinutes = stored });
        Assert.Equal(expected, svc.Load().HistoryWindowMinutes);
    }

    [Fact]
    public void PrefFor_CreatesOnce()
    {
        var s = new AppSettings();
        var a = s.PrefFor("x");
        a.Size = TileSize.S;
        Assert.Same(a, s.PrefFor("x"));
        Assert.Equal(TileSize.S, s.TilePrefs["x"].Size);
    }

    [Fact]
    public void Load_FanControl_DefaultsOffAndEmpty()
    {
        var s = new SettingsService(_dir).Load();
        Assert.False(s.FanControlEnabled);
        Assert.Empty(s.FanChannels);
    }

    [Fact]
    public void SaveThenLoad_FanChannels_RoundTrip()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings { FanControlEnabled = true };
        s.FanChannels["/lpc/it8696e/0/control/0"] = new FanChannelPref
        {
            Mode = FanMode.Curve, ManualPercent = 35, SourceMetricId = "cpu.x.temperature.tctl", Name = "Front intake",
            Points = new() { new(30, 20), new(60, 50), new(80, 100) },
        };
        svc.Save(s);
        var loaded = new SettingsService(_dir).Load();
        Assert.True(loaded.FanControlEnabled);
        var p = loaded.FanChannels["/lpc/it8696e/0/control/0"];
        Assert.Equal(FanMode.Curve, p.Mode);
        Assert.Equal(35f, p.ManualPercent);
        Assert.Equal("cpu.x.temperature.tctl", p.SourceMetricId);
        Assert.Equal("Front intake", p.Name);
        Assert.Equal(new[] { new FanPoint(30, 20), new FanPoint(60, 50), new FanPoint(80, 100) }, p.Points);
    }

    [Fact]
    public void Load_NullFanChannels_BecomesEmpty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{ "FanControlEnabled": true, "FanChannels": null, "ThresholdRules": null }""");
        var loaded = new SettingsService(_dir).Load();
        Assert.True(loaded.FanControlEnabled);
        Assert.NotNull(loaded.FanChannels);
        Assert.Empty(loaded.FanChannels);          // an explicit null must not blow up the fan loop
        Assert.Equal(5, loaded.ThresholdRules.Count); // …and null rules still get the defaults
    }

    [Fact]
    public void Load_MalformedCurve_FallsBackToDefault_AndClampsManual()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings();
        s.FanChannels["a"] = new FanChannelPref { ManualPercent = 140, Points = new() { new(50, 50) } }; // 1 point = invalid
        s.FanChannels["b"] = new FanChannelPref { ManualPercent = -5, Points = new() { new(50, 50), new(50.2f, 60) } }; // dup temps
        svc.Save(s);
        var loaded = new SettingsService(_dir).Load();
        Assert.Equal(100f, loaded.FanChannels["a"].ManualPercent);
        Assert.Equal(FanCurve.DefaultPoints, loaded.FanChannels["a"].Points);
        Assert.Equal(0f, loaded.FanChannels["b"].ManualPercent);
        Assert.Equal(FanCurve.DefaultPoints, loaded.FanChannels["b"].Points);
    }

    [Fact]
    public void Load_OlderFile_GainsFpsDefaultRule_KeepsExisting()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings { ThresholdRules = new() { new() { Group = Stats.Core.Metrics.MetricGroup.Cpu, Unit = "°C", Warn = 70, Crit = 80 } } };
        svc.Save(s);
        var loaded = new SettingsService(_dir).Load();
        Assert.Equal(70f, loaded.ThresholdRules.Single(r => r.Group == Stats.Core.Metrics.MetricGroup.Cpu && r.Unit == "°C").Warn);
        var fps = loaded.ThresholdRules.Single(r => r.Group == Stats.Core.Metrics.MetricGroup.Game && r.Unit == "fps");
        Assert.True(fps.LowerIsWorse);
    }

    [Fact]
    public void SaveThenLoad_LowerIsWorse_RoundTrips()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings();
        s.ThresholdOverrides["fps.avg"] = new ThresholdRule { Warn = 90, Crit = 45, LowerIsWorse = true };
        svc.Save(s);
        Assert.True(new SettingsService(_dir).Load().ThresholdOverrides["fps.avg"].LowerIsWorse);
    }

    [Fact]
    public void ReadMotherboardAndCoolers_DefaultsTrue_RoundTrips()
    {
        Assert.True(new SettingsService(_dir).Load().ReadMotherboardAndCoolers);
        var svc = new SettingsService(_dir); svc.Save(new AppSettings { ReadMotherboardAndCoolers = false });
        Assert.False(new SettingsService(_dir).Load().ReadMotherboardAndCoolers);
    }

    [Fact]
    public void Load_MigratesSingleSourceToList()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings();
        s.FanChannels["a"] = new FanChannelPref { SourceMetricId = "cpu.t", SourceMetricIds = new() };
        svc.Save(s);
        var p = new SettingsService(_dir).Load().FanChannels["a"];
        Assert.Equal(new[] { "cpu.t" }, p.SourceMetricIds);
        Assert.Equal("cpu.t", p.SourceMetricId);
    }

    [Fact]
    public void FanProfiles_RoundTrip_AndNullGuard()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings { ActiveFanProfile = "Silent" };
        s.FanProfiles.Add(new FanProfile
        {
            Name = "Silent",
            Channels = { ["/lpc/it8696e/0/control/0"] = new FanChannelPref { Mode = FanMode.Curve, ManualPercent = 140, Points = new() { new(50, 50) } } },
        });
        svc.Save(s);

        var loaded = new SettingsService(_dir).Load();
        Assert.Equal("Silent", loaded.ActiveFanProfile);
        var prof = loaded.FanProfiles.Single();
        Assert.Equal("Silent", prof.Name);
        var p = prof.Channels["/lpc/it8696e/0/control/0"];
        Assert.Equal(FanMode.Curve, p.Mode);
        Assert.Equal(100f, p.ManualPercent);                 // profile channels get the same sanitation as live ones
        Assert.Equal(FanCurve.DefaultPoints, p.Points);       // 1 point = invalid curve → default

        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{ "FanProfiles": null }""");
        var loaded2 = new SettingsService(_dir).Load();
        Assert.NotNull(loaded2.FanProfiles);
        Assert.Empty(loaded2.FanProfiles);
    }

    // System.Text.Json accepts a null element as happily as a null collection, and every one of these used to
    // reach the sanitation block — which runs before any window exists and would kill Stats on every launch.
    [Fact]
    public void Load_NullProfileElement_IsDropped()
    {
        Write("""{ "FanProfiles": [null] }""");
        Assert.Empty(new SettingsService(_dir).Load().FanProfiles);
    }

    [Fact]
    public void Load_NullProfileName_BecomesEmptyString()
    {
        Write("""{ "FanProfiles": [ { "Name": null } ] }""");
        Assert.Equal("", new SettingsService(_dir).Load().FanProfiles.Single().Name);
    }

    [Fact]
    public void Load_NullFanChannelEntry_IsDropped()
    {
        Write("""{ "FanChannels": { "a": null }, "FanProfiles": [ { "Name": "P", "Channels": { "b": null } } ] }""");
        var loaded = new SettingsService(_dir).Load();
        Assert.Empty(loaded.FanChannels);
        Assert.Empty(loaded.FanProfiles.Single().Channels);
        Assert.Equal(1.0, loaded.PollIntervalSeconds); // and the rest of the file is still usable
    }

    [Fact]
    public void Load_V13File_MigratesSourcesAndGainsV14Defaults_WithoutTouchingExistingRules()
    {
        Write("""
        {
          "PollIntervalSeconds": 1,
          "DefaultsApplied": true,
          "DashboardMetrics": [ "cpu.tctl" ],
          "ThresholdRules": [
            { "Group": "Cpu", "Unit": "°C", "Warn": 85, "Crit": 92 },
            { "Group": "Gpu", "Unit": "°C", "Warn": 80, "Crit": 88 },
            { "Group": "Cpu", "Unit": "%", "Warn": 90, "Crit": 98 },
            { "Group": "Gpu", "Unit": "%", "Warn": 90, "Crit": 98 }
          ],
          "FanControlEnabled": true,
          "FanChannels": {
            "/lpc/it8696e/0/control/0": { "Mode": "Curve", "ManualPercent": 40, "SourceMetricId": "cpu.t" }
          }
        }
        """);
        var loaded = new SettingsService(_dir).Load();

        var pref = loaded.FanChannels["/lpc/it8696e/0/control/0"];
        Assert.Equal(new[] { "cpu.t" }, pref.SourceMetricIds);   // §8 migration
        Assert.Equal("cpu.t", pref.SourceMetricId);
        Assert.Equal(FanMode.Curve, pref.Mode);
        Assert.True(loaded.ReadMotherboardAndCoolers);           // §4 default: on
        Assert.Empty(loaded.FanProfiles);                        // §5
        Assert.Null(loaded.ActiveFanProfile);
        Assert.False(loaded.GameModeEnabled);                    // §6
        Assert.Null(loaded.GameModeGamingProfile);
        Assert.Null(loaded.GameModeDesktopProfile);
        Assert.Equal(5, loaded.ThresholdRules.Count);            // §7: the fps rule was appended …
        Assert.All(loaded.ThresholdRules.Take(4), r => Assert.False(r.LowerIsWorse)); // … the four kept as they were
        Assert.Equal(85f, loaded.ThresholdRules[0].Warn);
        var fps = loaded.ThresholdRules.Single(r => r.Group == Stats.Core.Metrics.MetricGroup.Game && r.Unit == "fps");
        Assert.True(fps.LowerIsWorse);
        Assert.Equal(30f, loaded.ThresholdOverrides["fps.low1"].Warn); // and the 1 % low gets its own scale
        Assert.True(loaded.ThresholdOverrides["fps.low1"].LowerIsWorse);
    }

    [Fact]
    public void SaveThenLoad_GameModeFields_RoundTrip()
    {
        var svc = new SettingsService(_dir);
        svc.Save(new AppSettings { GameModeEnabled = true, GameModeGamingProfile = "Gaming", GameModeDesktopProfile = "Silent" });
        var loaded = new SettingsService(_dir).Load();
        Assert.True(loaded.GameModeEnabled);
        Assert.Equal("Gaming", loaded.GameModeGamingProfile);
        Assert.Equal("Silent", loaded.GameModeDesktopProfile);
    }

    [Fact]
    public void Load_ExistingFpsLow1Override_IsNotOverwritten()
    {
        var svc = new SettingsService(_dir);
        var s = new AppSettings();
        s.ThresholdOverrides["fps.low1"] = new ThresholdRule { Warn = 20, Crit = 10, LowerIsWorse = true };
        svc.Save(s);
        Assert.Equal(20f, new SettingsService(_dir).Load().ThresholdOverrides["fps.low1"].Warn);
    }

    [Fact]
    public void Load_MissingFile_DefaultsToDarkAmberTheme_NoAccentOverride()
    {
        var s = new SettingsService(_dir).Load();
        Assert.Equal("Dark Amber", s.ThemePreset);
        Assert.Null(s.ThemeAccent);
    }

    [Fact]
    public void Load_UnknownThemePreset_FallsBackToDefault()
    {
        Write("""{ "ThemePreset": "Neon Pink" }""");
        Assert.Equal("Dark Amber", new SettingsService(_dir).Load().ThemePreset);
    }

    [Fact]
    public void Load_NullThemePreset_FallsBackToDefault()
    {
        Write("""{ "ThemePreset": null }""");
        Assert.Equal("Dark Amber", new SettingsService(_dir).Load().ThemePreset);
    }

    [Theory]
    [InlineData("Dark Blue")]
    [InlineData("Dark Green")]
    [InlineData("Dark Purple")]
    [InlineData("Light")]
    public void SaveThenLoad_KnownThemePreset_RoundTrips(string preset)
    {
        var svc = new SettingsService(_dir);
        svc.Save(new AppSettings { ThemePreset = preset });
        Assert.Equal(preset, new SettingsService(_dir).Load().ThemePreset);
    }

    [Theory]
    [InlineData("not-a-hex")]
    [InlineData("#GGGGGG")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("123456")]
    public void Load_InvalidAccentHex_SanitizesToNull(string bad)
    {
        Write($$"""{ "ThemeAccent": "{{bad}}" }""");
        Assert.Null(new SettingsService(_dir).Load().ThemeAccent);
    }

    [Fact]
    public void SaveThenLoad_ValidAccentHex_RoundTrips()
    {
        var svc = new SettingsService(_dir);
        svc.Save(new AppSettings { ThemeAccent = "#4a9ee0" });
        Assert.Equal("#4A9EE0", new SettingsService(_dir).Load().ThemeAccent); // sanitized to uppercase
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), json);
    }
}
