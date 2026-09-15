using Stats.Core.Alerts;
using Stats.Core.Fans;
using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.Updates;
using Stats.Core.ViewModels;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview;

/// <summary>Pure, non-UI composition of one scenario: builds the real Core view models backed entirely by fake
/// services, with no WPF types anywhere in this file — so it can be built and asserted against directly from
/// tests/Stats.UiPreview.Tests without a window. Views/CaptureHost.cs is the only place that turns a
/// <see cref="PreviewComposition"/> into an actual on-screen window.</summary>
public sealed class PreviewComposition
{
    public required string Scenario { get; init; }
    public required string TempRoot { get; init; }
    public required IReadOnlyList<MetricDefinition> Definitions { get; init; }
    public required MetricStore Store { get; init; }
    public required AppSettings Settings { get; init; }
    public required string SettingsPath { get; init; }
    public required Action SaveSettings { get; init; }
    public required CommandLog Commands { get; init; }

    public required DashboardViewModel Dashboard { get; init; }
    public required SettingsViewModel SettingsVm { get; init; }
    public required OverlayViewModel Overlay { get; init; }
    public required PeaksViewModel Peaks { get; init; }
    public required AlertLogViewModel AlertLog { get; init; }
    public required FansViewModel Fans { get; init; }
    public required FanController FanController { get; init; }
    public required FakeFanControlBackend FanBackend { get; init; }
    public required FakeSensorReader Reader { get; init; }
    public required NullFanArmedMarker FanMarker { get; init; }

    /// <summary>Builds one scenario's full composition under a fresh per-run temp root
    /// (%TEMP%\Stats.UiPreview\&lt;run-id&gt;\...) — never the user's live %AppData%\Stats. When
    /// <paramref name="settingsFixturePath"/> is given it is copied into the temp root and loaded verbatim (a
    /// compatibility check), superseding the scenario's own dashboard/overlay selection but not its fixture
    /// sensor history.</summary>
    public static PreviewComposition Build(string scenarioName, string tempRoot, IReadOnlyList<string> substates, string? settingsFixturePath = null)
    {
        Directory.CreateDirectory(tempRoot);
        var fixture = Scenarios.Build(scenarioName);
        var commands = new CommandLog();

        var settingsService = new SettingsService(tempRoot);
        var settingsPath = Path.Combine(tempRoot, "settings.json");
        AppSettings settings;
        if (!string.IsNullOrEmpty(settingsFixturePath))
        {
            if (!File.Exists(settingsFixturePath))
                throw new FileNotFoundException("Compatibility settings fixture not found.", settingsFixturePath);
            File.Copy(settingsFixturePath, settingsPath, overwrite: true);
            settings = settingsService.Load();
            commands.Record($"settings.load (compatibility fixture from {settingsFixturePath})");
        }
        else
        {
            settings = new AppSettings
            {
                DashboardMetrics = fixture.DashboardMetrics.ToList(),
                OverlayMetrics = fixture.OverlayMetrics.ToList(),
                DefaultsApplied = true,
                FanControlEnabled = fixture.FanControlEnabled,
                ActiveFanProfile = fixture.ActiveFanProfile,
            };
            foreach (var (id, pref) in fixture.TilePrefs) settings.TilePrefs[id] = pref;
            foreach (var (id, limit) in fixture.MetricLimits) settings.MetricLimits[id] = limit;
            foreach (var (id, rule) in fixture.ThresholdOverrides) settings.ThresholdOverrides[id] = rule;
            settings.CollapsedGroups = fixture.CollapsedGroups.ToList();
            foreach (var (id, pref) in fixture.FanChannelPrefs) settings.FanChannels[id] = pref;
            settings.FanProfiles = fixture.FanProfiles.ToList();
            if (fixture.AccentHex is not null) settings.ThemeAccent = fixture.AccentHex;
            ThresholdDefaults.EnsureDefaults(settings.ThresholdRules, settings.ThresholdOverrides);
        }

        void Save()
        {
            string json;
            lock (settings.SyncRoot) json = SettingsService.Serialize(settings);
            settingsService.Save(json);
            commands.Record("settings.save");
        }

        // Store capacity matches the fixture's own tick count (review S2) so every capture that doesn't opt into
        // "graphs-warmup" renders a *full* buffer — the steady state the real app spends ~all its time in, and
        // the state every other ui-polish dashboard/details baseline was implicitly assuming. Previously this was
        // hardcoded to 120 against a 60-tick fixture, so the fixed SampleAxis (right-anchored, constant spacing)
        // rendered every capture's line at half width, silently changing what those baselines showed.
        var store = new MetricStore(fixture.Definitions, capacity: fixture.Ticks.Count);
        // "graphs-warmup" (T3 of docs/superpowers/plans/2026-09-11-graph-effects.md): apply only the most recent
        // quarter of the fixture's ticks so the ring buffer sits at 25% of its capacity — the fixed SampleAxis
        // then right-anchors the line with visible empty space to its left. Must happen here, before the
        // ViewModels below are built from `store`, not in ApplySubstate (which runs after they already exist).
        int warmupTickCount = Math.Max(1, fixture.Ticks.Count / 4); // 25% of the store's capacity (== fixture.Ticks.Count)
        // "detail-warmup" is the details-view counterpart of "graphs-warmup" — same store-level trim, so the
        // detail window's HistoryChart (fed from the same MetricStore) shows the same 25%-full buffer.
        var ticksToApply = substates.Contains("graphs-warmup") || substates.Contains("detail-warmup")
            ? fixture.Ticks.Skip(Math.Max(0, fixture.Ticks.Count - warmupTickCount)).ToList()
            : fixture.Ticks;
        foreach (var tick in ticksToApply) store.Apply(tick);

        var dashboard = new DashboardViewModel(store, settings, Save)
        {
            IsDegraded = fixture.Degraded,
            UiScale = settings.DashboardUiScale,
        };
        if (fixture.Health is not null) dashboard.SetSensorHealth(fixture.Health);
        if (fixture.GameStatus is not null) dashboard.SetGroupStatus(MetricGroup.Game, fixture.GameStatus);

        var settingsVm = new SettingsViewModel(settings, fixture.Definitions, Save);
        settingsVm.SetVersionInfo("v0.0.0-preview", isDevBuild: true);
        settingsVm.OpenLogFolderRequested += () => commands.Record("settings.openLogFolder (simulated — no folder opened)");
        settingsVm.StartupToggleRequested += enable =>
        {
            commands.Record($"settings.startupToggle requested={enable}");
            settingsVm.ApplyStartupState(enable, busy: false, error: "");
        };
        settingsVm.CheckForUpdatesRequested += () =>
        {
            commands.Record("settings.checkForUpdates (simulated — no network call)");
            settingsVm.ApplyManualCheckResult("Up to date");
        };
        settingsVm.RestartRequested += () => commands.Record("settings.restart requested (simulated — no relaunch)");
        settingsVm.OverlayPositionResetRequested += () => commands.Record("settings.overlayPositionReset");
        dashboard.SettingsPanel = settingsVm;

        var overlay = new OverlayViewModel(store, settings);
        var alertLog = new AlertLogViewModel();
        var peaks = new PeaksViewModel(store, settings, alertLog);

        var fanBackend = new FakeFanControlBackend(commands);
        fanBackend.Chans.AddRange(fixture.FanChannels);
        var fanMarker = new NullFanArmedMarker();
        var fanController = new FanController(fanBackend, settings, Save, fanMarker);
        var fans = new FansViewModel(fanController, fixture.Definitions, settings,
            processNames: () => Array.Empty<string>(),
            clock: () => TimeSeries.FixedTimeUtc,
            saveSettings: Save,
            switcher: new GameModeSwitcher(fanController, settings),
            hardwareEnabledAtStartup: true);

        var reader = new FakeSensorReader(fixture.Definitions, fixture.Ticks.Count > 0 ? fixture.Ticks[^1] : new(new Dictionary<string, float?>(), TimeSeries.FixedTimeUtc), fixture.Degraded);

        var composition = new PreviewComposition
        {
            Scenario = scenarioName,
            TempRoot = tempRoot,
            Definitions = fixture.Definitions,
            Store = store,
            Settings = settings,
            SettingsPath = settingsPath,
            SaveSettings = Save,
            Commands = commands,
            Dashboard = dashboard,
            SettingsVm = settingsVm,
            Overlay = overlay,
            Peaks = peaks,
            AlertLog = alertLog,
            Fans = fans,
            FanController = fanController,
            FanBackend = fanBackend,
            Reader = reader,
            FanMarker = fanMarker,
        };

        foreach (var substate in substates) ApplySubstate(composition, fixture, substate);
        return composition;
    }

    /// <summary>View-model/settings-level substates that need no visual tree (popup/dropdown/context-menu
    /// substates are applied later, once a window exists — see Views/CaptureHost.cs).</summary>
    private static void ApplySubstate(PreviewComposition c, ScenarioFixture fixture, string substate)
    {
        switch (substate)
        {
            // ---- dashboard ----
            case "collapsed-all": c.Dashboard.CollapseAllCommand.Execute(null); break;
            case "degraded": c.Dashboard.IsDegraded = true; break;
            case "sensor-failure":
                c.Dashboard.SetSensorHealth(new SensorHealthState(false, 4,
                    TimeSeries.FixedTimeUtc.AddSeconds(-4).ToLocalTime(), "LibreHardwareMonitor: access denied", new[] { "LibreHardwareMonitor" }));
                break;
            case "update-offered":
                c.Dashboard.OfferUpdate(new UpdateInfo(new Version(9, 9, 9), "v9.9.9",
                    "https://example.invalid/Stats-Setup-9.9.9.exe", 12_345_678, "https://example.invalid/releases/v9.9.9"));
                break;
            case "update-progress":
                c.Dashboard.OfferUpdate(new UpdateInfo(new Version(9, 9, 9), "v9.9.9",
                    "https://example.invalid/Stats-Setup-9.9.9.exe", 12_345_678, "https://example.invalid/releases/v9.9.9"));
                c.Dashboard.SetUpdateProgress(0.42);
                break;
            case "update-error":
                c.Dashboard.OfferUpdate(new UpdateInfo(new Version(9, 9, 9), "v9.9.9",
                    "https://example.invalid/Stats-Setup-9.9.9.exe", 12_345_678, "https://example.invalid/releases/v9.9.9"));
                c.Dashboard.SetUpdateError("Update failed: network unreachable (simulated)");
                break;
            case "fps-hint":
                foreach (var id in c.Settings.DashboardMetrics.Where(FrameMetrics.IsFrameMetric).ToList()) c.Settings.DashboardMetrics.Remove(id);
                foreach (var id in c.Settings.OverlayMetrics.Where(FrameMetrics.IsFrameMetric).ToList()) c.Settings.OverlayMetrics.Remove(id);
                c.Settings.FpsHintDismissed = false;
                c.Dashboard.RebuildSections();
                break;
            case "tile-menu": break; // visual-tree substate — applied in CaptureHost

    // ---- graph effects (T3 of docs/superpowers/plans/2026-09-11-graph-effects.md) ----
            // Settings-level, applied here (before the window/controls exist) so GraphStyle.Apply — called by
            // CaptureHost right after Build() returns, still before window.Show() — has the right values in hand
            // before any control's Loaded/OnRender runs. "graphs-effects" is mostly documentary: both settings
            // already default to true (AppSettings.SmoothLines/GraphEffects), so this substate just makes the
            // capture's intent explicit next to "graphs-plain".
            case "graphs-plain": c.Settings.SmoothLines = false; c.Settings.GraphEffects = false; break;
            case "graphs-effects": c.Settings.SmoothLines = true; c.Settings.GraphEffects = true; break;
            case "graphs-warmup": break; // history already trimmed above, before the ViewModels were built

            // ---- details (graph effects) ----
            case "detail-plain": c.Settings.SmoothLines = false; c.Settings.GraphEffects = false; break;
            // Also a no-op against defaults, same as "graphs-effects" above (review N4) — kept as its own named
            // substate because it targets the details view rather than the dashboard.
    case "detail-smooth": c.Settings.SmoothLines = true; c.Settings.GraphEffects = true; break;
    case "detail-warmup": break; // history already trimmed above, before the ViewModels were built

    // ---- dashboard layout modes (docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md) ----
            // Setting LayoutMode (rather than poking AppSettings.DashboardLayoutMode directly) runs the VM's own
            // OnLayoutModeChanged hook, which persists, calls RebuildSections(), and — since RebuildSections runs
            // PlaceUnpositioned() whenever LayoutMode != Auto — seeds every still-null tile/core-matrix position
            // deterministically. That is the easy, harness-friendly path the design calls out explicitly.
    case "layout-free": c.Dashboard.LayoutMode = DashboardLayoutMode.Free; break;
    case "layout-grid": c.Dashboard.LayoutMode = DashboardLayoutMode.Grid; break;
    case "layout-free-placed": ApplyLayoutFreePlaced(c); break;

            // ---- picker ----
            case "no-results":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 0;
                c.Dashboard.PickerFilter = "zzz-no-matching-sensor";
                break;
            case "filtered":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 0;
                c.Dashboard.PickerFilter = "cpu";
                break;

            // ---- settings ----
            case "theme-dropdown":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
                break; // dropdown itself opened in CaptureHost
            case "invalid-threshold":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
                if (c.SettingsVm.ThresholdRuleItems.Count > 0) c.SettingsVm.ThresholdRuleItems[0].WarnText = "abc";
                break;
            case "invalid-limit":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
                if (c.SettingsVm.LimitItems.Count > 0) c.SettingsVm.LimitItems[0].ValueText = "abc";
                break;
            case "invalid-hotkey":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
                c.SettingsVm.OverlayHotkey = "Q";
                break;
            case "restart-required":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
                c.SettingsVm.HardwareStatus = "Restart Stats to apply";
                break;
            case "startup-checking":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
                c.SettingsVm.ApplyStartupState(false, busy: true, error: "");
                break;
            case "startup-error":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
                c.SettingsVm.ApplyStartupState(false, busy: false, error: "Could not check startup status: simulated failure (Access is denied)");
                break;
            case "update-checking":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
                c.SettingsVm.UpdateCheckBusy = true;
                break;

            // "update-error" is shared with the dashboard banner name above but means something different under
            // Settings (the About section's manual-check result) — Program.cs disambiguates by view.
            case "settings-update-error":
                c.Dashboard.IsPickerOpen = true; c.Dashboard.FlyoutTabIndex = 1;
                c.SettingsVm.ApplyManualCheckResult("Update check failed: could not reach github.com (simulated)", failed: true);
                break;

            // ---- fans ----
            case "off": c.Fans.Enabled = false; break;
            case "on": c.Fans.Enabled = true; break;
            case "manual": ApplyFanManual(c); break;
            case "curve": ApplyFanCurve(c); break;
            case "pump": ApplyFanPump(c); break;
            case "modified": ApplyFanModified(c); break;
            case "no-channels": c.FanBackend.Chans.Clear(); break;
            case "conflict": ApplyFanConflict(c); break;
            case "recovery":
                c.Fans.RecoveryNotice = "Stats did not shut down cleanly last time — all fans were returned to device control.";
                break;
            case "write-failed": ApplyFanWriteFailed(c); break;

            // ---- peaks ----
            case "empty": c.Settings.DashboardMetrics.Clear(); c.Peaks.RebuildRows(); break;
            case "populated": c.Peaks.RebuildRows(); break;
            case "long-names":
                foreach (var id in c.Settings.DashboardMetrics.Take(3))
                    c.Settings.PrefFor(id).Name = "A Very Long Sensor And Hardware Combination Name For Column Width";
                c.Peaks.RebuildRows();
                break;

            // ---- alerts ----
            case "ongoing":
                c.AlertLog.Add(new AlertEvent(TimeSeries.FixedTimeUtc.ToLocalTime(), "cpu.temp.tctl", "Tctl/Tdie", "°C", 96f, 92f, false));
                break;

            // ---- details ----
            case "gap": case "long-unit": case "thresholds": break; // handled by Program's metric selection for the details view

            // ---- overlay ----
            case "move-mode": c.Overlay.IsMoveMode = true; break;
            case "vertical": c.Settings.OverlayOrientation = OverlayOrientation.Vertical; c.Overlay.ApplyLayout(); break;
            case "light-parent": break; // handled by --theme Light at the Program/CaptureHost level
            case "opacity-min": c.Settings.OverlayOpacity = 0.3; break;
            case "opacity-max": c.Settings.OverlayOpacity = 1.0; break;
            case "long-value":
                if (c.Settings.OverlayMetrics.Count > 0)
                    c.Settings.PrefFor(c.Settings.OverlayMetrics[0]).Name = "Very Long Overlay Metric Label For Width";
                c.Overlay.Rebuild();
                break;

            // ---- threshold-dialog ----
            case "valid": case "invalid": case "lower-is-worse": break; // applied in CaptureHost (owns the dialog instance)

            default:
                throw new ArgumentException($"Unknown substate '{substate}'.", nameof(substate));
        }
    }

    /// <summary>Free layout, seeded, then a few tiles are explicitly moved — including one deliberately overlapping
    /// pair (two tiles given the exact same X/Y) to show overlap is allowed in Free/Snap — and the core-matrix
    /// block is dragged off its seeded (0,0) spot, all through the same <see cref="DashboardViewModel.SetTilePosition"/>/
    /// <see cref="DashboardViewModel.SetCoreMatrixPosition"/> paths a real drag or nudge would use.</summary>
    private static void ApplyLayoutFreePlaced(PreviewComposition c)
    {
        c.Dashboard.LayoutMode = DashboardLayoutMode.Free; // seeds every still-unplaced tile + the core-matrix block
        var tiles = c.Dashboard.Tiles;
        if (tiles.Count >= 2)
        {
            // Deliberately overlapping pair — offset by less than a tile so both stay visible, in the empty
            // top-middle area the moved core-matrix block leaves behind; proves Free/Snap allows overlap.
            c.Dashboard.SetTilePosition(tiles[0].Definition.Id, 600, 40);
            c.Dashboard.SetTilePosition(tiles[1].Definition.Id, 680, 110);
        }
        if (tiles.Count >= 3)
            c.Dashboard.SetTilePosition(tiles[2].Definition.Id, 980, 60);
        if (c.Dashboard.CoreMatrix is not null)
            c.Dashboard.SetCoreMatrixPosition(760, 540); // moved off its seeded (0,0) spot
    }

    private static void ApplyFanManual(PreviewComposition c)
    {
        if (c.FanBackend.Chans.Count == 0) return;
        var id = c.FanBackend.Chans[0].Id;
        c.Fans.Enabled = true;
        c.FanController.SetMode(id, FanMode.Manual);
        c.FanController.SetManualPercent(id, 65f);
        TickFan(c, ticks: 2);
        c.Fans.Refresh();
    }

    private static void ApplyFanCurve(PreviewComposition c)
    {
        if (c.FanBackend.Chans.Count == 0) return;
        var idx = c.FanBackend.Chans.Count > 1 ? 1 : 0;
        var id = c.FanBackend.Chans[idx].Id;
        c.Fans.Enabled = true;
        c.FanController.SetMode(id, FanMode.Curve);
        c.FanController.SetSource(id, "cpu.temp.tctl");
        TickFan(c, ticks: 2);
        c.Fans.Refresh();
    }

    private static void ApplyFanPump(PreviewComposition c)
    {
        var pump = c.FanBackend.Chans.FirstOrDefault(ch => ch.Name.Contains("pump", StringComparison.OrdinalIgnoreCase));
        if (pump is null) return;
        c.Fans.Enabled = true;
        c.FanController.SetMode(pump.Id, FanMode.Manual);
        c.FanController.SetManualPercent(pump.Id, 10f); // below the 50% pump floor — proves FanController.PumpFloorPercent
        TickFan(c, ticks: 2);
        c.Fans.Refresh();
    }

    private static void ApplyFanModified(PreviewComposition c)
    {
        if (c.FanBackend.Chans.Count == 0) return;
        c.Fans.Enabled = true;
        // Through the view model's own SaveProfile so FansViewModel.LastLoadedProfile (private state IsModified
        // depends on) is actually set — going through FanController alone would leave it null forever.
        c.Fans.SaveProfileCommand.Execute("Balanced");
        var id = c.FanBackend.Chans[0].Id;
        c.FanController.SetManualPercent(id, 77f); // edits after saving — ActiveFanProfile drops to null (Custom)
        TickFan(c, ticks: 1);
        c.Fans.Refresh();
    }

    private static void ApplyFanConflict(PreviewComposition c)
    {
        // FansViewModel.Refresh() derives ConflictText from its constructor's processNames Func on a 5-second
        // cadence; the composition's Func always returns empty (see Build above), so the substate sets the same
        // text FansViewModel.Refresh() would have computed from a matching process name, directly.
        var found = ConflictingFanSoftware.Match(new[] { "MSIAfterburner.exe" });
        c.Fans.ConflictText = $"Detected: {string.Join(", ", found)} — two controllers fighting the same fan is unsafe. Close them before enabling fan control.";
    }

    private static void ApplyFanWriteFailed(PreviewComposition c)
    {
        if (c.FanBackend.Chans.Count == 0) return;
        var id = c.FanBackend.Chans[0].Id;
        c.FanBackend.FailingChannels.Add(id);
        c.Fans.Enabled = true;
        c.FanController.SetMode(id, FanMode.Manual);
        c.FanController.SetManualPercent(id, 80f);
        TickFan(c, ticks: 4); // FanController.MaxWriteFailures = 3
        c.Fans.Refresh();
    }

    private static void TickFan(PreviewComposition c, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            var values = new Dictionary<string, float?>();
            foreach (var d in c.Definitions) values[d.Id] = c.Store.TryGet(d.Id, out var h) ? h.Current : null;
            c.FanController.Tick(new SensorSnapshot(values, TimeSeries.FixedTimeUtc.AddSeconds(i)), TimeSeries.FixedTimeUtc.AddSeconds(i));
        }
    }
}
