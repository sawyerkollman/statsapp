using System.IO;
using System.Windows;
using System.Windows.Threading;
using Stats.App.Views;
using Stats.Core.Alerts;
using Stats.Core.Recording;
using Stats.Core.Sensors;
using Stats.Core.ViewModels;
using Stats.Core.Frames;

namespace Stats.App;

public partial class App
{
    private SessionRecorder? _recorder;
    private SessionViewModel? _sessionVm;
    private SessionWindow? _sessions;
    private ComparisonViewModel? _comparisonVm;
    private ComparisonWindow? _comparison;
    private AlertHistoryStore? _alertHistory;
    private DispatcherTimer? _alertSaveTimer;
    private Task? _alertSave;
    private bool _alertsDirty;
    private bool _alertTransitionPending;
    private string _recordingDirectory = "";
    private Action<RecordedFrame>? _recordingFrameSink;

    private void InitializeMonitoring(string directory)
    {
        _recordingDirectory = Path.Combine(directory, "recordings");
        _recorder = new SessionRecorder(_recordingDirectory); // constructor does not create files
        InitializeBetaLab(Path.Combine(directory, "beta-lab"));
        _alertHistory = new AlertHistoryStore(directory);
        _alertLog = new AlertLogViewModel();
        _alertLog.Load(_alertHistory.Load());
        _alertLog.HistoryStatus = _alertHistory.Error ?? "";
        _alertLog.Changed += () => _alertsDirty = true;
        _alertLog.PersistenceRequested += () => { _alertTransitionPending = true; SaveAlertHistory(); };
        _alertLog.OpenContextRequested += series => ShowCapturedComparison(
            $"Alert context - {series.Definition.DisplayName}", new[] { series });
        _alertSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _alertSaveTimer.Tick += (_, _) => { SaveAlertHistory(); _labVm?.SaveTimeline(); };
        _alertSaveTimer.Start();
    }

    private void RefreshMonitoring(SensorSnapshot snapshot)
    {
        _recorder?.Record(snapshot);
        _sessionVm?.RefreshRecorderState();
        RefreshBetaLab(snapshot);
        _alertLog?.UpdateOngoing(snapshot);
        if (_comparison is { IsVisible: true }) _comparisonVm?.RefreshLive();
    }

    private void SaveAlertHistory()
    {
        if (!_alertsDirty || _alertHistory is null || _alertLog is null || _alertSave is { IsCompleted: false }) return;
        _alertsDirty = false;
        _alertTransitionPending = false;
        var records = _alertLog.ExportRecords(); // owned arrays, never the observable UI collection
        _alertSave = Task.Run(() => _alertHistory.Save(records));
        _ = ReportAlertSaveAsync(_alertSave);
    }

    private async Task ReportAlertSaveAsync(Task save)
    {
        await save;
        if (_alertLog is null || _alertHistory is null) return;
        _alertLog.HistoryStatus = _alertHistory.Error ?? "";
        if (_alertHistory.Error is not null) _alertsDirty = true; // retry on the next bounded tick
        else if (_alertTransitionPending) SaveAlertHistory(); // a transition during the write is not delayed
    }

    private void StopMonitoring()
    {
        _alertSaveTimer?.Stop();
        DetachRecordingFrames();
        _recorder?.Dispose(); // drain after the existing poller-stop/fan-restore sequence
        _labVm?.Dispose();
        _alertSave?.GetAwaiter().GetResult(); // writer never awaits the Dispatcher
        if (_alertsDirty && _alertHistory is not null && _alertLog is not null)
            _alertHistory.Save(_alertLog.ExportRecords());
    }

    private void EnsureComparison()
    {
        if (_comparison is not null || _store is null || _settings is null) return;
        _comparisonVm = new ComparisonViewModel(_store, _settings);
        _comparison = new ComparisonWindow { DataContext = _comparisonVm };
        _comparison.IsVisibleChanged += (_, e) => { if (e.NewValue is true) _comparisonVm.RefreshLive(); };
    }

    private void ShowComparison()
    {
        EnsureComparison();
        _comparisonVm?.SwitchToLive();
        ShowMonitoringWindow(_comparison);
    }

    private void ShowCapturedComparison(string title, IReadOnlyList<MetricSeries> series)
    {
        EnsureComparison();
        _comparisonVm?.SetCaptured(title, series);
        ShowMonitoringWindow(_comparison);
    }

    private void EnsureSessionViewModel()
    {
        if (_recorder is null || _settings is null) return;
        if (_sessionVm is null)
        {
            _sessionVm = new SessionViewModel(_recorder, () =>
            {
                var byId = _definitions.ToDictionary(definition => definition.Id);
                return _settings.DashboardMetrics.Concat(_settings.OverlayMetrics).Distinct()
                    .Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
            }, recordingDirectory: _recordingDirectory,
                rawCaptureAvailable: () => _frameReader is { IsActive: true, IsAvailable: true });
            _sessionVm.OpenComparisonRequested += series => ShowCapturedComparison(
                $"Recorded comparison - {_sessionVm.StartedUtc?.ToLocalTime():g}", series);
            _sessionVm.RecordingStarted += () =>
            {
                DetachRecordingFrames();
                if (_sessionVm.RecordingIncludesRawFrames && _frameReader is not null)
                {
                    _recordingFrameSink = _recorder.CreateFrameSink();
                    _frameReader.FrameRecorded += _recordingFrameSink;
                }
                if (_startingAutoRecording) return;
                _autoRecordingOwned = false;
                _labVm?.AcknowledgeRecording(false, false);
            };
            _sessionVm.RecordingStopping += DetachRecordingFrames;
            _sessionVm.ReportReady += () =>
            {
                _postGameRecordingPath = _sessionVm.HasReport ? _sessionVm.FilePath : null;
                _labVm?.SetPostGameReport(_sessionVm.HasReport ? _sessionVm.ReportText : null);
            };
        }
    }

    private void DetachRecordingFrames()
    {
        if (_recordingFrameSink is not null && _frameReader is not null)
            _frameReader.FrameRecorded -= _recordingFrameSink;
        _recordingFrameSink = null;
    }

    private void ShowSessions()
    {
        EnsureSessionViewModel();
        if (_sessionVm is null) return;
        if (_sessionVm.CanStart && string.IsNullOrWhiteSpace(_sessionVm.RecordingGameName))
            _sessionVm.RecordingGameName = _recordingGameName ?? "";
        _sessions ??= new SessionWindow { DataContext = _sessionVm };
        _sessionVm!.RefreshRecorderState();
        ShowMonitoringWindow(_sessions);
    }

    private static void ShowMonitoringWindow(Window? window)
    {
        if (window is null) return;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
