using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Resources;
using H.NotifyIcon;
using Stats.App.Helpers;
using Stats.App.Tray;
using Stats.App.Views;
using Stats.Core.Fans;
using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.Updates;
using Stats.Core.ViewModels;

namespace Stats.App;

public partial class App : Application
{
    private SettingsService? _settingsService;
    private AppSettings? _settings;

    /// <summary>Loaded settings; read-only access for views that need raw prefs (e.g. context menus).</summary>
    public AppSettings? Settings => _settings;
    private ISensorReader? _reader;
    private FrameRateReader? _frameReader;
    private MetricStore? _store;
    private SensorPoller? _poller;
    private DashboardWindow? _dashboard;
    private DashboardViewModel? _dashboardVm;
    private OverlayWindow? _overlay;
    private OverlayViewModel? _overlayVm;
    private TaskbarIcon? _tray;
    private string? _trayCpuTempId;
    private string? _trayGpuTempId;
    private SettingsViewModel? _settingsVm;
    private GlobalHotkey? _hotkey;
    private PeaksWindow? _peaks;
    private PeaksViewModel? _peaksVm;
    private TrayIconRenderer? _trayRenderer;
    private System.Drawing.Icon? _appIcon;
    private MetricDefinition? _trayCpuTempDef;
    private FanController? _fanController;
    private GameModeSwitcher? _gameMode;
    private bool _fanRecovered;
    private bool _fanRecoveryPartial;
    private bool _hardwareAtStartup = true;
    private FansWindow? _fans;
    private FansViewModel? _fansVm;
    private System.Threading.Timer? _processScan;
    private volatile string[] _processNames = Array.Empty<string>();
    private volatile bool _fansVisible;
    private IReadOnlyList<MetricDefinition> _definitions = Array.Empty<MetricDefinition>();
    private CompositeSensorReader? _composite;
    private UpdateService? _updateService;
    private CancellationTokenSource? _updateCts;
    /// <summary>Cancelled by ExitApp()/OnExit() so an in-flight install download bails out (and resets the busy
    /// state) instead of racing app shutdown; a fresh instance per "Update now" click.</summary>
    private CancellationTokenSource? _installCts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stats");
        _settingsService = new SettingsService(settingsDir);
        _settings = _settingsService.Load();
        ThemeManager.Apply(_settings.ThemePreset, _settings.ThemeAccent); // before any window is created

        IReadOnlyList<MetricDefinition> definitions;
        try
        {
            _hardwareAtStartup = _settings.ReadMotherboardAndCoolers; // the reader keeps this until the next restart
            (_composite, _frameReader) = BuildReader(() => new LhmSensorReader(_hardwareAtStartup));
            _reader = _composite;
            definitions = _reader.Discover();
        }
        catch (Exception)
        {
            try { _reader?.Dispose(); } catch (Exception) { /* failed reader; fall through to fallback */ }
            (_composite, _frameReader) = BuildReader(() => new PerfCounterSensorReader());
            _reader = _composite;
            definitions = _reader.Discover();
        }
        // LHM does not throw when its kernel driver fails to load — CPU temp/power sensors are simply absent.
        bool cpuSensorsMissing = !definitions.Any(d => d.Group == MetricGroup.Cpu && d.Unit == "°C");
        bool degraded = _reader.IsDegraded || cpuSensorsMissing;
        _definitions = definitions;

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
        if (_frameReader is not null) _frameReader.Window = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
        ApplyFrameTracing();
        _fanController = new FanController(_composite!, _settings, RequestSaveSettings, new FileFanArmedMarker(settingsDir));
        _fanRecovered = _fanController.RecoverFromUncleanShutdown(out _fanRecoveryPartial); // before _poller.Start(): single-threaded here
        _gameMode = new GameModeSwitcher(_fanController, _settings);

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
        _dashboardVm.OverlayMetricsChanged += () => { _overlayVm.Rebuild(); ApplyFrameTracing(); };
        _dashboardVm.OverlayToggleRequested += ToggleOverlay;

        _settingsVm = new SettingsViewModel(_settings, definitions, SaveSettings);
        _dashboardVm.SettingsPanel = _settingsVm;
        _settingsVm.Changed += OnSettingsChanged;
        _settingsVm.OverlayPositionResetRequested += ResetOverlayPosition;

        ClickThrough.Set(_overlay, _settings.OverlayClickThrough);

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += ToggleOverlay;
        ApplyHotkey();

        _store.ResizeAll(HistoryCapacity.Compute(_settings.HistoryWindowMinutes, _settings.PollIntervalSeconds));

        _dashboardVm.OpenPeaksRequested += ShowPeaks;
        _dashboardVm.OpenFansRequested += ShowFans;
        _dashboardVm.DashboardMetricsChanged += () => { _peaksVm?.RebuildRows(); ApplyFrameTracing(); };
        _dashboardVm.InstallUpdateRequested += OnInstallUpdateRequested;
        _updateService = new UpdateService();

        var cpuTemps = definitions.Where(d => d.Group == MetricGroup.Cpu && d.Unit == "°C").ToList();
        _trayCpuTempId = (cpuTemps.FirstOrDefault(d => d.DisplayName.Contains("tctl", StringComparison.OrdinalIgnoreCase))
                       ?? cpuTemps.FirstOrDefault(d => d.DisplayName.Contains("package", StringComparison.OrdinalIgnoreCase))
                       ?? cpuTemps.FirstOrDefault())?.Id;
        _trayGpuTempId = definitions.FirstOrDefault(d =>
            d.Group == MetricGroup.Gpu && d.Unit == "°C")?.Id;
        _trayCpuTempDef = definitions.FirstOrDefault(d => d.Id == _trayCpuTempId);
        _trayRenderer = new TrayIconRenderer();
        SetupTray();

        _dashboard = new DashboardWindow { DataContext = _dashboardVm };
        RestoreWindowBounds();

        var fanController = _fanController;
        var gameMode = _gameMode;
        // poll thread: same thread as LHM reads. A controller fault must never propagate out of the poller's
        // subscriber list and starve the Dispatcher-refresh handler below. GameModeSwitcher runs first so a
        // profile switch it applies (deferSave: true) is picked up by FanController.Tick in the same snapshot.
        _poller.SnapshotAvailable += snapshot =>
        {
            try { gameMode.Tick(snapshot, DateTime.UtcNow); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("[Stats] GameModeSwitcher.Tick failed: " + ex); }
            try { fanController.Tick(snapshot, DateTime.UtcNow); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("[Stats] FanController.Tick failed: " + ex); }
        };
        _poller.SnapshotAvailable += snapshot => Dispatcher.BeginInvoke(() =>
        {
            _store.Apply(snapshot);
            _dashboardVm.RefreshAll();
            _dashboardVm.SetGroupStatus(MetricGroup.Game, FrameStatus());
            _overlayVm?.RefreshAll();
            UpdateTrayTooltip();
            if (_peaks is { IsVisible: true }) _peaksVm?.Refresh();
            if (_fans is { IsVisible: true }) _fansVm?.Refresh();
        });

        _dashboard.AllowClose = false; // close button hides to tray; exit via tray menu
        _dashboard.LocationChanged += (_, _) => SaveWindowBounds();
        _dashboard.SizeChanged += (_, _) => SaveWindowBounds();
        SessionEnding += (_, _) => ExitApp();
        _dashboard.Show();
        _poller.Start();
        StartUpdateChecks();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _installCts?.Cancel();
        _installCts?.Dispose();
        _updateService?.Dispose();
        _hotkey?.Dispose();
        _fansVisible = false;
        _processScan?.Dispose();
        bool stopped = _poller?.Stop() ?? true;   // stop the poll thread first …
        _fanController?.RestoreAll();             // … then hand every fan back to device control, always
        if (stopped) _reader?.Dispose();
        else System.Diagnostics.Trace.WriteLine("[Stats] poll loop did not stop in time; skipping reader dispose to avoid concurrent LHM access");
        SaveSettings();
        _trayRenderer?.Dispose();
        _appIcon?.Dispose();
        base.OnExit(e);
    }

    /// <summary>The UI thread owns every collection in the settings graph, so every save is serialized here, on
    /// this thread, under AppSettings.SyncRoot — which is also the FanController's gate, so the poll thread cannot
    /// be mutating FanChannels mid-serialize. Only the file write happens outside the lock.</summary>
    private void SaveSettings()
    {
        if (_settings is null || _settingsService is null) return;
        try
        {
            string json;
            lock (_settings.SyncRoot) json = SettingsService.Serialize(_settings);
            _settingsService.Save(json);
        }
        catch (Exception ex)
        {
            // Disk unavailable — keep running; the next save retries. Traced so a silently dropped write
            // (a just-created profile, a game-mode switch) is diagnosable instead of showing up as a stale file.
            System.Diagnostics.Trace.WriteLine("[Stats] settings save failed: " + ex.Message);
        }
    }

    /// <summary>Save request from any thread (the fan controller's callback runs on the poll thread). Marshalled
    /// so serialization only ever happens on the UI thread — see <see cref="SaveSettings"/>.</summary>
    private void RequestSaveSettings()
    {
        if (Dispatcher.CheckAccess()) SaveSettings();
        else Dispatcher.BeginInvoke(SaveSettings);
    }

    /// <summary>Background refresh of the running-process names the Fans window checks for competing fan tools.
    /// Process.GetProcesses() walks the whole process table (tens of ms) — far too slow for the Dispatcher.</summary>
    private void RefreshProcessNames()
    {
        if (!_fansVisible) return;
        try { _processNames = ConflictingFanSoftware.RunningProcessNames().ToArray(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("[Stats] process scan failed: " + ex.Message); }
    }

    /// <summary>Primary hardware reader + the PresentMon frame reader, merged. Discover() is the caller's.</summary>
    private static (CompositeSensorReader Reader, FrameRateReader Frames) BuildReader(Func<ISensorReader> primaryFactory)
    {
        var frames = FrameRateReader.CreateDefault();
        return (new CompositeSensorReader(primaryFactory(), frames), frames);
    }

    private void ApplyFrameTracing()
    {
        if (_frameReader is null || _settings is null) return;
        _frameReader.SetActive(FrameRateReader.ShouldBeActive(_settings.DashboardMetrics, _settings.OverlayMetrics) || _settings.GameModeEnabled);
    }

    private string? FrameStatus()
    {
        if (_frameReader is null || !_frameReader.IsActive || _frameReader.IsAvailable) return null;
        var msg = _frameReader.StatusMessage ?? "";
        int cut = msg.IndexOf(". ", StringComparison.Ordinal);
        return cut > 0 ? msg[..(cut + 1)] : msg;
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
            Icon = LoadAppIcon(),
        };
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open dashboard" };
        open.Click += (_, _) => ShowDashboard();
        var overlay = new MenuItem { Header = "Toggle overlay" };
        overlay.Click += (_, _) => ToggleOverlay();
        var peaks = new MenuItem { Header = "Session peaks" };
        peaks.Click += (_, _) => ShowPeaks();
        var fans = new MenuItem { Header = "Fans…" };
        fans.Click += (_, _) => ShowFans();
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => { ShowDashboard(); _dashboardVm?.OpenSettingsCommand.Execute(null); };
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApp();

        menu.Items.Add(open);
        menu.Items.Add(overlay);
        menu.Items.Add(peaks);
        menu.Items.Add(fans);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        _tray.ContextMenu = menu;
        _tray.TrayLeftMouseUp += (_, _) => ShowDashboard();
        // TaskbarIcon only materializes the shell icon on Loaded; created in code it must be forced.
        try { _tray.ForceCreate(enablesEfficiencyMode: false); }
        catch (Exception) { /* shell not ready (logon / explorer restart); dashboard still usable */ }
    }

    private System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            StreamResourceInfo? sri = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (sri?.Stream is Stream s) { _appIcon = new System.Drawing.Icon(s); return _appIcon; }
        }
        catch { /* fall back below */ }
        return System.Drawing.SystemIcons.Application;
    }

    private void UpdateTrayTooltip()
    {
        if (_tray is null || _store is null || _settings is null) return;
        string Part(string? id, string label) =>
            id is not null && _store.TryGet(id, out var h) && h.Current is float v
                ? $"{label} {v:F0}°C" : "";
        var text = $"Stats  {Part(_trayCpuTempId, "CPU")}  {Part(_trayGpuTempId, "GPU")}".Trim();
        _tray.ToolTipText = text.Length > 0 ? text : "Stats";

        if (_trayRenderer is null || _trayCpuTempDef is null) return;
        float? temp = _store.TryGet(_trayCpuTempDef.Id, out var hist) ? hist.Current : null;
        var label = temp is float t ? t.ToString("F0") : "–";
        var severity = ThresholdEvaluator.Evaluate(_trayCpuTempDef, temp, _settings);
        try
        {
            var icon = _trayRenderer.Render(label, severity);
            if (icon is not null) _tray.Icon = icon;
        }
        catch { /* keep previous icon */ }
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

    private void ShowPeaks()
    {
        if (_store is null || _settings is null) return;
        if (_peaks is null)
        {
            _peaksVm = new PeaksViewModel(_store, _settings);
            _peaks = new PeaksWindow { DataContext = _peaksVm };
            if (_settings.PeaksWidth is double w) _peaks.Width = w;
            if (_settings.PeaksHeight is double h) _peaks.Height = h;
            if (_settings.PeaksLeft is double l) _peaks.Left = ClampToVirtualScreenX(l, 200);
            if (_settings.PeaksTop is double t) _peaks.Top = ClampToVirtualScreenY(t, 100);
            _peaks.LocationChanged += (_, _) => SavePeaksBounds();
            _peaks.SizeChanged += (_, _) => SavePeaksBounds();
        }
        _peaksVm!.RebuildRows();
        _peaks.Show();
        _peaks.WindowState = WindowState.Normal;
        _peaks.Activate();
    }

    private void SavePeaksBounds()
    {
        if (_peaks is null || _settings is null) return;
        if (_peaks.WindowState != WindowState.Normal) return;
        if (double.IsNaN(_peaks.Left) || double.IsNaN(_peaks.Top)) return;
        _settings.PeaksLeft = _peaks.Left;
        _settings.PeaksTop = _peaks.Top;
        _settings.PeaksWidth = _peaks.Width;
        _settings.PeaksHeight = _peaks.Height;
    }

    private void ShowFans()
    {
        if (_fanController is null || _settings is null) return;
        if (_fans is null)
        {
            _fansVm = new FansViewModel(_fanController, _definitions, _settings, processNames: () => _processNames,
                saveSettings: SaveSettings, switcher: _gameMode, hardwareEnabledAtStartup: _hardwareAtStartup);
            _fansVm.GameModeChanged += ApplyFrameTracing;
            if (_fanRecovered)
                _fansVm.RecoveryNotice = _fanRecoveryPartial
                    ? "Stats did not shut down cleanly last time — some fans could not be returned to device control; check for other fan software."
                    : "Stats did not shut down cleanly last time — all fans were returned to device control.";
            _fans = new FansWindow { DataContext = _fansVm };
            _fans.IsVisibleChanged += (_, e) => _fansVisible = e.NewValue is true;
            if (_settings.FansWidth is double w) _fans.Width = w;
            if (_settings.FansHeight is double h) _fans.Height = h;
            if (_settings.FansLeft is double l) _fans.Left = ClampToVirtualScreenX(l, 200);
            if (_settings.FansTop is double t) _fans.Top = ClampToVirtualScreenY(t, 100);
            _fans.LocationChanged += (_, _) => SaveFansBounds();
            _fans.SizeChanged += (_, _) => SaveFansBounds();
        }
        _fansVisible = true;
        // Started on first open, not at launch: nothing else needs the process table.
        _processScan ??= new System.Threading.Timer(_ => RefreshProcessNames(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        _fansVm!.Refresh();
        _fans.Show();
        _fans.WindowState = WindowState.Normal;
        _fans.Activate();
    }

    private void SaveFansBounds()
    {
        if (_fans is null || _settings is null) return;
        if (_fans.WindowState != WindowState.Normal) return;
        if (double.IsNaN(_fans.Left) || double.IsNaN(_fans.Top)) return;
        _settings.FansLeft = _fans.Left;
        _settings.FansTop = _fans.Top;
        _settings.FansWidth = _fans.Width;
        _settings.FansHeight = _fans.Height;
    }

    private void OnSettingsChanged(SettingsChange change)
    {
        if (_settings is null) return;
        switch (change)
        {
            case SettingsChange.PollInterval:
                if (_poller is not null) _poller.Interval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
                if (_frameReader is not null) _frameReader.Window = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
                _store?.ResizeAll(HistoryCapacity.Compute(_settings.HistoryWindowMinutes, _settings.PollIntervalSeconds));
                break;
            case SettingsChange.HistoryWindow:
                _store?.ResizeAll(HistoryCapacity.Compute(_settings.HistoryWindowMinutes, _settings.PollIntervalSeconds));
                _dashboardVm?.RefreshAll();
                break;
            case SettingsChange.Thresholds:
                _dashboardVm?.RefreshAll();
                _overlayVm?.RefreshAll();
                break;
            case SettingsChange.Limits:
                _dashboardVm?.RebuildSections(); // limits can flip Auto kind to Gauge
                break;
            case SettingsChange.Overlay:
                if (_overlay is not null)
                {
                    _overlay.Opacity = _settings.OverlayOpacity;
                    ClickThrough.Set(_overlay, _settings.OverlayClickThrough);
                }
                _overlayVm?.ApplyLayout();
                break;
            case SettingsChange.Hotkey:
                ApplyHotkey();
                break;
            case SettingsChange.CoreMatrix:
                _dashboardVm?.RebuildSections();
                break;
            case SettingsChange.Hardware:
                if (_settingsVm is not null) _settingsVm.HardwareStatus = "Restart Stats to apply";
                break;
            case SettingsChange.Updates:
                if (_settings.CheckForUpdatesAutomatically) StartUpdateChecks();
                else { _updateCts?.Cancel(); _updateCts?.Dispose(); _updateCts = null; }
                break;
            case SettingsChange.Theme:
                ThemeManager.Apply(_settings.ThemePreset, _settings.ThemeAccent);
                // ThemeManager.Apply replaces brush entries rather than mutating them, so already-bound
                // SeverityToBrushConverter Foregrounds/Strokes/Fills won't repaint on their own — the Severity
                // value they're bound to hasn't changed. Re-raise it on every live VM so those Bindings re-run.
                _dashboardVm?.RaiseSeverityRefresh();
                _overlayVm?.RaiseSeverityRefresh();
                _peaksVm?.RaiseSeverityRefresh();
                break;
        }
    }

    private void ApplyHotkey()
    {
        if (_hotkey is null || _settings is null || _settingsVm is null) return;
        var parsed = HotkeyParser.Parse(_settings.OverlayHotkey);
        bool ok = _hotkey.Register(parsed);
        if (!ok) _settingsVm.HotkeyStatus = "Hotkey unavailable — in use by another app";
        else if (parsed is null) _settingsVm.HotkeyStatus = _settings.OverlayHotkey.Length == 0 ? "Hotkey disabled" : "Invalid hotkey — disabled";
        else _settingsVm.HotkeyStatus = "";
    }

    private void ResetOverlayPosition()
    {
        if (_overlay is null || _settings is null) return;
        _overlay.Left = SystemParameters.PrimaryScreenWidth / 2 - 150;
        _overlay.Top = 40;
        _settings.OverlayLeft = _overlay.Left;
        _settings.OverlayTop = _overlay.Top;
        if (!_overlay.IsVisible) _overlay.Show();
        SaveSettings();
    }

    private void ExitApp()
    {
        if (_dashboard is not null) _dashboard.AllowClose = true;
        if (_peaks is not null) _peaks.AllowClose = true;
        if (_fans is not null) _fans.AllowClose = true;
        _tray?.Dispose();
        SaveWindowBounds();
        Shutdown();
    }

    // ---- updater ----

    /// <summary>Starts (or, after a live settings toggle, restarts) the background update-check loop: a 15 s
    /// delay off the UI thread, then an initial check, then one every 24 h via PeriodicTimer, until
    /// <see cref="_updateCts"/> is cancelled (app exit or the setting being turned off). Never touches the
    /// SensorPoller or the fan thread. A no-op for a dev build (version 0.0.0.*) or while the loop is already
    /// running or the setting is off.</summary>
    private void StartUpdateChecks()
    {
        if (_settings is null || _updateService is null) return;
        if (!_settings.CheckForUpdatesAutomatically) return;
        if (_updateCts is { IsCancellationRequested: false }) return; // already running

        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
        if (IsDevBuild(current)) return;

        _updateCts = new CancellationTokenSource();
        _ = RunUpdateLoopAsync(current, _updateCts.Token);
    }

    private static bool IsDevBuild(Version v) => v.Major == 0 && v.Minor == 0 && v.Build <= 0;

    /// <summary>Runs entirely off the UI thread (Task.Delay/PeriodicTimer continuations); every step is wrapped
    /// so an updater failure can never take the app down.</summary>
    private async Task RunUpdateLoopAsync(Version current, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            await CheckForUpdateAsync(current, ct).ConfigureAwait(false);

            using var timer = new System.Threading.PeriodicTimer(TimeSpan.FromHours(24));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await CheckForUpdateAsync(current, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* app exiting, or the setting was turned off */ }
        catch (Exception ex) { Trace.WriteLine("[Stats] update check loop failed: " + ex); }
    }

    /// <summary>Threads the loop's own cancellation token through to the HTTP call so app exit (or the setting
    /// being turned off) aborts an in-flight check immediately instead of waiting out its 10 s timeout and then
    /// BeginInvoke-ing onto a Dispatcher that may already be gone.</summary>
    private async Task CheckForUpdateAsync(Version current, CancellationToken ct)
    {
        if (_updateService is null) return;
        try
        {
            var info = await _updateService.CheckAsync(current, ct).ConfigureAwait(false);
            if (info is null) return;
            _ = Dispatcher.BeginInvoke(() => _dashboardVm?.OfferUpdate(info));
        }
        catch (OperationCanceledException) { /* app exiting, or the setting was turned off */ }
        catch (Exception ex)
        {
            // CheckAsync already swallows its own failures and returns null; this is a last-resort net.
            Trace.WriteLine("[Stats] update check failed: " + ex.Message);
        }
    }

    /// <summary>"Update now" was clicked. Downloads the installer, writes + launches the relaunch helper, then
    /// exits via the app's own clean shutdown path (fans released, settings saved) — the installer never has to
    /// kill us. Download and launch are two separate try/catch blocks so a throw *after* the helper is already
    /// running can never leave the app half-exited while still showing "Download failed — retry" (a launched
    /// helper is already waiting to kill/relaunch us — from that point on we must actually go through with
    /// ExitApp(), never report an error over it). ExitApp() itself runs outside any catch.</summary>
    private async void OnInstallUpdateRequested(UpdateInfo info)
    {
        if (_updateService is null || _dashboardVm is null) return;
        _dashboardVm.SetUpdateProgress(0);

        _installCts?.Dispose();
        var cts = _installCts = new CancellationTokenSource();

        string destPath;
        try
        {
            // Both the installer and the helper script must live in a fresh, admin-only directory — %TEMP% is
            // writable by any same-user, non-elevated process, and we run cmd.exe (which re-reads its script
            // line-by-line) at high integrity. See CreateSecureStagingDirectory.
            var stagingDir = await Task.Run(CreateSecureStagingDirectory).ConfigureAwait(true); // sweep + ACL work off the UI thread
            var versionPart = info.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? info.TagName[1..] : info.TagName;
            destPath = Path.Combine(stagingDir, $"Stats-Setup-{versionPart}.exe");
            var progress = new Progress<double>(p => _dashboardVm.SetUpdateProgress(p)); // Progress<T> marshals to the captured (UI) SynchronizationContext
            await _updateService.DownloadAsync(info, destPath, progress, cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested)
                Trace.WriteLine("[Stats] update download cancelled (app exiting)");
            else
                Trace.WriteLine("[Stats] update download failed: " + ex.Message);
            _dashboardVm.SetUpdateError("Download failed — retry");
            return;
        }

        if (cts.IsCancellationRequested)
        {
            // Exiting mid-download (tray Exit, setting toggled off, etc.) raced the download to completion —
            // bail out rather than launching the helper against a shutdown already in progress.
            _dashboardVm.SetUpdateError("Download failed — retry");
            return;
        }

        try
        {
            LaunchUpdateHelper(destPath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[Stats] update helper launch failed: " + ex.Message);
            _dashboardVm.SetUpdateError("Download failed — retry");
            return;
        }

        ExitApp();
    }

    /// <summary>Creates "%SystemRoot%\Temp\Stats-update-{guid}" with an explicit, non-inherited DACL granting
    /// FullControl only to BUILTIN\Administrators and NT AUTHORITY\SYSTEM, owned by Administrators — the
    /// installer and helper script both live here instead of %TEMP% so a non-elevated same-user process cannot
    /// pre-plant or swap either file out from under the elevated cmd.exe that runs the helper (local-EoP fix;
    /// review finding B1). %SystemRoot%\Temp is the base because its default DACL lets only SYSTEM,
    /// Administrators and CREATOR OWNER create anything, so no parent level can be pre-created, junctioned, or
    /// owned by a non-elevated attacker — which is why no existing-directory "repair" branch exists (a re-ACL
    /// path is both unreliable without SeTakeOwnershipPrivilege handling and TOCTOU-exploitable; see re-review).
    /// The SD is applied atomically at creation; a pre-existing leaf is refused explicitly (fail closed).</summary>
    private static string CreateSecureStagingDirectory()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false); // protected: drop inherited rules entirely
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.SetOwner(admins); // default owner would be the creating user's SID → implicit WRITE_DAC for
                                   // that user's non-elevated processes; the elevated token contains the
                                   // Administrators SID, so setting it as owner at creation is permitted.

        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");

        // Best-effort sweep of leftovers from previous updates (each holds a ~50 MB installer). They are
        // admin-only directories we created ourselves; failures (e.g. a helper still running) are ignored.
        try
        {
            foreach (var old in Directory.EnumerateDirectories(baseDir, "Stats-update-*"))
                try { Directory.Delete(old, recursive: true); } catch { /* in use or already gone */ }
        }
        catch { /* enumeration denied — never block an update on cleanup */ }

        var stagingDir = Path.Combine(baseDir, "Stats-update-" + Guid.NewGuid().ToString("N"));
        // Create(DirectorySecurity) is a silent no-op (SD not applied!) on an existing directory, so refuse
        // one explicitly. Unreachable for a fresh GUID under an admin-only base, but keep the invariant honest.
        if (Directory.Exists(stagingDir))
            throw new IOException($"Update staging directory '{stagingDir}' already exists.");
        new DirectoryInfo(stagingDir).Create(security); // SD applied atomically at creation
        return stagingDir;
    }

    /// <summary>Writes {stagingDir}\stats-update.cmd (waits for this process to exit, runs the installer
    /// silently, then relaunches this exe) and starts it hidden and detached. The app must already be on its way
    /// out via ExitApp() by the time the helper's wait loop can observe this PID gone. FileMode.CreateNew: this
    /// directory is fresh per install (see CreateSecureStagingDirectory) — a file already there would mean
    /// something pre-planted it, so fail loudly instead of silently overwriting it.</summary>
    private static void LaunchUpdateHelper(string installerPath)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            throw new InvalidOperationException("Could not resolve the running executable's path.");

        var pid = Environment.ProcessId;
        var scriptPath = Path.Combine(Path.GetDirectoryName(installerPath)!, "stats-update.cmd");
        var scriptBytes = System.Text.Encoding.ASCII.GetBytes(BuildUpdateScript(pid, installerPath, exePath));
        using (var scriptStream = new FileStream(scriptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            scriptStream.Write(scriptBytes, 0, scriptBytes.Length);

        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }

    /// <summary>Waits up to 2 minutes (120 * ~1 s ping) for this PID to exit before giving up — bounds the loop
    /// so a wedged app can't leave cmd.exe polling forever. `ping -n 2 127.0.0.1` is used instead of
    /// `timeout /t 1` because timeout fails outright when its stdin is redirected (as it is here, launched
    /// hidden/detached with UseShellExecute=false).</summary>
    private static string BuildUpdateScript(int pid, string installerPath, string exePath) =>
        "@echo off\r\n" +
        "set n=0\r\n" +
        ":wait\r\n" +
        $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul\r\n" +
        "if errorlevel 1 goto run\r\n" +
        "set /a n+=1\r\n" +
        "if %n% gtr 120 exit /b\r\n" +
        "ping -n 2 127.0.0.1 >nul\r\n" +
        "goto wait\r\n" +
        ":run\r\n" +
        $"\"{installerPath}\" /SILENT /NOCANCEL\r\n" +
        $"start \"\" \"{exePath}\"\r\n";
}
