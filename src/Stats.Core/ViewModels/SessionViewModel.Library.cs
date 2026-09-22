using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Recording;

namespace Stats.Core.ViewModels;

public sealed record SessionLibraryGroup(string GameName, IReadOnlyList<SessionLibraryEntry> Entries);

public sealed partial class SessionViewModel
{
    private IReadOnlyList<SessionLibraryEntry> _libraryEntries = [];
    public ObservableCollection<SessionLibraryEntry> LibraryEntries { get; } = new();
    public ObservableCollection<SessionLibraryGroup> LibraryGroups { get; } = new();
    [ObservableProperty] private string _libraryFilter = "";
    [ObservableProperty] private SessionLibraryEntry? _selectedLibraryEntry;
    [ObservableProperty] private string? _pinnedBaselinePath;
    public bool HasPinnedBaseline => !string.IsNullOrWhiteSpace(PinnedBaselinePath);
    public string BaselineLabel => HasPinnedBaseline ? "Baseline: " + Path.GetFileName(PinnedBaselinePath) : "No baseline pinned";

    partial void OnLibraryFilterChanged(string value) => RebuildLibraryView();
    partial void OnPinnedBaselinePathChanged(string? value) { OnPropertyChanged(nameof(HasPinnedBaseline)); OnPropertyChanged(nameof(BaselineLabel)); }

    private void RefreshLibraryCore()
    {
        if (string.IsNullOrWhiteSpace(RecordingDirectory)) { SetLibrary([]); return; }
        var library = new SessionLibrary(RecordingDirectory);
        SetLibrary(library.Read(), library.PinnedPath, library.Error);
    }

    private async Task RefreshLibraryCoreAsync()
    {
        var directory = RecordingDirectory;
        if (string.IsNullOrWhiteSpace(directory)) { SetLibrary([]); return; }
        try
        {
            var result = await Task.Run(() => { var library = new SessionLibrary(directory); return (Entries: library.Read(), library.PinnedPath, library.Error); });
            SetLibrary(result.Entries, result.PinnedPath, result.Error);
        }
        catch (Exception ex) { Error = "Library unavailable: " + ex.Message; }
    }

    private void SetLibrary(IReadOnlyList<SessionLibraryEntry> entries, string? pinnedPath = null, string? libraryError = null)
    {
        _libraryEntries = entries; PinnedBaselinePath = pinnedPath;
        RecordingLibrary.Clear(); foreach (var entry in entries.Where(entry => entry.CanOpen)) RecordingLibrary.Add(entry.Path);
        RebuildLibraryView();
        if (!string.IsNullOrWhiteSpace(libraryError)) Error = libraryError;
    }

    private void RebuildLibraryView()
    {
        var filter = LibraryFilter.Trim();
        var visible = _libraryEntries.Where(entry => filter.Length == 0 || entry.GameName.Contains(filter, StringComparison.OrdinalIgnoreCase) || entry.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        LibraryEntries.Clear(); foreach (var entry in visible) LibraryEntries.Add(entry);
        LibraryGroups.Clear();
        foreach (var group in visible.GroupBy(entry => entry.GameName, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            LibraryGroups.Add(new(group.Key, group.ToArray()));
    }

    [RelayCommand] private async Task PinBaselineAsync(SessionLibraryEntry? entry)
    {
        if (!CanOpenOrExport || entry is not { CanOpen: true } || string.IsNullOrWhiteSpace(RecordingDirectory)) return;
        IsBusy = true;
        try { var directory = RecordingDirectory; await Task.Run(() => new SessionLibrary(directory).SetPinned(entry.Path)); await RefreshLibraryCoreAsync(); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private async Task UnpinBaselineAsync()
    {
        if (!CanOpenOrExport || string.IsNullOrWhiteSpace(RecordingDirectory)) return;
        IsBusy = true;
        try { var directory = RecordingDirectory; await Task.Run(() => new SessionLibrary(directory).SetPinned(null)); await RefreshLibraryCoreAsync(); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private async Task CompareWithBaselineAsync(SessionLibraryEntry? entry)
    {
        if (!CanOpenOrExport) return;
        var baseline = PinnedBaselinePath;
        if (string.IsNullOrWhiteSpace(baseline) || string.IsNullOrWhiteSpace(RecordingDirectory)) { Error = "Pinned baseline is missing; choose another recording or unpin it."; return; }
        try { baseline = SessionLibrary.NormalizePinnedPath(RecordingDirectory, baseline); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { Error = ex.Message; return; }
        IsBusy = true;
        bool exists;
        try { exists = await Task.Run(() => File.Exists(baseline)); }
        finally { IsBusy = false; }
        if (!exists) { Error = "Pinned baseline is missing; choose another recording or unpin it."; return; }
        var candidate = entry?.CanOpen == true ? entry.Path : _loadedPath;
        if (string.IsNullOrWhiteSpace(candidate)) { Error = "Open or select a recording to compare with the pinned baseline."; return; }
        if (string.Equals(candidate, baseline, StringComparison.OrdinalIgnoreCase)) { Error = "Choose a recording other than the pinned baseline."; return; }
        if (_loadedPath != candidate && !await OpenAsync(candidate)) return;
        await OpenComparisonAsync(baseline);
    }
}
