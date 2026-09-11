using System.Diagnostics;
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
    /// <summary>Serializes the tmp-write + move so two writers can never share the same tmp path.</summary>
    private static readonly object FileGate = new();

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
            Normalize(settings);
        }
        catch (Exception ex)
        {
            // Anything at all — a corrupt file, an unreadable directory, a sanitation bug over hand-edited JSON —
            // must leave Stats startable: OnStartup calls Load() before any window exists and never catches.
            Trace.WriteLine("[Stats] settings load failed; using defaults: " + ex.Message);
            settings = new AppSettings();
            Normalize(settings);
        }
        return settings;
    }

    /// <summary>Clamps, null-guards and migrates a just-deserialized settings object. Must be safe to run over a
    /// fresh <see cref="AppSettings"/> too (that is the fallback path when loading throws).</summary>
    private static void Normalize(AppSettings settings)
    {
        settings.PollIntervalSeconds = Math.Clamp(settings.PollIntervalSeconds, 0.5, 5.0);
        settings.OverlayFontScale = Math.Clamp(settings.OverlayFontScale, 0.8, 1.6);
        settings.OverlayOpacity = Math.Clamp(settings.OverlayOpacity, 0.3, 1.0);
        settings.HistoryWindowMinutes = SnapHistoryMinutes(settings.HistoryWindowMinutes);
        settings.AlertHoldSeconds = Math.Clamp(settings.AlertHoldSeconds, 1, 120);
        settings.DashboardUiScale = Math.Clamp(settings.DashboardUiScale, 0.9, 1.3);
        settings.CoreMatrixX = SanitizePosition(settings.CoreMatrixX);
        settings.CoreMatrixY = SanitizePosition(settings.CoreMatrixY);
        foreach (var pref in settings.TilePrefs.Values.Where(p => p is not null))
        {
            pref.X = SanitizePosition(pref.X);
            pref.Y = SanitizePosition(pref.Y);
        }
        // An explicit JSON null deserializes over the property initializer, so re-establish the empty collections.
        settings.ThresholdRules ??= new();
        settings.ThresholdOverrides ??= new();
        settings.FanChannels ??= new();
        settings.FanProfiles ??= new();
        settings.IgnoredFanConflicts ??= new();
        ThresholdDefaults.EnsureDefaults(settings.ThresholdRules, settings.ThresholdOverrides);
        settings.OverlayHotkey ??= "";
        settings.ThemePreset = ThemePresets.SanitizePresetName(settings.ThemePreset);
        settings.ThemeAccent = ThemePresets.SanitizeAccentHex(settings.ThemeAccent);
        // Null *elements* deserialize just as happily as null collections ({"FanChannels":{"a":null}}), and the
        // sanitation below dereferences every one of them.
        settings.FanChannels = settings.FanChannels.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var pref in settings.FanChannels.Values) Sanitize(pref);
        settings.FanProfiles.RemoveAll(p => p is null);
        foreach (var profile in settings.FanProfiles)
        {
            profile.Name ??= "";
            profile.Channels = (profile.Channels ?? new()).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var pref in profile.Channels.Values) Sanitize(pref);
        }

        static void Sanitize(FanChannelPref pref)
        {
            pref.ManualPercent = Math.Clamp(pref.ManualPercent, 0f, 100f);
            if (!FanCurve.TryCreate(pref.Points, out _))
                pref.Points = FanCurve.DefaultPoints.ToList();
            pref.SourceMetricIds ??= new();
            if (pref.SourceMetricIds.Count == 0 && !string.IsNullOrWhiteSpace(pref.SourceMetricId))
                pref.SourceMetricIds.Add(pref.SourceMetricId);
            pref.SourceMetricId = pref.SourceMetricIds.FirstOrDefault();
        }
    }

    /// <summary>Snapshot the graph as JSON. The caller MUST hold <see cref="AppSettings.SyncRoot"/> across this
    /// call — the serializer enumerates every collection in the graph.</summary>
    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>Convenience for single-threaded callers (tests, tools): serializes under the settings lock.</summary>
    public void Save(AppSettings settings)
    {
        string json;
        lock (settings.SyncRoot) json = Serialize(settings);
        Save(json);
    }

    /// <summary>Atomically replace settings.json with already-serialized JSON. No settings object is touched here,
    /// so this half can run outside the settings lock — only the file write needs serializing.</summary>
    public void Save(string json)
    {
        lock (FileGate)
        {
            Directory.CreateDirectory(_directory);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
    }

    /// <summary>Nearest allowed value (2/5/15/60); ties resolve upward.</summary>
    public static int SnapHistoryMinutes(int minutes) =>
        AllowedHistoryMinutes.OrderBy(a => Math.Abs(a - minutes)).ThenByDescending(a => a).First();

    /// <summary>A NaN, negative, or absurdly large (&gt; 100 000) dashboard-canvas coordinate sanitizes to null
    /// ("not yet placed" — the seed pack re-places it) rather than being clamped into range, since a corrupt
    /// stored value carries no useful position information to preserve.</summary>
    private static double? SanitizePosition(double? v) =>
        v is double d && !double.IsNaN(d) && d >= 0 && d <= 100_000 ? d : null;
}
