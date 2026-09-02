using System.Text.Json.Serialization;

namespace Stats.Core.Settings;

public sealed class AppSettings
{
    /// <summary>The one owner lock for this whole object graph. Everything that serializes the settings
    /// (<see cref="SettingsService.Serialize"/>) must hold it, and so must any thread other than the UI thread
    /// that mutates a collection in the graph — <see cref="Fans.FanController"/> uses it as its own gate, so a
    /// poll-thread fan change can never run while a save is walking <see cref="FanChannels"/>.</summary>
    [JsonIgnore] public object SyncRoot { get; } = new();

    public double PollIntervalSeconds { get; set; } = 1.0;
    /// <summary>True once first-run defaults have been applied; lets "user unchecked everything" survive restarts.</summary>
    public bool DefaultsApplied { get; set; }
    /// <summary>Selected dashboard metrics. ORDER = tile order within each group.</summary>
    public List<string> DashboardMetrics { get; set; } = new();
    public List<string> OverlayMetrics { get; set; } = new();
    /// <summary>Optional user-entered limits (e.g. PBO PPT watts) keyed by metric id; tile shows "% of limit" when set.</summary>
    public Dictionary<string, float> MetricLimits { get; set; } = new();
    public double OverlayOpacity { get; set; } = 0.85;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    // ---- v1.1 ----
    public Dictionary<string, TilePref> TilePrefs { get; set; } = new();
    public List<ThresholdRule> ThresholdRules { get; set; } = new();
    public Dictionary<string, ThresholdRule> ThresholdOverrides { get; set; } = new();
    public List<string> CollapsedGroups { get; set; } = new();
    public bool ShowCoreMatrix { get; set; } = true;
    public int HistoryWindowMinutes { get; set; } = 2;
    public OverlayOrientation OverlayOrientation { get; set; } = OverlayOrientation.Horizontal;
    public double OverlayFontScale { get; set; } = 1.0;
    public bool OverlayClickThrough { get; set; }
    /// <summary>Global hotkey text, e.g. "Ctrl+Shift+O". Empty = disabled.</summary>
    public string OverlayHotkey { get; set; } = "Ctrl+Shift+O";
    public double? PeaksLeft { get; set; }
    public double? PeaksTop { get; set; }
    public double? PeaksWidth { get; set; }
    public double? PeaksHeight { get; set; }

    // ---- v1.3 fan control ----
    /// <summary>Master switch: nothing is written to fan controls while false.</summary>
    public bool FanControlEnabled { get; set; }
    /// <summary>Per-channel desired state keyed by LHM control identifier (e.g. "/lpc/it8696e/0/control/0").</summary>
    public Dictionary<string, FanChannelPref> FanChannels { get; set; } = new();
    public double? FansLeft { get; set; }
    public double? FansTop { get; set; }
    public double? FansWidth { get; set; }
    public double? FansHeight { get; set; }

    // ---- v1.4 ----
    /// <summary>Poll motherboard Super-I/O and USB/HID fan controllers (needed for fan control); restart to apply.</summary>
    public bool ReadMotherboardAndCoolers { get; set; } = true;
    /// <summary>Saved fan configurations (e.g. Silent/Balanced/Gaming plus any user-named ones).</summary>
    public List<FanProfile> FanProfiles { get; set; } = new();
    /// <summary>Name of the profile that matches the live <see cref="FanChannels"/> state; null = "Custom"
    /// (edited since the last save/load of a profile).</summary>
    public string? ActiveFanProfile { get; set; }
    /// <summary>Switch fan profiles automatically based on foreground frame rate (see <see cref="Fans.GameModeSwitcher"/>).</summary>
    public bool GameModeEnabled { get; set; }
    /// <summary>Profile name applied while a game is detected in the foreground.</summary>
    public string? GameModeGamingProfile { get; set; }
    /// <summary>Profile name applied when no game has been active for a while; null = leave state as-is.</summary>
    public string? GameModeDesktopProfile { get; set; }
    /// <summary>Check GitHub Releases for a newer Stats version on startup (after a delay) and every 24 h; dev
    /// builds (version 0.0.0.*) never check regardless of this setting. Checkbox in Settings.</summary>
    public bool CheckForUpdatesAutomatically { get; set; } = true;
    /// <summary>Palette preset name (see <see cref="ThemePresets.Names"/>); unknown/legacy values sanitize to
    /// <see cref="ThemePresets.Default"/> at load.</summary>
    public string ThemePreset { get; set; } = ThemePresets.Default;
    /// <summary>Custom accent override as "#RRGGBB", or null to use the preset's own accent colour.</summary>
    public string? ThemeAccent { get; set; }

    // ---- v1.8 alerts ----
    /// <summary>Master switch for the alert engine (tray balloon + log); evaluated regardless of dashboard/overlay
    /// visibility.</summary>
    public bool AlertsEnabled { get; set; } = true;
    /// <summary>Seconds a metric must hold Crit before an alert raises; clamped 1–120 at load.</summary>
    public int AlertHoldSeconds { get; set; } = 10;
    /// <summary>Play <see cref="System.Media.SystemSounds.Exclamation"/> alongside the tray balloon.</summary>
    public bool AlertSoundEnabled { get; set; }

    // ---- v1.8 tray picker ----
    /// <summary>Metric id the tray icon renders; null = Auto (the CPU-temp heuristic in App). Resolved via
    /// <see cref="Tray.TrayMetricSelector.Resolve"/>, which falls back to null (Auto) when the id is missing or no
    /// longer discovered.</summary>
    public string? TrayMetricId { get; set; }

    // ---- v1.8 FPS hint ----
    /// <summary>The dashboard's "Gaming? Add FPS…" hint banner was dismissed via Got it; persists across restarts.</summary>
    public bool FpsHintDismissed { get; set; }

    /// <summary>Get-or-create the TilePref for a metric id.</summary>
    public TilePref PrefFor(string metricId)
    {
        if (!TilePrefs.TryGetValue(metricId, out var pref))
        {
            pref = new TilePref();
            TilePrefs[metricId] = pref;
        }
        return pref;
    }
}
