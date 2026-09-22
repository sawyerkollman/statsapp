using Stats.Core.Settings;
namespace Stats.Core.Lab;
public static class GameAppearance
{
    public static bool Apply(AppSettings settings, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var design = settings.ThemeDesigns.FirstOrDefault(x => x.Name == name);
        if (design is not null) { var d = design.Validate(); settings.ThemePreset = d.Preset; settings.ThemeAccent = d.Accent; settings.ThemeSecondary = d.Secondary; settings.ThemeGradient = d.Gradient; settings.ReactiveDecorations = d.Reactive; return true; }
        if (!ThemePresets.Names.Contains(name)) return false;
        settings.ThemePreset = name; settings.ThemeAccent = null; settings.ThemeSecondary = null; return true;
    }
}
