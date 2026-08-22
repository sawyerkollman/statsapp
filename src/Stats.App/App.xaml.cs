using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
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

    /// <summary>Loaded settings; read-only access for views that need raw prefs (e.g. context menus).</summary>
    public AppSettings? Settings => _settings;
    private ISensorReader? _reader;
    private MetricStore? _store;
    private SensorPoller? _poller;
    private DashboardWindow? _dashboard;
    private DashboardViewModel? _dashboardVm;
    private OverlayWindow? _overlay;
    private OverlayViewModel? _overlayVm;
    private TaskbarIcon? _tray;
    private string? _trayCpuTempId;
    private string? _trayGpuTempId;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stats");
        _settingsService = new SettingsService(settingsDir);
        _settings = _settingsService.Load();

        IReadOnlyList<MetricDefinition> definitions;
        try
        {
            _reader = new LhmSensorReader();
            definitions = _reader.Discover();
        }
        catch (Exception)
        {
            try { _reader?.Dispose(); } catch (Exception) { /* failed reader; fall through to fallback */ }
            _reader = new PerfCounterSensorReader();
            definitions = _reader.Discover();
        }
        // LHM does not throw when its kernel driver fails to load — CPU temp/power sensors are simply absent.
        bool cpuSensorsMissing = !definitions.Any(d => d.Group == MetricGroup.Cpu && d.Unit == "°C");
        bool degraded = _reader.IsDegraded || cpuSensorsMissing;

        if (!_settings.DefaultsApplied)
        {
            // Seed only a genuinely fresh settings file; a pre-flag file with selections keeps them.
            if (_settings.DashboardMetrics.Count == 0 && _settings.OverlayMetrics.Count == 0)
            {
                _settings.DashboardMetrics = DefaultSelector.DashboardDefaults(definitions);
                _settings.OverlayMetrics = DefaultSelector.OverlayDefaults(definitions);
            }
            _settings.DefaultsApplied = true;
        }

        _store = new MetricStore(definitions);
        _poller = new SensorPoller(_reader)
        {
            Interval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds),
        };

        _dashboardVm = new DashboardViewModel(_store, _settings, SaveSettings)
        {
            IsDegraded = degraded,
        };

        _overlayVm = new OverlayViewModel(_store, _settings);
        _overlay = new OverlayWindow
        {
            DataContext = _overlayVm,
            Opacity = _settings.OverlayOpacity,
        };
        if (_settings.OverlayLeft is double ol) _overlay.Left = ClampToVirtualScreenX(ol, 100);
        if (_settings.OverlayTop is double ot) _overlay.Top = ClampToVirtualScreenY(ot, 40);
        _overlay.LocationChanged += (_, _) =>
        {
            if (double.IsNaN(_overlay.Left) || double.IsNaN(_overlay.Top)) return;
            _settings.OverlayLeft = _overlay.Left;
            _settings.OverlayTop = _overlay.Top;
        };
        _dashboardVm.OverlayMetricsChanged += () => _overlayVm.Rebuild();
        _dashboardVm.OverlayToggleRequested += ToggleOverlay;

        var cpuTemps = definitions.Where(d => d.Group == MetricGroup.Cpu && d.Unit == "°C").ToList();
        _trayCpuTempId = (cpuTemps.FirstOrDefault(d => d.DisplayName.Contains("tctl", StringComparison.OrdinalIgnoreCase))
                       ?? cpuTemps.FirstOrDefault(d => d.DisplayName.Contains("package", StringComparison.OrdinalIgnoreCase))
                       ?? cpuTemps.FirstOrDefault())?.Id;
        _trayGpuTempId = definitions.FirstOrDefault(d =>
            d.Group == MetricGroup.Gpu && d.Unit == "°C")?.Id;
        SetupTray();

        _dashboard = new DashboardWindow { DataContext = _dashboardVm };
        RestoreWindowBounds();

        _poller.SnapshotAvailable += snapshot => Dispatcher.BeginInvoke(() =>
        {
            _store.Apply(snapshot);
            _dashboardVm.RefreshAll();
            _overlayVm?.RefreshAll();
            UpdateTrayTooltip();
        });

        _dashboard.AllowClose = false; // close button hides to tray; exit via tray menu
        _dashboard.LocationChanged += (_, _) => SaveWindowBounds();
        _dashboard.SizeChanged += (_, _) => SaveWindowBounds();
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
        if (_settings is null) return;
        try { _settingsService?.Save(_settings); }
        catch (Exception) { /* disk unavailable — keep running; next save retries */ }
    }

    private void RestoreWindowBounds()
    {
        if (_dashboard is null || _settings is null) return;
        if (_settings.WindowWidth is double width) _dashboard.Width = width;
        if (_settings.WindowHeight is double height) _dashboard.Height = height;
        if (_settings.WindowLeft is double left) _dashboard.Left = ClampToVirtualScreenX(left, 200);
        if (_settings.WindowTop is double top) _dashboard.Top = ClampToVirtualScreenY(top, 100);
    }

    private void SaveWindowBounds()
    {
        if (_dashboard is null || _settings is null) return;
        if (_dashboard.WindowState != WindowState.Normal) return;
        if (double.IsNaN(_dashboard.Left) || double.IsNaN(_dashboard.Top)) return;
        _settings.WindowLeft = _dashboard.Left;
        _settings.WindowTop = _dashboard.Top;
        _settings.WindowWidth = _dashboard.Width;
        _settings.WindowHeight = _dashboard.Height;
    }

    /// <summary>Keeps at least <paramref name="minVisible"/> px of the window inside the combined monitor area.</summary>
    private static double ClampToVirtualScreenX(double left, double minVisible) =>
        Math.Max(SystemParameters.VirtualScreenLeft,
                 Math.Min(left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - minVisible));

    private static double ClampToVirtualScreenY(double top, double minVisible) =>
        Math.Max(SystemParameters.VirtualScreenTop,
                 Math.Min(top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - minVisible));

    private void SetupTray()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "Stats",
            Icon = System.Drawing.SystemIcons.Application,
        };
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open dashboard" };
        open.Click += (_, _) => ShowDashboard();
        var overlay = new MenuItem { Header = "Toggle overlay" };
        overlay.Click += (_, _) => ToggleOverlay();
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApp();

        menu.Items.Add(open);
        menu.Items.Add(overlay);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        _tray.ContextMenu = menu;
        _tray.TrayLeftMouseUp += (_, _) => ShowDashboard();
        // TaskbarIcon only materializes the shell icon on Loaded; created in code it must be forced.
        try { _tray.ForceCreate(enablesEfficiencyMode: false); }
        catch (Exception) { /* shell not ready (logon / explorer restart); dashboard still usable */ }
    }

    private void UpdateTrayTooltip()
    {
        if (_tray is null || _store is null) return;
        string Part(string? id, string label) =>
            id is not null && _store.TryGet(id, out var h) && h.Current is float v
                ? $"{label} {v:F0}°C" : "";
        var text = $"Stats  {Part(_trayCpuTempId, "CPU")}  {Part(_trayGpuTempId, "GPU")}".Trim();
        _tray.ToolTipText = text.Length > 0 ? text : "Stats";
    }

    private void ShowDashboard()
    {
        if (_dashboard is null) return;
        _dashboard.Show();
        _dashboard.WindowState = WindowState.Normal;
        _dashboard.Activate();
    }

    private void ToggleOverlay()
    {
        if (_overlay is null) return;
        if (_overlay.IsVisible) _overlay.Hide();
        else _overlay.Show();
    }

    private void ExitApp()
    {
        if (_dashboard is not null) _dashboard.AllowClose = true;
        _tray?.Dispose();
        SaveWindowBounds();
        Shutdown();
    }
}
