using System.Text.Json;

namespace Stats.Core.Settings;

/// <summary>Portable decoration-only design. No machine identifiers, text or severity overrides.</summary>
public sealed record ThemeDesign(string Name, string Preset, string? Accent = null,
    string? Secondary = null, bool Gradient = true, bool Reactive = false, int Version = 1)
{
    public ThemeDesign Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl) ||
            !ThemePresets.Names.Contains(Preset) ||
            (Accent is not null && !ThemePresets.IsValidHex(Accent)) ||
            (Secondary is not null && !ThemePresets.IsValidHex(Secondary)))
            throw new InvalidDataException("Use a name of 1–80 characters, a known palette and #RRGGBB accents.");
        return this with { Name = Name.Trim(), Accent = Accent?.ToUpperInvariant(), Secondary = Secondary?.ToUpperInvariant() };
    }

    public static ThemeDesign Parse(string json)
    {
        if (json.Length > 8192) throw new InvalidDataException("Theme files must be smaller than 8 KB.");
        return (JsonSerializer.Deserialize<ThemeDesign>(json) ?? throw new InvalidDataException("Missing theme.")).Validate();
    }

    public string ToJson() => JsonSerializer.Serialize(Validate(), new JsonSerializerOptions { WriteIndented = true });
}
