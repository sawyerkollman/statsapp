using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class OverlayEditorViewModel : ObservableObject
{
    private const int HistoryLimit = 32;
    private sealed record Draft(OverlayCanvasLayout Layout, string[] SelectedIds, string? ActiveId);
    private readonly AppSettings _settings; private readonly MetricStore _store; private readonly Action _save;
    private readonly Stack<Draft> _undo = new(), _redo = new(); private Draft? _dragBefore, _numericBefore; private bool _importBorder;
    public OverlayEditorViewModel(AppSettings settings, MetricStore store, Action save)
    { _settings = settings; _store = store; _save = save; AvailableMetrics = store.Definitions.ToArray(); var current = OverlayCanvasLayout.Normalize(settings.OverlayCanvas) ?? NewLayout(settings.OverlayMetrics); FixedNeonBorder = current.FixedNeonBorder; foreach (var e in current.Elements) Add(e); SelectedSlot = Slots.FirstOrDefault(); RebuildPreview(); }
    public IReadOnlyList<MetricDefinition> AvailableMetrics { get; }
    public ObservableCollection<OverlayCanvasSlot> Slots { get; } = new(); public ObservableCollection<MetricTileViewModel> PreviewTiles { get; } = new(); public ObservableCollection<OverlayCanvasImportMapping> ImportMappings { get; } = new(); public event Action? Applied;
    [ObservableProperty] private MetricDefinition? _selectedMetric; [ObservableProperty] private OverlayCanvasSlot? _selectedSlot; [ObservableProperty] private bool _fixedNeonBorder; [ObservableProperty] private string _error = ""; [ObservableProperty] private double _zoom = .5; [ObservableProperty] private double _guideX; [ObservableProperty] private double _guideY; [ObservableProperty] private bool _showVerticalGuide; [ObservableProperty] private bool _showHorizontalGuide;
    public bool CanUndo => _undo.Count > 0; public bool CanRedo => _redo.Count > 0;
    public void Refresh() { var t = ThresholdIndex.Build(_settings); foreach (var tile in PreviewTiles) tile.Refresh(t); }
    public void Select(OverlayCanvasSlot slot, bool toggle) { if (!toggle) foreach (var s in Slots) s.IsSelected = false; slot.IsSelected = toggle ? !slot.IsSelected : true; SelectedSlot = slot; UpdateGuides(); }
    public void SelectForManipulation(OverlayCanvasSlot slot, bool extend)
    {
        if (!slot.IsSelected)
        {
            if (!extend) foreach (var other in Slots) other.IsSelected = false;
            slot.IsSelected = true;
        }
        SelectedSlot = slot;
        UpdateGuides();
    }
    public void SetSelection(IEnumerable<OverlayCanvasSlot> slots) { var selected = slots.ToHashSet(); foreach (var slot in Slots) slot.IsSelected = selected.Contains(slot); SelectedSlot = Slots.FirstOrDefault(selected.Contains); UpdateGuides(); }
    public void SetActive(OverlayCanvasSlot? slot) { if (slot is not null) SelectedSlot = slot; }
    [RelayCommand] private void ToggleNeonBorder() { var before = Capture(); FixedNeonBorder = !FixedNeonBorder; Commit(before); }
    public void BeginDrag() => _dragBefore ??= Capture(); public void CompleteDrag() { if (_dragBefore is { } before) { _dragBefore = null; Commit(before); } }
    public void BeginNumericEdit() => _numericBefore ??= Capture(); public void CompleteNumericEdit() { if (_numericBefore is { } before) { _numericBefore = null; Commit(before); } }
    public void Move(OverlayCanvasSlot slot, double x, double y) { var group = Selected(slot).ToArray(); var dx = Snap(x, OverlayCanvasLayout.MaxX) - slot.X; var dy = Snap(y, OverlayCanvasLayout.MaxY) - slot.Y; dx = Math.Clamp(dx, group.Max(s => -s.X), group.Min(s => OverlayCanvasLayout.MaxX - s.Width - s.X)); dy = Math.Clamp(dy, group.Max(s => -s.Y), group.Min(s => OverlayCanvasLayout.MaxY - s.Height - s.Y)); foreach (var s in group) { s.X += dx; s.Y += dy; } UpdateGuides(); }
    public void Resize(OverlayCanvasSlot slot, double width, double height) { slot.Width = Snap(Math.Clamp(width, OverlayCanvasLayout.MinWidth, OverlayCanvasLayout.MaxWidth), OverlayCanvasLayout.MaxWidth); slot.Height = Snap(Math.Clamp(height, OverlayCanvasLayout.MinHeight, OverlayCanvasLayout.MaxHeight), OverlayCanvasLayout.MaxHeight); }
    public void Nudge(OverlayCanvasSlot slot, double dx, double dy) { var before = Capture(); Move(slot, slot.X + dx, slot.Y + dy); Commit(before); }
    public void FitToViewport(double width, double height) => Zoom = Math.Clamp(Math.Min(width / OverlayCanvasLayout.MaxX, height / OverlayCanvasLayout.MaxY), .1, 1);
    [RelayCommand] private void AlignLeft() => Align(s => s.X, (s, v) => s.X = v); [RelayCommand] private void AlignTop() => Align(s => s.Y, (s, v) => s.Y = v);
    private void Align(Func<OverlayCanvasSlot, double> get, Action<OverlayCanvasSlot, double> set) { var slots = Slots.Where(s => s.IsSelected).ToArray(); if (slots.Length < 2) return; var before = Capture(); var value = get(slots[0]); foreach (var slot in slots) set(slot, value); Commit(before); }
    [RelayCommand] private void Undo() { if (_undo.TryPop(out var draft)) { _redo.Push(Capture()); Restore(draft); } } [RelayCommand] private void Redo() { if (_redo.TryPop(out var draft)) { _undo.Push(Capture()); Restore(draft); } }
    [RelayCommand] private void AddSelected() { if (SelectedMetric is null || Slots.Count >= OverlayCanvasLayout.MaxElements || Slots.Any(s => s.MetricId == SelectedMetric.Id)) return; var before = Capture(); Add(new() { MetricId = SelectedMetric.Id, X = 24 + (Slots.Count % 8) * 196, Y = 24 + (Slots.Count / 8) * 112 }); Select(Slots.Last(), false); RebuildPreview(); Commit(before); }
    [RelayCommand] private void RemoveSelected() { var slots = Slots.Where(s => s.IsSelected).ToArray(); if (slots.Length == 0 && SelectedSlot is { } selected) slots = [selected]; if (slots.Length == 0) return; var before = Capture(); foreach (var slot in slots) Slots.Remove(slot); SelectedSlot = Slots.FirstOrDefault(); RebuildPreview(); Commit(before); }
    [RelayCommand] private void Apply() { var layout = Capture().Layout; if (!layout.IsValid()) { Error = "Each card must fit within the 1920 × 1080 canvas."; return; } _settings.OverlayCanvas = layout; _settings.OverlayMetrics = layout.Elements.Select(e => e.MetricId).ToList(); _save(); Error = ""; Applied?.Invoke(); }
    [RelayCommand] private void ResetToAuto() { _settings.OverlayCanvas = null; _save(); Error = ""; Applied?.Invoke(); }
    [RelayCommand] private void Compact() => Preset(180, 90, 16, false); [RelayCommand] private void Analysis() => Preset(260, 130, 24, false); [RelayCommand] private void Synthwave() => Preset(220, 110, 32, true);
    private void Preset(double width, double height, double gap, bool neon) { var before = Capture(); FixedNeonBorder = neon; var cols = Math.Max(1, (int)((OverlayCanvasLayout.MaxX - 24) / (width + gap))); for (var i = 0; i < Slots.Count; i++) { Slots[i].X = 0; Slots[i].Y = 0; Resize(Slots[i], width, height); Slots[i].X = Snap(24 + (i % cols) * (width + gap), OverlayCanvasLayout.MaxX); Slots[i].Y = Snap(24 + (i / cols) * (height + gap), OverlayCanvasLayout.MaxY); } Commit(before); }
    public string ExportDraft() { var layout = Capture().Layout; if (!layout.IsValid()) throw new InvalidDataException("Fix canvas geometry before export."); var package = new PortableOverlayCanvas { FixedNeonBorder = layout.FixedNeonBorder, Elements = layout.Elements.Select(e => { var d = _store.Definitions.FirstOrDefault(x => x.Id == e.MetricId) ?? throw new InvalidDataException("A canvas metric is unavailable."); return new PortableOverlayCanvasElement { Group = d.Group, Unit = d.Unit, X = e.X, Y = e.Y, Width = e.Width, Height = e.Height }; }).ToList() }; package.Validate(); return JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true }); }
    public void ImportFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Canvas import exceeds 1 MB.");
            ImportDraft(File.ReadAllText(path));
        }
        catch (Exception ex) { ImportMappings.Clear(); Error = ex.Message; }
    }
    public void ImportDraft(string json) { try { if (json.Length > 1024 * 1024) throw new InvalidDataException("Canvas import exceeds 1 MB."); var package = JsonSerializer.Deserialize<PortableOverlayCanvas>(json) ?? throw new InvalidDataException("Missing canvas."); package.Validate(); ImportMappings.Clear(); foreach (var e in package.Elements) ImportMappings.Add(new() { Element = e, Choices = AvailableMetrics.Where(d => d.Group == e.Group && d.Unit == e.Unit).ToArray() }); _importBorder = package.FixedNeonBorder; Error = "Map every imported card, then import into the draft."; } catch (Exception ex) { ImportMappings.Clear(); Error = ex.Message; } }
    [RelayCommand] private void ImportMapped() { try { if (ImportMappings.Count == 0 || ImportMappings.Any(m => m.Selected is null || !m.Choices.Contains(m.Selected) || !_store.TryGet(m.Selected.Id, out _)) || ImportMappings.Select(m => m.Selected!.Id).Distinct().Count() != ImportMappings.Count) throw new InvalidDataException("Choose distinct compatible metrics for every imported card."); var elements = ImportMappings.Select(m => m.Element.ToElement(m.Selected!.Id)).ToArray(); if (!(new OverlayCanvasLayout { Elements = elements.ToList(), FixedNeonBorder = _importBorder }).IsValid()) throw new InvalidDataException("Imported canvas geometry is invalid."); var before = Capture(); Slots.Clear(); foreach (var element in elements) Add(element); FixedNeonBorder = _importBorder; ImportMappings.Clear(); SelectedSlot = Slots.FirstOrDefault(); RebuildPreview(); Commit(before); Error = ""; } catch (Exception ex) { Error = ex.Message; } }
    private IEnumerable<OverlayCanvasSlot> Selected(OverlayCanvasSlot slot) => Slots.Where(s => s.IsSelected || ReferenceEquals(s, slot));
    private void UpdateGuides()
    {
        if (SelectedSlot is not { } slot) { ShowVerticalGuide = ShowHorizontalGuide = false; return; }
        var selected = Selected(slot).ToHashSet();
        var other = Slots.Where(candidate => !selected.Contains(candidate));
        var horizontal = other.SelectMany(candidate => new[] { candidate.X, candidate.X + candidate.Width / 2, candidate.X + candidate.Width })
            .FirstOrDefault(value => Math.Abs(value - slot.X) <= 4 || Math.Abs(value - (slot.X + slot.Width / 2)) <= 4 || Math.Abs(value - (slot.X + slot.Width)) <= 4, double.NaN);
        var vertical = other.SelectMany(candidate => new[] { candidate.Y, candidate.Y + candidate.Height / 2, candidate.Y + candidate.Height })
            .FirstOrDefault(value => Math.Abs(value - slot.Y) <= 4 || Math.Abs(value - (slot.Y + slot.Height / 2)) <= 4 || Math.Abs(value - (slot.Y + slot.Height)) <= 4, double.NaN);
        ShowVerticalGuide = !double.IsNaN(horizontal);
        ShowHorizontalGuide = !double.IsNaN(vertical);
        if (!double.IsNaN(horizontal)) GuideX = horizontal;
        if (!double.IsNaN(vertical)) GuideY = vertical;
    }
    private Draft Capture() => new(new() { FixedNeonBorder = FixedNeonBorder, Elements = Slots.Select(s => s.ToElement()).ToList() }, Slots.Where(s => s.IsSelected).Select(s => s.MetricId).ToArray(), SelectedSlot?.MetricId);
    private void Commit(Draft before) { if (Same(before.Layout, Capture().Layout)) return; _undo.Push(before); if (_undo.Count > HistoryLimit) { var retained = _undo.Take(HistoryLimit).Reverse().ToArray(); _undo.Clear(); foreach (var draft in retained) _undo.Push(draft); } _redo.Clear(); RaiseHistory(); }
    private void Restore(Draft draft) { FixedNeonBorder = draft.Layout.FixedNeonBorder; Slots.Clear(); foreach (var e in draft.Layout.Elements) Add(e); foreach (var slot in Slots) slot.IsSelected = draft.SelectedIds.Contains(slot.MetricId); SelectedSlot = Slots.FirstOrDefault(slot => slot.MetricId == draft.ActiveId) ?? Slots.FirstOrDefault(slot => slot.IsSelected) ?? Slots.FirstOrDefault(); RebuildPreview(); RaiseHistory(); }
    private void RaiseHistory() { OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); } private static bool Same(OverlayCanvasLayout a, OverlayCanvasLayout b) => a.FixedNeonBorder == b.FixedNeonBorder && a.Elements.Select(e => (e.MetricId,e.X,e.Y,e.Width,e.Height)).SequenceEqual(b.Elements.Select(e => (e.MetricId,e.X,e.Y,e.Width,e.Height)));
    private void Add(OverlayCanvasElement e) => Slots.Add(new(e)); private void RebuildPreview() { PreviewTiles.Clear(); var t = ThresholdIndex.Build(_settings); foreach (var s in Slots) if (_store.Definitions.FirstOrDefault(d => d.Id == s.MetricId) is { } d) { var tile = new MetricTileViewModel(d, _store[d.Id], _settings); tile.Refresh(t); s.Tile = tile; s.Label = d.DisplayName; PreviewTiles.Add(tile); } }
    private static OverlayCanvasLayout NewLayout(IEnumerable<string> ids) => new() { Elements = ids.Take(OverlayCanvasLayout.MaxElements).Select((id,i) => new OverlayCanvasElement { MetricId=id, X=24+(i%8)*196, Y=24+(i/8)*112 }).ToList() }; private static double Snap(double value, double maximum) => Math.Clamp(Math.Round(value / 8) * 8, 0, maximum);
}
public sealed partial class OverlayCanvasImportMapping : ObservableObject { public required PortableOverlayCanvasElement Element { get; init; } public required IReadOnlyList<MetricDefinition> Choices { get; init; } [ObservableProperty] private MetricDefinition? _selected; }
public sealed partial class OverlayCanvasSlot : ObservableObject
{ public OverlayCanvasSlot(OverlayCanvasElement e) { MetricId=e.MetricId; Label=e.MetricId; Width=e.Width; Height=e.Height; X=e.X; Y=e.Y; } public string MetricId { get; } [ObservableProperty] private string _label=""; [ObservableProperty] private bool _isSelected; private double _x,_y,_width,_height; public double X { get=>_x; set=>SetProperty(ref _x, Bound(value,_x,0,OverlayCanvasLayout.MaxX-Width)); } public double Y { get=>_y; set=>SetProperty(ref _y, Bound(value,_y,0,OverlayCanvasLayout.MaxY-Height)); } public double Width { get=>_width; set=>SetProperty(ref _width, Bound(value,_width,OverlayCanvasLayout.MinWidth,Math.Min(OverlayCanvasLayout.MaxWidth,OverlayCanvasLayout.MaxX-X))); } public double Height { get=>_height; set=>SetProperty(ref _height, Bound(value,_height,OverlayCanvasLayout.MinHeight,Math.Min(OverlayCanvasLayout.MaxHeight,OverlayCanvasLayout.MaxY-Y))); } private MetricTileViewModel? _tile; public MetricTileViewModel? Tile { get=>_tile; set=>SetProperty(ref _tile,value); } public OverlayCanvasElement ToElement()=>new(){MetricId=MetricId,X=X,Y=Y,Width=Width,Height=Height}; private static double Bound(double value,double fallback,double min,double max)=>double.IsFinite(value)?Math.Clamp(value,min,max):fallback; }
