using System.Text.Json;

namespace Stats.Core.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
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
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            settings = new AppSettings();
        }
        settings.PollIntervalSeconds = Math.Clamp(settings.PollIntervalSeconds, 0.5, 5.0);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }
}
