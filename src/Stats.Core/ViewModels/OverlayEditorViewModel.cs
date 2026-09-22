using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

/// <summary>Draft-only overlay editor. It never starts capture or owns a polling loop.</summary>
public sealed partial class OverlayEditorViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MetricStore _store;
    private readonly Action _save;
    public OverlayEditorViewModel(AppSettings settings, MetricStore store, Action save)
    {
        _settings = settings; _store = store; _save = save;
        AvailableMetrics = store.Definitions.ToArray();
        var current = OverlayCanvasLayout.Normalize(settings.OverlayCanvas) ?? NewLayout(settings.OverlayMetrics);
        FixedNeonBorder = current.FixedNeonBorder;
        foreach (var element in current.Elements) Add(element);
        SelectedSlot = Slots.FirstOrDefault();
        RebuildPreview();
    }

    public IReadOnlyList<MetricDefinition> AvailableMetrics { get; }
    public ObservableCollection<OverlayCanvasSlot> Slots { get; } = new();
    public ObservableCollection<MetricTileViewModel> PreviewTiles { get; } = new();
    public event Action? Applied;
    [ObservableProperty] private MetricDefinition? _selectedMetric;
    [ObservableProperty] private OverlayCanvasSlot? _selectedSlot;
    [ObservableProperty] private bool _fixedNeonBorder;
    [ObservableProperty] private string _error = "";

    public void Refresh() { var thresholds = ThresholdIndex.Build(_settings); foreach (var tile in PreviewTiles) tile.Refresh(thresholds); }
    public void Move(OverlayCanvasSlot slot, double x, double y) { slot.X = Snap(x, OverlayCanvasLayout.MaxX); slot.Y = Snap(y, OverlayCanvasLayout.MaxY); }
    public void Resize(OverlayCanvasSlot slot, double width, double height)
    {
        slot.Width = Snap(Math.Clamp(width, OverlayCanvasLayout.MinWidth, OverlayCanvasLayout.MaxWidth), OverlayCanvasLayout.MaxWidth);
        slot.Height = Snap(Math.Clamp(height, OverlayCanvasLayout.MinHeight, OverlayCanvasLayout.MaxHeight), OverlayCanvasLayout.MaxHeight);
    }
    public void Nudge(OverlayCanvasSlot slot, double dx, double dy) => Move(slot, slot.X + dx, slot.Y + dy);

    [RelayCommand] private void AddSelected()
    {
        if (SelectedMetric is null || Slots.Count >= OverlayCanvasLayout.MaxElements || Slots.Any(s => s.MetricId == SelectedMetric.Id)) return;
        Add(new OverlayCanvasElement { MetricId = SelectedMetric.Id, X = 24 + (Slots.Count % 8) * 196, Y = 24 + (Slots.Count / 8) * 112 }); SelectedSlot = Slots.Last(); RebuildPreview();
    }
    [RelayCommand] private void RemoveSelected()
    {
        if (SelectedSlot is null) return;
        Slots.Remove(SelectedSlot); SelectedSlot = Slots.FirstOrDefault(); RebuildPreview();
    }
    [RelayCommand] private void Apply()
    {
        var layout = new OverlayCanvasLayout { FixedNeonBorder = FixedNeonBorder, Elements = Slots.Select(s => s.ToElement()).ToList() };
        if (!layout.IsValid()) { Error = "Each card must fit within the 1920 × 1080 canvas."; return; }
        _settings.OverlayCanvas = layout.Clone();
        _settings.OverlayMetrics = layout.Elements.Select(e => e.MetricId).ToList();
        _save(); Error = ""; Applied?.Invoke();
    }
    [RelayCommand] private void ResetToAuto()
    {
        _settings.OverlayCanvas = null; _save(); Error = ""; Applied?.Invoke();
    }
    [RelayCommand] private void Compact() { FixedNeonBorder = false; ApplyPreset(180, 90, 16); }
    [RelayCommand] private void Analysis() { FixedNeonBorder = false; ApplyPreset(260, 130, 24); }
    [RelayCommand] private void Synthwave() { ApplyPreset(220, 110, 32); FixedNeonBorder = true; }

    private void ApplyPreset(double width, double height, double gap)
    {
        var columns = Math.Max(1, (int)((OverlayCanvasLayout.MaxX - 24) / (width + gap)));
        for (var i = 0; i < Slots.Count; i++) { Move(Slots[i], 0, 0); Resize(Slots[i], width, height); Move(Slots[i], 24 + (i % columns) * (width + gap), 24 + (i / columns) * (height + gap)); }
    }
    private void Add(OverlayCanvasElement element) => Slots.Add(new OverlayCanvasSlot(element));
    private void RebuildPreview()
    {
        PreviewTiles.Clear(); var thresholds = ThresholdIndex.Build(_settings);
        foreach (var slot in Slots)
            if (_store.Definitions.FirstOrDefault(d => d.Id == slot.MetricId) is { } def)
            { var tile = new MetricTileViewModel(def, _store[def.Id], _settings); tile.Refresh(thresholds); slot.Tile = tile; slot.Label = def.DisplayName; PreviewTiles.Add(tile); }
    }
    private static OverlayCanvasLayout NewLayout(IEnumerable<string> ids) => new()
    {
        Elements = ids.Take(OverlayCanvasLayout.MaxElements).Select((id, i) =>
            new OverlayCanvasElement { MetricId = id, X = 24 + (i % 8) * 196, Y = 24 + (i / 8) * 112 }).ToList(),
    };
    private static double Snap(double value, double maximum) => Math.Clamp(Math.Round(value / 8) * 8, 0, maximum);
}

public sealed partial class OverlayCanvasSlot : ObservableObject
{
    public OverlayCanvasSlot(OverlayCanvasElement element) { MetricId = element.MetricId; Label = element.MetricId; Width = element.Width; Height = element.Height; X = element.X; Y = element.Y; }
    public string MetricId { get; }
    [ObservableProperty] private string _label = "";
    private double _x, _y, _width, _height;
    public double X { get => _x; set => SetProperty(ref _x, Bound(value, _x, 0, OverlayCanvasLayout.MaxX - Width)); }
    public double Y { get => _y; set => SetProperty(ref _y, Bound(value, _y, 0, OverlayCanvasLayout.MaxY - Height)); }
    public double Width { get => _width; set => SetProperty(ref _width, Bound(value, _width, OverlayCanvasLayout.MinWidth, Math.Min(OverlayCanvasLayout.MaxWidth, OverlayCanvasLayout.MaxX - X))); }
    public double Height { get => _height; set => SetProperty(ref _height, Bound(value, _height, OverlayCanvasLayout.MinHeight, Math.Min(OverlayCanvasLayout.MaxHeight, OverlayCanvasLayout.MaxY - Y))); }
    private MetricTileViewModel? _tile;
    public MetricTileViewModel? Tile { get => _tile; set => SetProperty(ref _tile, value); }
    public OverlayCanvasElement ToElement() => new() { MetricId = MetricId, X = X, Y = Y, Width = Width, Height = Height };
    private static double Bound(double value, double fallback, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
