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

    public SessionViewModel(SessionRecorder recorder, Func<IReadOnlyList<MetricDefinition>> selectedDefinitions, Func<DateTime>? clock = null, string? recordingDirectory = null)
    { _recorder = recorder; _selectedDefinitions = selectedDefinitions; _clock = clock ?? (() => DateTime.UtcNow); RecordingDirectory = recordingDirectory; }

    public ObservableCollection<SessionSeriesViewModel> Rows { get; } = new();
    public event Action<IReadOnlyList<MetricSeries>>? OpenComparisonRequested;
    [ObservableProperty] private string _status = "Not recording";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string? _recordingDirectory;
    [ObservableProperty] private DateTime? _startedUtc;
    [ObservableProperty] private DateTime? _endedUtc;
    [ObservableProperty] private string _chartLimitText = "Charts show newest 3600 samples; export includes the full recording.";
    public bool CanStart => !IsBusy && !_recordingPending;
    public bool CanStop => !IsBusy && _recordingPending;
    public bool CanOpenOrExport => !IsBusy && !_recordingPending;
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
            _recordingPending = true;
            RaiseFlags();
            IsRecording = _recorder.IsRecording;
            StartedUtc = started; EndedUtc = null; Rows.Clear();
            FilePath = _recorder.FilePath;
            Error = _recorder.Error ?? "";
            Status = IsRecording ? "Recording" : "Recording could not start";
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
            if (FilePath is not null) LoadFile(FilePath);
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

    private bool LoadFile(string path)
    {
        try
        {
            var data = SessionFile.Load(path);
            Rows.Clear();
            foreach (var series in data.Series)
                Rows.Add(new(series, data.Summaries.FirstOrDefault(summary => summary.MetricId == series.Definition.Id), SelectionChanged) { IsSelected = Rows.Count < MaxComparisonSeries });
            FilePath = path; Error = "";
            StartedUtc = data.StartedUtc; EndedUtc = data.EndedUtc;
            Status = data.IsComplete ? $"Loaded {data.SampleCount} samples" : $"Loaded incomplete recording ({data.SampleCount} samples)";
            return true;
        }
        catch (Exception ex) { Error = ex.Message; Status = "Could not open recording"; return false; }
    }

    public bool Export(string destination)
    {
        if (!CanOpenOrExport || string.IsNullOrWhiteSpace(FilePath)) return false;
        try { SessionFile.ExportCsv(FilePath, destination); Error = ""; Status = "CSV exported"; return true; }
        catch (Exception ex) { Error = ex.Message; Status = "CSV export failed"; return false; }
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
