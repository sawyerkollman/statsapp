using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Recording;

namespace Stats.Core.ViewModels;

public sealed partial class SessionSeriesViewModel : ObservableObject
{
    private readonly Action<SessionSeriesViewModel>? _changed;
    public SessionSeriesViewModel(MetricSeries series, SessionMetricSummary? summary, Action<SessionSeriesViewModel>? changed = null)
    {
        Series = series; Metric = series.Definition.DisplayName; Unit = series.Definition.Unit;
        Min = summary?.Min; Average = summary?.Average; Max = summary?.Max;
        _changed = changed;
    }
    public MetricSeries Series { get; }
    public string Metric { get; }
    public string Unit { get; }
    public float? Min { get; }
    public float? Average { get; }
    public float? Max { get; }
    public string MinText => ValueFormatter.Format(Series.Definition, Min);
    public string AverageText => ValueFormatter.Format(Series.Definition, Average);
    public string MaxText => ValueFormatter.Format(Series.Definition, Max);
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _replayValue = "—";
    partial void OnIsSelectedChanged(bool value) => _changed?.Invoke(this);
}

public sealed partial class SessionViewModel : ObservableObject
{
    private const int MaxComparisonSeries = 4;
    private readonly SessionRecorder _recorder;
    private readonly Func<IReadOnlyList<MetricDefinition>> _selectedDefinitions;
    private readonly Func<DateTime> _clock;
    private bool _syncingSelection;
    private bool _recordingPending;
    private SessionData? _loaded;
    private SessionData? _comparison;
    private int _loadGeneration;
    private string? _loadedPath;
    private IReadOnlyList<SessionBookmark> _comparisonNotes = [];
    private bool _settingCursor;

    public SessionViewModel(SessionRecorder recorder, Func<IReadOnlyList<MetricDefinition>> selectedDefinitions, Func<DateTime>? clock = null, string? recordingDirectory = null)
    { _recorder = recorder; _selectedDefinitions = selectedDefinitions; _clock = clock ?? (() => DateTime.UtcNow); RecordingDirectory = recordingDirectory; }

    public ObservableCollection<SessionSeriesViewModel> Rows { get; } = new();
    public ObservableCollection<SessionBookmark> Bookmarks { get; } = new();
    public ObservableCollection<string> RecordingLibrary { get; } = new();
    public event Action<IReadOnlyList<MetricSeries>>? OpenComparisonRequested;
    public event Action? RecordingStarted;
    [ObservableProperty] private string _status = "Not recording";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string? _recordingDirectory;
    [ObservableProperty] private DateTime? _startedUtc;
    [ObservableProperty] private DateTime? _endedUtc;
    [ObservableProperty] private string _chartLimitText = "Charts show newest 3600 samples; export includes the full recording.";
    [ObservableProperty] private int _replayIndex;
    [ObservableProperty] private int _replayMaximum;
    [ObservableProperty] private string _replayText = "Open a recording to replay it.";
    [ObservableProperty] private IReadOnlyList<float> _replayValues = [];
    [ObservableProperty] private IReadOnlyList<DateTime> _replayTimesUtc = [];
    [ObservableProperty] private string _replayMetric = "";
    [ObservableProperty] private string _summaryScope = "Full recording summary";
    [ObservableProperty] private string _bookmarkNote = "";
    [ObservableProperty] private string _analysisText = "Detective reports measured association only; correlation does not prove a bottleneck.";
    [ObservableProperty] private string _abText = "Open a second recording to compare aligned elapsed-time bins. No interpolation or causal claims.";
    [ObservableProperty] private SessionSeriesViewModel? _selectedReplayMetric;
    [ObservableProperty] private DateTime? _replayCursorUtc;
    public DateTime? ReplayStartUtc => ReplayTimesUtc.Count == 0 ? null : ReplayTimesUtc[0];
    public DateTime? ReplayEndUtc => ReplayTimesUtc.Count == 0 ? null : ReplayTimesUtc[^1];
    [ObservableProperty] private long _replayWindowStart;
    [ObservableProperty] private int _replayWindowSize = SessionFile.ChartSampleLimit;
    public bool CanStart => !IsBusy && !_recordingPending;
    public bool CanStop => !IsBusy && _recordingPending;
    public bool CanOpenOrExport => !IsBusy && !_recordingPending;
    public bool HasNextWindow => _loaded is not null && ReplayWindowStart + ReplayMaximum + 1 < _loaded.SampleCount;
    public string DatedSummary => StartedUtc is null ? "" : $"Started {StartedUtc.Value.ToLocalTime():g}" + (EndedUtc is { } end ? $" · ended {end.ToLocalTime():g}" : "");
    partial void OnIsBusyChanged(bool value) => RaiseFlags();
    partial void OnIsRecordingChanged(bool value) => RaiseFlags();
    partial void OnStartedUtcChanged(DateTime? value) => OnPropertyChanged(nameof(DatedSummary));
    partial void OnEndedUtcChanged(DateTime? value) => OnPropertyChanged(nameof(DatedSummary));

    public void RefreshRecorderState()
    {
        if (!_recordingPending) return;
        IsRecording = _recorder.IsRecording; FilePath = _recorder.FilePath; Error = _recorder.Error ?? "";
        if (!IsRecording && FilePath is not null && Error.Length > 0) Status = "Recording ended with an error";
        RaiseFlags();
    }

    [RelayCommand]
    private void Start()
    {
        if (!CanStart) return;
        var definitions = _selectedDefinitions();
        if (definitions.Count == 0) { Error = "Select at least one dashboard or overlay metric before recording."; return; }
        try
        {
            var started = _clock();
            _recorder.Start(definitions, started);
            ResetReplay();
            _recordingPending = true;
            RaiseFlags();
            IsRecording = _recorder.IsRecording;
            StartedUtc = started; EndedUtc = null; Rows.Clear();
            FilePath = _recorder.FilePath;
            Error = _recorder.Error ?? "";
            Status = IsRecording ? "Recording" : "Recording could not start";
            if (IsRecording) RecordingStarted?.Invoke();
        }
        catch (Exception ex) { Error = ex.Message; Status = "Recording could not start"; }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (!CanStop) return;
        IsBusy = true;
        try
        {
            await _recorder.StopAsync();
            IsRecording = _recorder.IsRecording;
            FilePath = _recorder.FilePath;
            Error = _recorder.Error ?? "";
            Status = string.IsNullOrEmpty(Error) ? "Recording stopped" : "Recording ended with an error";
            EndedUtc = _clock();
            _recordingPending = false;
            var recorderError = Error;
            if (FilePath is not null)
            {
                var path = FilePath;
                var data = await Task.Run(() => SessionFile.Load(path));
                ApplyLoaded(path, data);
            }
            if (!string.IsNullOrEmpty(recorderError)) Error = recorderError;
            RaiseFlags();
        }
        catch (Exception ex) { Error = ex.Message; Status = "Recording could not stop"; }
        finally { IsBusy = false; }
    }

    public bool Open(string path)
    {
        if (!CanOpenOrExport) return false;
        return LoadFile(path);
    }

    public async Task<bool> OpenAsync(string path)
    {
        if (!CanOpenOrExport) return false;
        IsBusy = true;
        try { var data = await Task.Run(() => SessionFile.Load(path)); ApplyLoaded(path, data); return true; }
        catch (Exception ex) { Error = ex.Message; return false; }
        finally { IsBusy = false; }
    }

    public void RefreshLibrary()
    {
        RecordingLibrary.Clear();
        if (string.IsNullOrWhiteSpace(RecordingDirectory) || !Directory.Exists(RecordingDirectory)) return;
        try { foreach (var file in Directory.EnumerateFiles(RecordingDirectory, "*.stats-session.jsonl").Take(200).OrderByDescending(File.GetLastWriteTimeUtc)) RecordingLibrary.Add(file); }
        catch (Exception ex) { Error = "Library unavailable: " + ex.Message; }
    }

    public async Task RefreshLibraryAsync()
    {
        var directory = RecordingDirectory;
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            var files = await Task.Run(() => Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.stats-session.jsonl").Take(200).OrderByDescending(File.GetLastWriteTimeUtc).ToArray() : []);
            RecordingLibrary.Clear(); foreach (var file in files) RecordingLibrary.Add(file);
        }
        catch (Exception ex) { Error = "Library unavailable: " + ex.Message; }
    }

    private bool LoadFile(string path)
    {
        try
        {
            ApplyLoaded(path, SessionFile.Load(path));
            return true;
        }
        catch (Exception ex) { Error = ex.Message; Status = "Could not open recording"; return false; }
    }

    private void ApplyLoaded(string path, SessionData data)
    {
            ResetReplay();
            _loadedPath = path; _loaded = data;
            SummaryScope = "Full recording summary (replay chart is the bounded tail)";
            Rows.Clear();
            foreach (var series in data.Series)
                Rows.Add(new(series, data.Summaries.FirstOrDefault(summary => summary.MetricId == series.Definition.Id), SelectionChanged) { IsSelected = Rows.Count < MaxComparisonSeries });
            FilePath = path; Error = "";
            StartedUtc = data.StartedUtc; EndedUtc = data.EndedUtc;
            Status = data.IsComplete ? $"Loaded {data.SampleCount} samples" : $"Loaded incomplete recording ({data.SampleCount} samples)";
            ReplayMaximum = Math.Max(0, data.Series.FirstOrDefault()?.Values.Length - 1 ?? 0);
            ReplayWindowStart = Math.Max(0, data.SampleCount - ReplayMaximum - 1);
            ReplayIndex = ReplayMaximum;
            LoadAnnotations(path);
            SelectedReplayMetric = Rows.FirstOrDefault();
            RefreshDetective();
    }

    private void ResetReplay()
    {
        _loadedPath = null; _loaded = null; _comparison = null; _comparisonNotes = []; SelectedReplayMetric = null;
        Rows.Clear(); Bookmarks.Clear(); ReplayValues = []; ReplayTimesUtc = []; ReplayCursorUtc = null;
        ReplayMaximum = 0; ReplayIndex = 0; ReplayWindowStart = 0; ReplayText = "Open a recording to replay it.";
        AnalysisText = "Select two or more recorded metrics for association analysis.";
        AbText = "Open a second recording for aligned elapsed-time comparison.";
    }

    public bool OpenComparison(string path)
    {
        if (!CanOpenOrExport) return false;
        try { _comparison = SessionFile.Load(path); _comparisonNotes = SessionAnnotationStore.Load(path).Bookmarks; RefreshAb(); return true; }
        catch (Exception ex) { Error = ex.Message; AbText = "Could not load comparison recording."; return false; }
    }

    public async Task<bool> OpenComparisonAsync(string path)
    {
        if (!CanOpenOrExport || _loadedPath is null) return false;
        IsBusy = true;
        try
        {
            var firstPath = _loadedPath;
            var pair = await Task.Run(() => (SessionFile.LoadWindow(firstPath, 0, SessionFile.ChartSampleLimit), SessionFile.LoadWindow(path, 0, SessionFile.ChartSampleLimit), SessionAnnotationStore.Load(path).Bookmarks));
            ApplyLoaded(firstPath, pair.Item1); ReplayWindowStart = 0; SummaryScope = "First replay window summary";
            _comparison = pair.Item2; _comparisonNotes = pair.Item3; RefreshAb(); return true;
        }
        catch (Exception ex) { Error = ex.Message; return false; }
        finally { IsBusy = false; }
    }

    public async Task<bool> LoadReplayWindowAsync(long firstSample, int maximumSamples)
    {
        if (!CanOpenOrExport || _loaded is null || string.IsNullOrWhiteSpace(_loadedPath)) return false;
        firstSample = Math.Clamp(firstSample, 0, Math.Max(0, _loaded.SampleCount - 1));
        maximumSamples = Math.Clamp(maximumSamples, 1, SessionFile.ChartSampleLimit);
        var path = _loadedPath; var generation = ++_loadGeneration; IsBusy = true;
        try
        {
            var loaded = await Task.Run(() => SessionFile.LoadWindow(path, firstSample, maximumSamples));
            if (generation != _loadGeneration || path != _loadedPath) return false;
            var metricId = SelectedReplayMetric?.Series.Definition.Id;
            ApplyLoaded(path, loaded); ReplayWindowStart = firstSample; ReplayWindowSize = maximumSamples;
            SummaryScope = "Current replay window summary";
            ReplayMaximum = Math.Max(0, _loaded.Series.FirstOrDefault()?.Values.Length - 1 ?? 0); ReplayIndex = 0;
            SelectedReplayMetric = Rows.FirstOrDefault(row => row.Series.Definition.Id == metricId) ?? Rows.FirstOrDefault();
            RefreshReplay(); RefreshDetective(); Status = $"Loaded replay window starting at sample {firstSample}."; return true;
        }
        catch (Exception ex) { if (generation == _loadGeneration) Error = ex.Message; return false; }
        finally { if (generation == _loadGeneration) IsBusy = false; }
    }

    [RelayCommand] private async Task PreviousWindowAsync() { if (ReplayWindowStart > 0) await LoadReplayWindowAsync(Math.Max(0, ReplayWindowStart - ReplayWindowSize), ReplayWindowSize); }
    [RelayCommand] private async Task NextWindowAsync() { if (HasNextWindow) await LoadReplayWindowAsync(ReplayWindowStart + ReplayWindowSize, ReplayWindowSize); }

    partial void OnReplayIndexChanged(int value) => RefreshReplay();
    partial void OnReplayCursorUtcChanged(DateTime? value)
    {
        if (_settingCursor || value is null || ReplayTimesUtc.Count == 0) return;
        var closest = 0;
        for (var i = 1; i < ReplayTimesUtc.Count; i++)
            if (Math.Abs((ReplayTimesUtc[i] - value.Value).Ticks) < Math.Abs((ReplayTimesUtc[closest] - value.Value).Ticks)) closest = i;
        ReplayIndex = closest;
        RefreshReplay();
    }
    [RelayCommand] private void StepReplay(int delta) => ReplayIndex = Math.Clamp(ReplayIndex + delta, 0, ReplayMaximum);
    [RelayCommand] private void PreviousSample() { if (CanOpenOrExport) StepReplay(-1); }
    [RelayCommand] private void NextSample() { if (CanOpenOrExport) StepReplay(1); }
    [RelayCommand] private void AddBookmark(string? note)
    {
        if (!CanOpenOrExport || _loaded is null || _loaded.SampleCount == 0 || string.IsNullOrWhiteSpace(_loadedPath)) return;
        Bookmarks.Insert(0, new(ReplayWindowStart + ReplayIndex, note ?? BookmarkNote)); BookmarkNote = "";
        while (Bookmarks.Count > SessionAnnotations.MaxBookmarks) Bookmarks.RemoveAt(Bookmarks.Count - 1);
        try { SessionAnnotationStore.Save(_loadedPath, Bookmarks); Error = ""; }
        catch (Exception ex) { Error = ex.Message; }
    }
    [RelayCommand] private async Task JumpToBookmarkAsync(SessionBookmark bookmark)
    {
        if (!CanOpenOrExport || _loaded is null || bookmark.SampleIndex < 0 || bookmark.SampleIndex >= _loaded.SampleCount) return;
        if (bookmark.SampleIndex < ReplayWindowStart || bookmark.SampleIndex > ReplayWindowStart + ReplayMaximum)
            await LoadReplayWindowAsync(Math.Max(0, bookmark.SampleIndex - ReplayWindowSize / 2), ReplayWindowSize);
        ReplayIndex = Math.Clamp((int)(bookmark.SampleIndex - ReplayWindowStart), 0, ReplayMaximum);
    }

    private void LoadAnnotations(string path)
    {
        Bookmarks.Clear();
        try { foreach (var bookmark in SessionAnnotationStore.Load(path).Bookmarks.Where(b => b.SampleIndex < (_loaded?.SampleCount ?? 0))) Bookmarks.Add(bookmark); }
        catch (Exception ex) { Error = ex.Message; }
    }
    partial void OnSelectedReplayMetricChanged(SessionSeriesViewModel? value)
    {
        ReplayValues = value?.Series.Values.Select(v => v ?? float.NaN).ToArray() ?? [];
        ReplayTimesUtc = value?.Series.TimesUtc ?? [];
        ReplayMetric = value?.Unit ?? "";
        OnPropertyChanged(nameof(ReplayStartUtc)); OnPropertyChanged(nameof(ReplayEndUtc));
        RefreshReplay();
    }
    private void RefreshReplay()
    {
        if (ReplayTimesUtc.Count == 0) { ReplayCursorUtc = null; ReplayText = "No replay samples in this window."; return; }
        var index = Math.Clamp(ReplayIndex, 0, ReplayTimesUtc.Count - 1);
        _settingCursor = true;
        try { ReplayCursorUtc = ReplayTimesUtc[index]; }
        finally { _settingCursor = false; }
        foreach (var row in Rows) row.ReplayValue = ValueFormatter.Format(row.Series.Definition, index < row.Series.Values.Length ? row.Series.Values[index] : null);
        ReplayText = $"{ReplayCursorUtc:u} · sample {ReplayWindowStart + index + 1} / {_loaded?.SampleCount}";
    }
    private void RefreshAb()
    {
        if (_loaded is null || _comparison is null) return;
        var rows = SessionAnalysis.Compare(_loaded, _comparison);
        AbText = rows.Count == 0 ? "No compatible metrics." : "First bounded windows, aligned elapsed-time bin means; no interpolation. " + string.Join("; ", rows.Take(4).Select(r => $"{r.MetricId}: Δ {r.Delta?.ToString("F2") ?? "—"} {r.Unit}, relative {r.RelativeDelta?.ToString("P1") ?? "n/a"}, {r.DurationSeconds:F1}s, {r.FirstCount}/{r.SecondCount} samples, {r.PairCount} paired {r.BinSeconds:F1}s bins, coverage {r.Coverage:P0} (A {r.FirstCoverage:P0}, B {r.SecondCoverage:P0})"));
        AbText += "\nA experiment notes: " + Notes(Bookmarks, _loaded.SampleCount) + "\nB experiment notes: " + Notes(_comparisonNotes, _comparison.SampleCount);
    }
    private static string Notes(IEnumerable<SessionBookmark> notes, long count) =>
        string.Join("; ", notes.Where(n => n.SampleIndex >= 0 && n.SampleIndex < count).Select(n => $"Sample {n.SampleIndex + 1}: {n.Note}").DefaultIfEmpty("None recorded."));
    private void RefreshDetective()
    {
        if (_loaded is null || _loaded.Series.Count < 2) { AnalysisText = "Insufficient metrics for association analysis."; return; }
        var correlation = SessionAnalysis.Correlate(_loaded.Series[0], _loaded.Series[1]);
        AnalysisText = correlation.Pearson is double r
            ? $"Measured {correlation.FirstMetricId}/{correlation.SecondMetricId} correlation r={r:F2}, {correlation.PairCount} pairs, {correlation.Coverage:P0} coverage. Association is not bottleneck attribution."
            : $"Insufficient varying paired samples ({correlation.PairCount}, {correlation.Coverage:P0} coverage); no bottleneck attribution.";
    }

    public bool Export(string destination)
    {
        if (!CanOpenOrExport || string.IsNullOrWhiteSpace(FilePath)) return false;
        try { SessionFile.ExportCsv(FilePath, destination); Error = ""; Status = "CSV exported"; return true; }
        catch (Exception ex) { Error = ex.Message; Status = "CSV export failed"; return false; }
    }

    public async Task<bool> ExportAsync(string destination)
    {
        if (!CanOpenOrExport || string.IsNullOrWhiteSpace(_loadedPath)) return false;
        IsBusy = true;
        try { await Task.Run(() => SessionFile.ExportCsv(_loadedPath, destination)); Error = ""; Status = "CSV exported"; return true; }
        catch (Exception ex) { Error = ex.Message; return false; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Compare()
    {
        var selected = Rows.Where(row => row.IsSelected).Take(MaxComparisonSeries).Select(row => row.Series).ToList();
        if (selected.Count == 0) { Error = "Select at least one metric to compare."; return; }
        OpenComparisonRequested?.Invoke(selected);
    }

    private void SelectionChanged(SessionSeriesViewModel row)
    {
        if (_syncingSelection || !row.IsSelected || Rows.Count(x => x.IsSelected) <= MaxComparisonSeries) return;
        _syncingSelection = true;
        try { row.IsSelected = false; }
        finally { _syncingSelection = false; }
    }
    private void RaiseFlags() { OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanStop)); OnPropertyChanged(nameof(CanOpenOrExport)); }
}
