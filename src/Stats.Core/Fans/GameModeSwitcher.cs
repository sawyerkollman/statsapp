using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Fans;

/// <summary>Applies the Gaming/Desktop fan profile based on foreground frame rate. Tick on the poll thread, BEFORE FanController.Tick.</summary>
public sealed class GameModeSwitcher
{
    public const float MinFps = 10f;
    public const string FpsMetricId = "fps.avg";
    public static readonly TimeSpan EnterAfter = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ExitAfter = TimeSpan.FromSeconds(20);
    private readonly FanController _controller;
    private readonly AppSettings _settings;
    private DateTime? _activeSince, _inactiveSince, _gamingSince;
    private string _note = "";

    public GameModeSwitcher(FanController controller, AppSettings settings) { _controller = controller; _settings = settings; }

    public bool IsGaming { get; private set; }

    public string StatusText => !_settings.GameModeEnabled ? "Game mode: off"
        : IsGaming ? $"Game mode: gaming ({_settings.GameModeGamingProfile ?? "no profile"} since {_gamingSince:HH:mm}){_note}"
        : $"Game mode: desktop{_note}";

    public void Tick(SensorSnapshot snapshot, DateTime nowUtc)
    {
        if (!_settings.GameModeEnabled) { if (IsGaming) IsGaming = false; _activeSince = _inactiveSince = null; return; }
        bool active = snapshot.Values.TryGetValue(FpsMetricId, out var v) && v is float f && !float.IsNaN(f) && f >= MinFps;
        if (active) { _activeSince ??= nowUtc; _inactiveSince = null; } else { _inactiveSince ??= nowUtc; _activeSince = null; }
        if (!IsGaming && _activeSince is DateTime a && nowUtc - a >= EnterAfter) { IsGaming = true; _gamingSince = nowUtc; Apply(_settings.GameModeGamingProfile); }
        else if (IsGaming && _inactiveSince is DateTime i && nowUtc - i >= ExitAfter) { IsGaming = false; Apply(_settings.GameModeDesktopProfile); }
    }

    private void Apply(string? name)
    {
        _note = "";
        if (string.IsNullOrWhiteSpace(name)) return;
        var prof = _settings.FanProfiles.FirstOrDefault(p => p.Name == name);
        if (prof is null) { _note = " (profile not found)"; return; }
        _controller.ApplyProfile(prof, deferSave: true);
    }
}
