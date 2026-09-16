using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class SceneMapping : ObservableObject
{
    public required PortableSceneSlot Slot { get; init; }
    public required IReadOnlyList<MetricDefinition> Choices { get; init; }
    public string Label => $"{Slot.Group} · {Slot.Unit} · {(Slot.Dashboard ? "Dashboard" : "")} {(Slot.Overlay ? "Overlay" : "")}";
    [ObservableProperty] private MetricDefinition? _selected;
}
public sealed partial class SceneSectionLabel : ObservableObject
{
    public required string Group { get; init; }
    [ObservableProperty] private string _label = "";
}
public sealed partial class SceneComposerViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly IReadOnlyList<MetricDefinition> _definitions;
    private PortableScene? _import;
    public SceneComposerViewModel(AppSettings settings, IEnumerable<MetricDefinition> definitions, Action save)
    {
        _settings = settings; _definitions = definitions.ToArray(); _save = save;
        Scenes = new(settings.Scenes);
        foreach (var group in Enum.GetValues<MetricGroup>())
            SectionLabels.Add(new() { Group = group.ToString(), Label = settings.SceneSectionLabels.GetValueOrDefault(group.ToString(), "") });
        OverlayVertical = settings.OverlayOrientation == OverlayOrientation.Vertical;
        OverlayScale = settings.OverlayFontScale; OverlayOpacity = settings.OverlayOpacity;
        OverlaySparklines = settings.OverlayGraphs == OverlayGraphs.Sparkline;
    }
    public ObservableCollection<DashboardScene> Scenes { get; }
    public ObservableCollection<SceneMapping> Mappings { get; } = new();
    public ObservableCollection<SceneSectionLabel> SectionLabels { get; } = new();
    public event Action<DashboardScene, bool>? SceneApplied;
    [ObservableProperty] private string _name = "My scene";
    [ObservableProperty] private string _caption = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private DashboardScene? _selectedScene;
    [ObservableProperty] private bool _overlayVertical;
    [ObservableProperty] private double _overlayScale = 1;
    [ObservableProperty] private double _overlayOpacity = .85;
    [ObservableProperty] private bool _overlaySparklines = true;
    partial void OnSelectedSceneChanged(DashboardScene? value)
    {
        if (value is null) return;
        Name = value.Name; Caption = value.Caption;
        foreach (var row in SectionLabels) row.Label = value.SectionLabels.GetValueOrDefault(row.Group, "");
        OverlayVertical = value.OverlayOrientation == OverlayOrientation.Vertical;
        OverlayScale = value.OverlayFontScale; OverlayOpacity = value.OverlayOpacity;
        OverlaySparklines = value.OverlayGraphs == OverlayGraphs.Sparkline;
    }
    private DashboardScene Capture(LayoutProfile layout) => new()
    {
        Name = Name.Trim(), Caption = Caption, Layout = layout,
        SectionLabels = SectionLabels.Where(r => !string.IsNullOrWhiteSpace(r.Label)).ToDictionary(r => r.Group, r => r.Label.Trim()),
        OverlayOrientation = OverlayVertical ? OverlayOrientation.Vertical : OverlayOrientation.Horizontal,
        OverlayFontScale = OverlayScale, OverlayOpacity = OverlayOpacity,
        OverlayGraphs = OverlaySparklines ? OverlayGraphs.Sparkline : OverlayGraphs.None,
    };
    [RelayCommand] private void SaveCurrent()
    {
        try { Save(Capture(LayoutProfile.Capture(_settings, Name))); }
        catch (Exception ex) { Error = ex.Message; }
    }
    private void Save(DashboardScene scene)
    {
        scene.Validate();
        var old = Scenes.FirstOrDefault(s => s.Name == scene.Name);
        if (old is null && Scenes.Count >= 50) throw new InvalidDataException("Save up to 50 scenes.");
        if (old is not null) Scenes[Scenes.IndexOf(old)] = scene; else Scenes.Add(scene);
        _settings.Scenes = Scenes.ToList(); _save(); SelectedScene = scene; Error = "";
    }
    [RelayCommand] private void ApplySelected() => Apply(false);
    [RelayCommand] private void ApplyOverlayOnly() => Apply(true);
    private void Apply(bool overlayOnly)
    {
        if (SelectedScene is not { } scene) return;
        try { scene.Validate(); SceneApplied?.Invoke(scene, overlayOnly); Error = ""; }
        catch (Exception ex) { Error = ex.Message; }
    }
    [RelayCommand] private void DeleteSelected()
    {
        if (SelectedScene is not { } scene) return;
        Scenes.Remove(scene); _settings.Scenes = Scenes.ToList(); _save(); SelectedScene = null;
    }
    public string ExportSelected()
    {
        var scene = SelectedScene ?? throw new InvalidDataException("Select a saved scene to export.");
        scene.Validate();
        if (scene.Layout.DashboardMetrics.Concat(scene.Layout.OverlayMetrics).Any(id => !_definitions.Any(d => d.Id == id)))
            throw new InvalidDataException("This scene includes unavailable sensors. Remap them before exporting.");
        var portable = new PortableScene
        {
            Name = scene.Name, Mode = scene.Layout.DashboardLayoutMode,
            SectionLabels = new(scene.SectionLabels),
            Slots = scene.Layout.DashboardMetrics.Concat(scene.Layout.OverlayMetrics).Distinct()
                .Select(id => _definitions.FirstOrDefault(d => d.Id == id)).OfType<MetricDefinition>()
                .Select(d =>
                {
                    var pref = scene.Layout.TilePrefs.GetValueOrDefault(d.Id) ?? new();
                    return new PortableSceneSlot { Group = d.Group, Unit = d.Unit, Kind = pref.Kind, Size = pref.Size,
                        X = pref.X, Y = pref.Y, Dashboard = scene.Layout.DashboardMetrics.Contains(d.Id), Overlay = scene.Layout.OverlayMetrics.Contains(d.Id) };
                }).ToList(),
        };
        portable.Validate();
        return JsonSerializer.Serialize(portable, new JsonSerializerOptions { WriteIndented = true });
    }
    public void Import(string json)
    {
        if (json.Length > 1024 * 1024) throw new InvalidDataException("Scene exceeds 1 MB.");
        var imported = JsonSerializer.Deserialize<PortableScene>(json) ?? throw new InvalidDataException("Missing scene.");
        imported.Validate();
        var mappings = imported.Slots.Select(slot => new SceneMapping { Slot = slot,
            Choices = _definitions.Where(d => d.Group == slot.Group && d.Unit == slot.Unit).ToArray() }).ToArray();
        _import = imported; Mappings.Clear(); foreach (var mapping in mappings) Mappings.Add(mapping);
        Caption = "";
        foreach (var row in SectionLabels) row.Label = imported.SectionLabels.GetValueOrDefault(row.Group, "");
        Name = imported.Name; Error = "Map each slot explicitly, then save mapped scene. Imported files never select sensors automatically.";
    }
    [RelayCommand] private void ImportMapped()
    {
        try
        {
            if (_import is null || Mappings.Any(m => m.Selected is null || !m.Choices.Contains(m.Selected)) ||
                Mappings.Select(m => m.Selected!.Id).Distinct().Count() != Mappings.Count)
                throw new InvalidDataException("Choose a distinct compatible metric for every slot.");
            var layout = new LayoutProfile { Name = Name, DashboardLayoutMode = _import.Mode, ShowCoreMatrix = false };
            foreach (var mapping in Mappings)
            {
                var id = mapping.Selected!.Id; var slot = mapping.Slot;
                if (slot.Dashboard) layout.DashboardMetrics.Add(id);
                if (slot.Overlay) layout.OverlayMetrics.Add(id);
                layout.TilePrefs[id] = new TilePref { Kind = slot.Kind, Size = slot.Size, X = slot.X, Y = slot.Y };
            }
            Save(Capture(layout)); Mappings.Clear(); _import = null;
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}
