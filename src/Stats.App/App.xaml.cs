using System.IO;
using System.Windows;
using Stats.App.Views;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.App;

public partial class App : Application
{
    private SettingsService? _settingsService;
    private AppSettings? _settings;
    private ISensorReader? _reader;
    private MetricStore? _store;
    private SensorPoller? _poller;
    private DashboardWindow? _dashboard;
    private DashboardViewModel? _dashboardVm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stats");
        _settingsService = new SettingsService(settingsDir);
        _settings = _settingsService.Load();

        try
        {
            _reader = new LhmSensorReader();
        }
        catch (Exception)
        {
            _reader = new PerfCounterSensorReader();
        }

        var definitions = _reader.Discover();
        if (_settings.DashboardMetrics.Count == 0)
            _settings.DashboardMetrics = DefaultSelector.DashboardDefaults(definitions);
        if (_settings.OverlayMetrics.Count == 0)
            _settings.OverlayMetrics = DefaultSelector.OverlayDefaults(definitions);

        _store = new MetricStore(definitions);
        _poller = new SensorPoller(_reader)
        {
            Interval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds),
        };

        _dashboardVm = new DashboardViewModel(_store, _settings, SaveSettings)
        {
            IsDegraded = _reader.IsDegraded,
        };

        _dashboard = new DashboardWindow { DataContext = _dashboardVm };
        RestoreWindowBounds();

        _poller.SnapshotAvailable += snapshot => Dispatcher.BeginInvoke(() =>
        {
            _store.Apply(snapshot);
            _dashboardVm.RefreshAll();
        });

        _dashboard.AllowClose = true; // becomes false when tray lands (Task 14)
        _dashboard.Closing += (_, _) => SaveWindowBounds();
        _dashboard.Closed += (_, _) => Shutdown();
        _dashboard.Show();
        _poller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _poller?.Dispose();
        _reader?.Dispose();
        SaveSettings();
        base.OnExit(e);
    }

    private void SaveSettings()
    {
        if (_settings is not null) _settingsService?.Save(_settings);
    }

    private void RestoreWindowBounds()
    {
        if (_dashboard is null || _settings is null) return;
        if (_settings.WindowLeft is double left) _dashboard.Left = left;
        if (_settings.WindowTop is double top) _dashboard.Top = top;
        if (_settings.WindowWidth is double width) _dashboard.Width = width;
        if (_settings.WindowHeight is double height) _dashboard.Height = height;
    }

    private void SaveWindowBounds()
    {
        if (_dashboard is null || _settings is null) return;
        _settings.WindowLeft = _dashboard.Left;
        _settings.WindowTop = _dashboard.Top;
        _settings.WindowWidth = _dashboard.Width;
        _settings.WindowHeight = _dashboard.Height;
    }
}
