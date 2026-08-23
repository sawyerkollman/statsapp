using System.Text.Json;
using System.Text.Json.Serialization;
using Stats.Core.Fans;
using Stats.Core.Metrics;

namespace Stats.Core.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly int[] AllowedHistoryMinutes = { 2, 5, 15, 60 };

    private readonly string _path;
    private readonly string _directory;

    public SettingsService(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            settings = new AppSettings();
        }
        settings.PollIntervalSeconds = Math.Clamp(settings.PollIntervalSeconds, 0.5, 5.0);
        settings.OverlayFontScale = Math.Clamp(settings.OverlayFontScale, 0.8, 1.6);
        settings.OverlayOpacity = Math.Clamp(settings.OverlayOpacity, 0.3, 1.0);
        settings.HistoryWindowMinutes = SnapHistoryMinutes(settings.HistoryWindowMinutes);
        // An explicit JSON null deserializes over the property initializer, so re-establish the empty collections.
        settings.ThresholdRules ??= new();
        settings.FanChannels ??= new();
        if (settings.ThresholdRules.Count == 0)
            settings.ThresholdRules = ThresholdDefaults.Rules();
        settings.OverlayHotkey ??= "";
        foreach (var pref in settings.FanChannels.Values)
        {
            pref.ManualPercent = Math.Clamp(pref.ManualPercent, 0f, 100f);
            if (!FanCurve.TryCreate(pref.Points, out _))
                pref.Points = FanCurve.DefaultPoints.ToList();
        }
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>Nearest allowed value (2/5/15/60); ties resolve upward.</summary>
    public static int SnapHistoryMinutes(int minutes) =>
        AllowedHistoryMinutes.OrderBy(a => Math.Abs(a - minutes)).ThenByDescending(a => a).First();
}
