using System.IO;
using System.Windows;
using System.Windows.Threading;
using Stats.App.Views;
using Stats.Core.Alerts;
using Stats.Core.Recording;
using Stats.Core.Sensors;
using Stats.Core.ViewModels;

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

    private void InitializeMonitoring(string directory)
    {
        _recordingDirectory = Path.Combine(directory, "recordings");
        _recorder = new SessionRecorder(_recordingDirectory); // constructor does not create files
        _alertHistory = new AlertHistoryStore(directory);
        _alertLog = new AlertLogViewModel();
        _alertLog.Load(_alertHistory.Load());
        _alertLog.HistoryStatus = _alertHistory.Error ?? "";
        _alertLog.Changed += () => _alertsDirty = true;
        _alertLog.PersistenceRequested += () => { _alertTransitionPending = true; SaveAlertHistory(); };
        _alertLog.OpenContextRequested += series => ShowCapturedComparison(
            $"Alert context - {series.Definition.DisplayName}", new[] { series });
        _alertSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _alertSaveTimer.Tick += (_, _) => SaveAlertHistory();
        _alertSaveTimer.Start();
    }

    private void RefreshMonitoring(SensorSnapshot snapshot)
    {
        _recorder?.Record(snapshot);
        _sessionVm?.RefreshRecorderState();
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
        _recorder?.Dispose(); // drain after the existing poller-stop/fan-restore sequence
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

    private void ShowSessions()
    {
        if (_recorder is null || _settings is null) return;
        if (_sessions is null)
        {
            _sessionVm = new SessionViewModel(_recorder, () =>
            {
                var byId = _definitions.ToDictionary(definition => definition.Id);
                return _settings.DashboardMetrics.Concat(_settings.OverlayMetrics).Distinct()
                    .Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
            }, recordingDirectory: _recordingDirectory);
            _sessionVm.OpenComparisonRequested += series => ShowCapturedComparison(
                $"Recorded comparison - {_sessionVm.StartedUtc?.ToLocalTime():g}", series);
            _sessions = new SessionWindow { DataContext = _sessionVm };
        }
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
