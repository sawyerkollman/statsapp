using Stats.Core.Frames;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Fans;

/// <summary>Applies the Gaming/Desktop fan profile based on foreground frame rate. Tick on the poll thread, BEFORE FanController.Tick.</summary>
public sealed class GameModeSwitcher
{
    public const float MinFps = 10f;
    /// <summary>Same id the frame reader publishes under — bound to the constant that owns it so a rename
    /// cannot silently stop game mode from ever seeing a frame rate.</summary>
    public const string FpsMetricId = FrameMetrics.FpsId;
    public static readonly TimeSpan EnterAfter = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ExitAfter = TimeSpan.FromSeconds(20);
    private readonly FanController _controller;
    private readonly AppSettings _settings;
    private DateTime? _activeSince, _inactiveSince, _gamingSince;
    private string _note = "";
    /// <summary>Composed on the poll thread at the end of every Tick; the UI thread only reads this one
    /// reference, so it can never tear a DateTime? or read half a transition.</summary>
    private volatile string _status = "Game mode: desktop";

    public GameModeSwitcher(FanController controller, AppSettings settings) { _controller = controller; _settings = settings; }

    public bool IsGaming { get; private set; }

    public string StatusText => _settings.GameModeEnabled ? _status : "Game mode: off";

    public void Tick(SensorSnapshot snapshot, DateTime nowUtc)
    {
        if (!_settings.GameModeEnabled)
        {
            if (IsGaming) IsGaming = false;
            _activeSince = _inactiveSince = null;
            _note = "";
            UpdateStatus();
            return;
        }
        bool active = snapshot.Values.TryGetValue(FpsMetricId, out var v) && v is float f && !float.IsNaN(f) && f >= MinFps;
        if (active) { _activeSince ??= nowUtc; _inactiveSince = null; } else { _inactiveSince ??= nowUtc; _activeSince = null; }
        // The transition is only recorded once Apply has returned: if applying throws (e.g. the profile list was
        // mutated underneath us) the next tick retries instead of latching IsGaming with the old curve running.
        if (!IsGaming && _activeSince is DateTime a && nowUtc - a >= EnterAfter)
        {
            Apply(_settings.GameModeGamingProfile);
            IsGaming = true;
            _gamingSince = nowUtc;
        }
        else if (IsGaming && _inactiveSince is DateTime i && nowUtc - i >= ExitAfter)
        {
            Apply(_settings.GameModeDesktopProfile);
            IsGaming = false;
        }
        UpdateStatus();
    }

    private void Apply(string? name)
    {
        _note = "";
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!_controller.TryGetProfile(name, out var prof)) { _note = " (profile not found)"; return; }
        if (_controller.ActiveProfile == name) return; // already live: re-applying would discard channel edits
        _controller.ApplyProfile(prof!, deferSave: true, resetFailures: false);
    }

    private void UpdateStatus() => _status = IsGaming
        ? $"Game mode: gaming ({_settings.GameModeGamingProfile ?? "no profile"} since {_gamingSince?.ToLocalTime():HH:mm}){_note}"
        : $"Game mode: desktop{_note}";
}
