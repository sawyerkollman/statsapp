using System.Text.Json;

namespace Stats.Core.Lab;

public sealed class LabOptions
{
    public bool HistoryEnabled { get; set; }
    public int RetentionDays { get; set; } = 7;
    public int MaximumHistoryMb { get; set; } = 64;
    public bool AutoGamingEnabled { get; set; }
    public List<GameLabOption> Games { get; set; } = new();
    public List<CompoundRule> Rules { get; set; } = new();
    public List<NamedCompoundRuleSet> NamedCompoundRuleSets { get; set; } = new();
}

public sealed class GameLabOption
{
    public string ExecutableBaseName { get; set; } = "";
    public string LayoutName { get; set; } = "";
    public string OverlaySceneName { get; set; } = "";
    public bool AutoRecord { get; set; }
    public bool RestoreDesktopAfterGame { get; set; }
    public string ThemeName { get; set; } = "";
    public string AlertSetName { get; set; } = "";
}
public sealed class NamedCompoundRuleSet { public string Name { get; set; } = ""; public List<CompoundRule> Rules { get; set; } = new(); }

public sealed class CompoundRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FirstMetricId { get; set; } = "";
    public string SecondMetricId { get; set; } = "";
    public float FirstThreshold { get; set; }
    public float SecondThreshold { get; set; }
    public bool FirstLower { get; set; }
    public bool SecondLower { get; set; }
    public int HoldSeconds { get; set; } = 5;
    public int CooldownSeconds { get; set; } = 60;
    public bool Enabled { get; set; }
    public bool TestOnly { get; set; }
}

public static class LabOptionsStore
{
    public static string? Error { get; private set; }
    public static LabOptions Load(string directory)
    {
        try
        {
            var path = Path.Combine(directory, "lab-options.json");
            if (!File.Exists(path)) { Error = null; return new(); }
            if (new FileInfo(path).Length > 1_000_000) throw new InvalidDataException("Lab options exceed 1 MB.");
            Error = null;
            return Sanitize(JsonSerializer.Deserialize<LabOptions>(File.ReadAllText(path)) ?? new());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { Error = ex.Message; return new(); }
    }
    public static void Save(string directory, LabOptions options)
    {
        var path = Path.Combine(directory, "lab-options.json"); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { options = Sanitize(options); var json = JsonSerializer.Serialize(options); if (System.Text.Encoding.UTF8.GetByteCount(json) > 1_000_000) throw new IOException("Lab options exceed 1 MB; shorten rule sets before saving."); Directory.CreateDirectory(directory); File.WriteAllText(temporary, json); File.Move(temporary, path, true); Error = null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = ex.Message; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error ??= ex.Message; } }
    }
    public static LabOptions Sanitize(LabOptions options)
    {
        options ??= new(); options.Games ??= new(); options.Rules ??= new(); options.NamedCompoundRuleSets ??= new();
        options.RetentionDays = Math.Clamp(options.RetentionDays, 1, 90);
        options.MaximumHistoryMb = Math.Clamp(options.MaximumHistoryMb, 16, 512);
        options.Games = options.Games.Where(x => x is not null).Take(100).ToList(); options.Rules = options.Rules.Where(x => x is not null).Take(32).ToList();
        foreach (var game in options.Games) { game.ExecutableBaseName = Text(game.ExecutableBaseName, 100); game.LayoutName = Text(game.LayoutName, 100); game.OverlaySceneName = Text(game.OverlaySceneName, 100); game.ThemeName = Text(game.ThemeName, 100); game.AlertSetName = Text(game.AlertSetName, 100); }
        var ids = new HashSet<string>();
        foreach (var rule in options.Rules)
        {
            SanitizeRule(rule, ids);
        }
        options.NamedCompoundRuleSets = options.NamedCompoundRuleSets.Where(s => s is not null).Take(16).Select(s => { var setIds = new HashSet<string>(); var rules = (s.Rules ?? []).Where(r => r is not null).Take(32).Select(Clone).ToList(); foreach (var rule in rules) SanitizeRule(rule, setIds); return new NamedCompoundRuleSet { Name = Text(s.Name, 100), Rules = rules }; }).Where(s => s.Name.Length > 0).ToList();
        return options;
    }
    public static CompoundRule Clone(CompoundRule rule) => new() { Id = rule.Id, FirstMetricId = rule.FirstMetricId, SecondMetricId = rule.SecondMetricId, FirstThreshold = rule.FirstThreshold, SecondThreshold = rule.SecondThreshold, FirstLower = rule.FirstLower, SecondLower = rule.SecondLower, HoldSeconds = rule.HoldSeconds, CooldownSeconds = rule.CooldownSeconds, Enabled = rule.Enabled, TestOnly = rule.TestOnly };
    private static void SanitizeRule(CompoundRule rule, HashSet<string> ids) { if (!Guid.TryParseExact(rule.Id, "N", out _) || !ids.Add(rule.Id)) { rule.Id = Guid.NewGuid().ToString("N"); ids.Add(rule.Id); } rule.FirstMetricId = Text(rule.FirstMetricId, 1024); rule.SecondMetricId = Text(rule.SecondMetricId, 1024); if (!float.IsFinite(rule.FirstThreshold) || !float.IsFinite(rule.SecondThreshold)) { rule.FirstThreshold = 0; rule.SecondThreshold = 0; rule.Enabled = false; rule.TestOnly = true; } rule.HoldSeconds = Math.Clamp(rule.HoldSeconds, 1, 120); rule.CooldownSeconds = Math.Clamp(rule.CooldownSeconds, 1, 3600); }
    private static string Text(string? value, int maximum) { var text = (value ?? "").Trim(); return text[..Math.Min(text.Length, maximum)]; }
}
