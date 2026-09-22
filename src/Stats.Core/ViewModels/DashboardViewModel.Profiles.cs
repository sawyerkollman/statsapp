using CommunityToolkit.Mvvm.Input;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class DashboardViewModel
{
    private sealed record LayoutUndo(LayoutProfile Layout, bool WasModified);
    private LayoutUndo? _layoutUndo;
    private bool _applyingLayoutProfile;
    private bool _restoringLayoutUndo;

    public IReadOnlyList<string> LayoutProfileNames => _settings.LayoutProfiles.Select(p => p.Name).ToList();
    public string? ActiveLayoutProfileName => _settings.ActiveLayoutProfile;
    public bool IsLayoutModified => _settings.ActiveLayoutProfile is not null && _settings.LayoutProfileModified;
    public bool IsLayoutLocked => _settings.IsLayoutLocked;
    public bool CanUndoLayoutEdit => _layoutUndo is not null;

    public bool TryGetLayoutProfile(string name, out LayoutProfile? profile)
    {
        profile = _settings.LayoutProfiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
        return profile is not null;
    }

    public LayoutProfile SnapshotLayoutProfile(string name) => LayoutProfile.Capture(_settings, name);

    public bool ApplyLayoutProfile(string name)
    {
        if (!TryGetLayoutProfile(name, out var profile)) return false;
        _settings.SceneSectionLabels.Clear();
        return ApplyLayout(profile!, profile!.Name);
    }

    public void ApplySceneLayout(LayoutProfile layout) => ApplyLayout(layout, null);
    public void SyncOverlaySelection()
    {
        ClearLayoutUndo();
        _suppressPickerEvents = true;
        try { foreach (var item in PickerItems) item.IsOnOverlay = _settings.OverlayMetrics.Contains(item.Definition.Id); }
        finally { _suppressPickerEvents = false; }
        MarkLayoutModified();
    }

    public void RefreshRestoredLayout() => ApplyLayout(LayoutProfile.Capture(_settings, ""), _settings.ActiveLayoutProfile, _settings.LayoutProfileModified);

    private bool ApplyLayout(LayoutProfile profile, string? activeName, bool modified = false)
    {
        _applyingLayoutProfile = true;
        try
        {
            profile!.Restore(_settings);
            _settings.ActiveLayoutProfile = activeName;
            _settings.LayoutProfileModified = modified;
            OnPropertyChanged(nameof(IsLayoutLocked));
            LayoutMode = _settings.DashboardLayoutMode;
            OnPropertyChanged(nameof(IsAutoLayout));
            OnPropertyChanged(nameof(IsGridLayout));
            _suppressPickerEvents = true;
            try
            {
                foreach (var item in PickerItems)
                {
                    item.IsChecked = _settings.DashboardMetrics.Contains(item.Definition.Id);
                    item.IsOnOverlay = _settings.OverlayMetrics.Contains(item.Definition.Id);
                }
            }
            finally { _suppressPickerEvents = false; }
            SettingsPanel?.SyncShowCoreMatrix();
            _tilesSeededBelowUnmeasuredBlock.Clear();
            ClearLayoutUndo();
            RebuildSections();
            DashboardMetricsChanged?.Invoke();
            OverlayMetricsChanged?.Invoke();
            RaiseLayoutProfileState();
            if (!_seedPackSavedLastRebuild) _saveSettings();
            return true;
        }
        finally { _applyingLayoutProfile = false; }
    }

    [RelayCommand] private void LoadLayoutProfile(string name) => ApplyLayoutProfile(name);

    [RelayCommand] private void SaveLayoutProfile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        var profile = LayoutProfile.Capture(_settings, name);
        var index = _settings.LayoutProfiles.FindIndex(p => p.Name == name);
        if (index >= 0) _settings.LayoutProfiles[index] = profile;
        else _settings.LayoutProfiles.Add(profile);
        _settings.ActiveLayoutProfile = name;
        _settings.LayoutProfileModified = false;
        ClearLayoutUndo();
        RaiseLayoutProfileState();
        _saveSettings();
    }

    [RelayCommand(CanExecute = nameof(IsLayoutModified))] private void SaveActiveLayoutProfile() => SaveLayoutProfile(_settings.ActiveLayoutProfile);
    [RelayCommand(CanExecute = nameof(IsLayoutModified))] private void RevertLayoutProfile() => ApplyLayoutProfile(_settings.ActiveLayoutProfile!);

    [RelayCommand] private void DeleteLayoutProfile(string name)
    {
        if (_settings.LayoutProfiles.RemoveAll(p => p.Name == name) == 0) return;
        if (_settings.ActiveLayoutProfile == name)
        {
            _settings.ActiveLayoutProfile = null;
            _settings.LayoutProfileModified = false;
        }
        if (_settings.GameModeGamingLayoutProfile == name) _settings.GameModeGamingLayoutProfile = null;
        if (_settings.GameModeDesktopLayoutProfile == name) _settings.GameModeDesktopLayoutProfile = null;
        ClearLayoutUndo();
        RaiseLayoutProfileState();
        _saveSettings();
    }

    public void RenameLayoutProfile(string oldName, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        newName = newName.Trim();
        if (newName == oldName || _settings.LayoutProfiles.Any(p => p.Name == newName)) return;
        var profile = _settings.LayoutProfiles.FirstOrDefault(p => p.Name == oldName);
        if (profile is null) return;
        profile.Name = newName;
        if (_settings.ActiveLayoutProfile == oldName) _settings.ActiveLayoutProfile = newName;
        if (_settings.GameModeGamingLayoutProfile == oldName) _settings.GameModeGamingLayoutProfile = newName;
        if (_settings.GameModeDesktopLayoutProfile == oldName) _settings.GameModeDesktopLayoutProfile = newName;
        ClearLayoutUndo();
        RaiseLayoutProfileState();
        _saveSettings();
    }

    public bool MarkLayoutModified()
    {
        if (_settings.ActiveLayoutProfile is null || _settings.LayoutProfileModified) return false;
        _settings.LayoutProfileModified = true;
        RaiseLayoutProfileState();
        return true;
    }

    public void ApplyGameModeLayout(bool gaming)
    {
        var name = gaming ? _settings.GameModeGamingLayoutProfile : _settings.GameModeDesktopLayoutProfile;
        if (!string.IsNullOrWhiteSpace(name) && (_settings.ActiveLayoutProfile != name || _settings.LayoutProfileModified))
            ApplyLayoutProfile(name);
    }

    /// <summary>Called by the composition root after Settings changes the core-matrix setting outside this VM.</summary>
    public void ExternalCoreMatrixChanged()
    {
        ClearLayoutUndo();
        if (MarkLayoutModified()) _saveSettings();
        RebuildSections();
    }

    public void SetLayoutLocked(bool value)
    {
        if (_settings.IsLayoutLocked == value) return;
        _settings.IsLayoutLocked = value;
        ClearLayoutUndo();
        OnPropertyChanged(nameof(IsLayoutLocked));
        _saveSettings();
    }

    [RelayCommand] private void ToggleLayoutLock() => SetLayoutLocked(!_settings.IsLayoutLocked);

    internal bool BeginLayoutEdit(bool undoable)
    {
        if (_applyingLayoutProfile || IsLayoutLocked) return false;
        if (undoable) _layoutUndo = new(LayoutProfile.Capture(_settings, ""), _settings.LayoutProfileModified);
        OnPropertyChanged(nameof(CanUndoLayoutEdit));
        UndoLayoutEditCommand.NotifyCanExecuteChanged();
        return true;
    }

    internal void ClearLayoutUndo()
    {
        if (_layoutUndo is null) return;
        _layoutUndo = null;
        OnPropertyChanged(nameof(CanUndoLayoutEdit));
        UndoLayoutEditCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndoLayoutEdit))]
    private void UndoLayoutEdit()
    {
        if (_layoutUndo is null) return;
        var undo = _layoutUndo;
        _layoutUndo = null;
        undo.Layout.Restore(_settings);
        _settings.LayoutProfileModified = undo.WasModified;
        _restoringLayoutUndo = true;
        try { LayoutMode = _settings.DashboardLayoutMode; }
        finally { _restoringLayoutUndo = false; }
        OnPropertyChanged(nameof(IsAutoLayout));
        OnPropertyChanged(nameof(IsGridLayout));
        RebuildSections();
        DashboardMetricsChanged?.Invoke();
        OverlayMetricsChanged?.Invoke();
        RaiseLayoutProfileState();
        OnPropertyChanged(nameof(CanUndoLayoutEdit));
        UndoLayoutEditCommand.NotifyCanExecuteChanged();
        _saveSettings();
    }

    private void RaiseLayoutProfileState()
    {
        OnPropertyChanged(nameof(ActiveLayoutProfileName));
        OnPropertyChanged(nameof(IsLayoutModified));
        OnPropertyChanged(nameof(LayoutProfileNames));
        SaveActiveLayoutProfileCommand.NotifyCanExecuteChanged();
        RevertLayoutProfileCommand.NotifyCanExecuteChanged();
    }
}
