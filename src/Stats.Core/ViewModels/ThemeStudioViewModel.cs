using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class ThemeStudioViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _save;
    public event Action? Applied;
    public ThemeStudioViewModel(AppSettings settings, Action save)
    {
        _settings = settings; _save = save;
        foreach (var design in settings.ThemeDesigns) Designs.Add(design);
        Set(new("My theme", settings.ThemePreset, settings.ThemeAccent, settings.ThemeSecondary,
            settings.ThemeGradient, settings.ReactiveDecorations));
    }
    public IReadOnlyList<string> Presets => ThemePresets.Names;
    public ObservableCollection<ThemeDesign> Designs { get; } = new();
    [ObservableProperty] private string _name = "My theme";
    [ObservableProperty] private string _preset = ThemePresets.Default;
    [ObservableProperty] private string _accent = "";
    [ObservableProperty] private string _secondary = "";
    [ObservableProperty] private bool _gradient = true;
    [ObservableProperty] private bool _reactive;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private ThemeDesign? _selectedDesign;
    public bool HasDecoration => Preset is "Synthwave" or "Outrun" or "Midnight" or "CRT Terminal" or "Arctic Glass" or "Reactor" or "Deep Space";
    partial void OnPresetChanged(string value) => OnPropertyChanged(nameof(HasDecoration));
    partial void OnSelectedDesignChanged(ThemeDesign? value) { if (value is not null) Set(value); }
    public ThemeDesign Current() => new ThemeDesign(Name, Preset,
        string.IsNullOrWhiteSpace(Accent) ? null : Accent.Trim(),
        string.IsNullOrWhiteSpace(Secondary) ? null : Secondary.Trim(), Gradient, Reactive).Validate();
    public void Set(ThemeDesign design)
    {
        design = design.Validate();
        Name = design.Name; Preset = design.Preset; Accent = design.Accent ?? "";
        Secondary = design.Secondary ?? ""; Gradient = design.Gradient; Reactive = design.Reactive; Error = "";
    }
    [RelayCommand] private void Apply()
    {
        try
        {
            var design = Current();
            _settings.ThemePreset = design.Preset; _settings.ThemeAccent = design.Accent;
            _settings.ThemeSecondary = design.Secondary; _settings.ThemeGradient = design.Gradient;
            _settings.ReactiveDecorations = design.Reactive;
            _save(); Applied?.Invoke(); Error = "";
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    [RelayCommand] private void SaveDesign()
    {
        try
        {
            var design = Current();
            var index = _settings.ThemeDesigns.FindIndex(d => d.Name == design.Name);
            if (index < 0 && Designs.Count >= 50) throw new InvalidDataException("Save up to 50 named themes.");
            if (index < 0) { _settings.ThemeDesigns.Add(design); Designs.Add(design); }
            else { _settings.ThemeDesigns[index] = design; Designs[index] = design; }
            _save(); Error = "";
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}
