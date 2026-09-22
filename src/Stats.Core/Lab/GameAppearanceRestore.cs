using Stats.Core.Settings;

namespace Stats.Core.Lab;

[Flags]
public enum GameAppearanceDomains
{
    None = 0,
    Theme = 1,
    Layout = 2,
    Overlay = 4,
    CompoundRules = 8,
}

public readonly record struct GameAppearanceRestoreResult(bool Theme, bool Layout, bool Overlay, bool CompoundRules);

/// <summary>Restores only automatic game appearance changes that the user has not subsequently changed.</summary>
public sealed class GameAppearanceRestore
{
    private ThemeState? _beforeTheme, _appliedTheme;
    private LayoutState? _beforeLayout, _appliedLayout;
    private OverlayState? _beforeOverlay, _appliedOverlay;
    private List<CompoundRule>? _beforeRules, _appliedRules;
    private GameAppearanceDomains _manualChanges;

    public void CaptureBefore(AppSettings settings, IEnumerable<CompoundRule> rules, GameAppearanceDomains domains = GameAppearanceDomains.Theme | GameAppearanceDomains.Layout | GameAppearanceDomains.Overlay | GameAppearanceDomains.CompoundRules)
    {
        _beforeTheme = domains.HasFlag(GameAppearanceDomains.Theme) ? ThemeState.Capture(settings) : null;
        _beforeLayout = domains.HasFlag(GameAppearanceDomains.Layout) ? LayoutState.Capture(settings) : null;
        _beforeOverlay = domains.HasFlag(GameAppearanceDomains.Overlay) ? OverlayState.Capture(settings) : null;
        _beforeRules = domains.HasFlag(GameAppearanceDomains.CompoundRules) ? CloneRules(rules) : null;
        _appliedTheme = null; _appliedLayout = null; _appliedOverlay = null; _appliedRules = null;
        _manualChanges = GameAppearanceDomains.None;
    }

    /// <summary>Call after all automatic game choices have been applied, with only the domains that changed.</summary>
    public void Complete(AppSettings settings, IEnumerable<CompoundRule> rules, GameAppearanceDomains applied)
    {
        _appliedTheme = applied.HasFlag(GameAppearanceDomains.Theme) ? ThemeState.Capture(settings) : null;
        _appliedLayout = applied.HasFlag(GameAppearanceDomains.Layout) ? LayoutState.Capture(settings) : null;
        _appliedOverlay = applied.HasFlag(GameAppearanceDomains.Overlay) ? OverlayState.Capture(settings) : null;
        _appliedRules = applied.HasFlag(GameAppearanceDomains.CompoundRules) ? CloneRules(rules) : null;
        _manualChanges = GameAppearanceDomains.None;
    }

    /// <summary>Marks a domain changed permanently for this game run; returning it to its automatic value does not erase the user's choice.</summary>
    public void ObserveChanges(AppSettings settings, IEnumerable<CompoundRule> rules)
    {
        if (_appliedTheme is not null && _appliedTheme != ThemeState.Capture(settings)) _manualChanges |= GameAppearanceDomains.Theme;
        if (_appliedLayout is not null && !_appliedLayout.Matches(settings)) _manualChanges |= GameAppearanceDomains.Layout;
        if (_appliedOverlay is not null && !_appliedOverlay.Matches(settings)) _manualChanges |= GameAppearanceDomains.Overlay;
        if (_appliedRules is not null && !RulesEqual(_appliedRules, rules)) _manualChanges |= GameAppearanceDomains.CompoundRules;
    }

    public GameAppearanceRestoreResult Restore(AppSettings settings, IList<CompoundRule> rules)
    {
        ObserveChanges(settings, rules);
        var theme = RestoreTheme(settings);
        var layout = RestoreLayout(settings);
        var overlay = RestoreOverlay(settings);
        var compoundRules = RestoreRules(rules);
        return new(theme, layout, overlay, compoundRules);
    }

    private bool RestoreTheme(AppSettings settings)
    {
        if (_beforeTheme is null || _appliedTheme is null || _manualChanges.HasFlag(GameAppearanceDomains.Theme) || _appliedTheme != ThemeState.Capture(settings)) return false;
        _beforeTheme.Apply(settings); return true;
    }

    private bool RestoreLayout(AppSettings settings)
    {
        if (_beforeLayout is null || _appliedLayout is null || _manualChanges.HasFlag(GameAppearanceDomains.Layout) || !_appliedLayout.Matches(settings)) return false;
        _beforeLayout.Apply(settings); return true;
    }

    private bool RestoreOverlay(AppSettings settings)
    {
        if (_beforeOverlay is null || _appliedOverlay is null || _manualChanges.HasFlag(GameAppearanceDomains.Overlay) || !_appliedOverlay.Matches(settings)) return false;
        _beforeOverlay.Apply(settings); return true;
    }

    private bool RestoreRules(IList<CompoundRule> rules)
    {
        if (_beforeRules is null || _appliedRules is null || _manualChanges.HasFlag(GameAppearanceDomains.CompoundRules) || !RulesEqual(_appliedRules, rules)) return false;
        rules.Clear(); foreach (var rule in _beforeRules) rules.Add(LabOptionsStore.Clone(rule)); return true;
    }

    private static List<CompoundRule> CloneRules(IEnumerable<CompoundRule> rules) => rules.Select(LabOptionsStore.Clone).ToList();
    private static bool RulesEqual(IEnumerable<CompoundRule> left, IEnumerable<CompoundRule> right) =>
        left.Select(RuleState.Capture).SequenceEqual(right.Select(RuleState.Capture));

    private sealed record ThemeState(string Preset, string? Accent, string? Secondary, bool Gradient, bool Reactive)
    {
        public static ThemeState Capture(AppSettings s) => new(s.ThemePreset, s.ThemeAccent, s.ThemeSecondary, s.ThemeGradient, s.ReactiveDecorations);
        public void Apply(AppSettings s) { s.ThemePreset = Preset; s.ThemeAccent = Accent; s.ThemeSecondary = Secondary; s.ThemeGradient = Gradient; s.ReactiveDecorations = Reactive; }
    }

    // OverlayMetrics deliberately stay out of this fingerprint and restore: dashboard layout changes must not erase an overlay edit.
    private sealed record LayoutState(LayoutProfile Layout, Dictionary<string, string> SectionLabels, string? ActiveProfile, bool Modified, bool Locked)
    {
        public static LayoutState Capture(AppSettings s) => new(LayoutProfile.Capture(s, ""), new(s.SceneSectionLabels), s.ActiveLayoutProfile, s.LayoutProfileModified, s.IsLayoutLocked);
        public bool Matches(AppSettings s) => ActiveProfile == s.ActiveLayoutProfile && Modified == s.LayoutProfileModified && Locked == s.IsLayoutLocked && SectionLabels.OrderBy(x => x.Key).SequenceEqual(s.SceneSectionLabels.OrderBy(x => x.Key)) && LayoutMatches(Layout, LayoutProfile.Capture(s, ""));
        public void Apply(AppSettings s)
        {
            var display = s.TilePrefs.ToDictionary(p => p.Key, p => (p.Value.Name, p.Value.Max));
            s.DashboardMetrics.Clear(); s.DashboardMetrics.AddRange(Layout.DashboardMetrics);
            s.CollapsedGroups.Clear(); s.CollapsedGroups.AddRange(Layout.CollapsedGroups);
            s.TilePrefs.Clear(); foreach (var pair in Layout.TilePrefs) s.TilePrefs[pair.Key] = LayoutProfile.CloneArrangement(pair.Value);
            foreach (var (id, value) in display) { if (value.Name is not null || value.Max is not null) { var pref = s.PrefFor(id); pref.Name = value.Name; pref.Max = value.Max; } }
            s.ShowCoreMatrix = Layout.ShowCoreMatrix; s.DashboardLayoutMode = Layout.DashboardLayoutMode; s.CoreMatrixX = Layout.CoreMatrixX; s.CoreMatrixY = Layout.CoreMatrixY;
            s.SceneSectionLabels.Clear(); foreach (var pair in SectionLabels) s.SceneSectionLabels[pair.Key] = pair.Value;
            s.ActiveLayoutProfile = ActiveProfile; s.LayoutProfileModified = Modified; s.IsLayoutLocked = Locked;
        }
        private static bool LayoutMatches(LayoutProfile a, LayoutProfile b) =>
            a.DashboardMetrics.SequenceEqual(b.DashboardMetrics) && a.CollapsedGroups.SequenceEqual(b.CollapsedGroups) &&
            a.ShowCoreMatrix == b.ShowCoreMatrix && a.DashboardLayoutMode == b.DashboardLayoutMode && a.CoreMatrixX == b.CoreMatrixX && a.CoreMatrixY == b.CoreMatrixY &&
            a.TilePrefs.Count == b.TilePrefs.Count && a.TilePrefs.All(pair => b.TilePrefs.TryGetValue(pair.Key, out var other) &&
                pair.Value.Kind == other.Kind && pair.Value.Size == other.Size && pair.Value.X == other.X && pair.Value.Y == other.Y);
    }

    private sealed record OverlayState(List<string> Metrics, OverlayOrientation Orientation, double FontScale, double Opacity, OverlayGraphs Graphs, OverlayCanvasLayout? Canvas)
    {
        public static OverlayState Capture(AppSettings s) => new(s.OverlayMetrics.ToList(), s.OverlayOrientation, s.OverlayFontScale, s.OverlayOpacity, s.OverlayGraphs, s.OverlayCanvas?.Clone());
        public bool Matches(AppSettings s) => Metrics.SequenceEqual(s.OverlayMetrics) && Orientation == s.OverlayOrientation && FontScale == s.OverlayFontScale && Opacity == s.OverlayOpacity && Graphs == s.OverlayGraphs && CanvasMatches(Canvas, s.OverlayCanvas);
        public void Apply(AppSettings s) { s.OverlayMetrics.Clear(); s.OverlayMetrics.AddRange(Metrics); s.OverlayOrientation = Orientation; s.OverlayFontScale = FontScale; s.OverlayOpacity = Opacity; s.OverlayGraphs = Graphs; s.OverlayCanvas = Canvas?.Clone(); }
        private static bool CanvasMatches(OverlayCanvasLayout? a, OverlayCanvasLayout? b) => a is null ? b is null : b is not null && a.FixedNeonBorder == b.FixedNeonBorder && a.Elements.Count == b.Elements.Count && a.Elements.Zip(b.Elements).All(pair => pair.First.MetricId == pair.Second.MetricId && pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y && pair.First.Width == pair.Second.Width && pair.First.Height == pair.Second.Height);
    }

    private sealed record RuleState(string Id, string First, string Second, float FirstThreshold, float SecondThreshold, bool FirstLower, bool SecondLower, int Hold, int Cooldown, bool Enabled, bool TestOnly)
    {
        public static RuleState Capture(CompoundRule r) => new(r.Id, r.FirstMetricId, r.SecondMetricId, r.FirstThreshold, r.SecondThreshold, r.FirstLower, r.SecondLower, r.HoldSeconds, r.CooldownSeconds, r.Enabled, r.TestOnly);
    }
}
