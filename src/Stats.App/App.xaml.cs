using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Resources;
using System.Windows.Threading;
using H.NotifyIcon;
using Stats.App.Helpers;
using Stats.App.Tray;
using Stats.App.Views;
using Stats.Core.Fans;
using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.Startup;
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
    private StartupTaskService? _startupTaskService;
    /// <summary>Entry-assembly version, resolved once at startup and reused by the automatic update-check loop,
    /// the manual "Check for updates" button, and the About section's version display.</summary>
    private Version _currentVersion = new(0, 0, 0, 0);
    /// <summary>Cancelled by OnExit() so an in-flight manual "Check for updates" doesn't try to touch a
    /// tearing-down SettingsViewModel.</summary>
    private CancellationTokenSource? _manualCheckCts;
    /// <summary>True until the first ShowDashboard() call (tray click or Settings/Open dashboard) — that call
    /// performs one immediate RefreshAll() against already-polled data before showing, since a --minimized
    /// launch skipped the initial Show() and its first snapshot dispatch race.</summary>
    private bool _pendingFirstShowRefresh;
    /// <summary>Cancelled by ExitApp()/OnExit() so an in-flight install download bails out (and resets the busy
    /// state) instead of racing app shutdown; a fresh instance per "Update now" click.</summary>
    private CancellationTokenSource? _installCts;
    /// <summary>0 = not run yet, 1 = fatal cleanup has run. Guards DispatcherUnhandledException and
    /// AppDomain.UnhandledException so a race between the two (or a repeated fault) only restores fans and
    /// flushes the log once.</summary>
    private int _fatalCleanupDone;
    /// <summary>Shared debounce for Dashboard/Peaks/Fans window-bounds persistence: coalesces bursts of drag/resize
    /// events (each of which already mutated the in-memory AppSettings) into one SaveSettings() call about five
    /// seconds after the last change. The explicit exit-time SaveSettings() in OnExit remains the final backstop
    /// if the app closes before this timer fires.</summary>
    private DispatcherTimer? _boundsSaveTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        RollingTraceLog.Install();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);

        bool startMinimized = StartupArgs.HasMinimizedFlag(e.Args); // case-insensitive: installer/shortcuts may pass any casing
        _currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

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
        _settingsVm.OpenLogFolderRequested += OpenLogFolder;
        _settingsVm.StartupToggleRequested += OnStartupToggleRequested;
        _settingsVm.CheckForUpdatesRequested += OnManualCheckForUpdatesRequested;
        _settingsVm.RestartRequested += OnRestartNowRequested;
        _settingsVm.SetVersionInfo(UpdateChecker.FormatVersionDisplay(_currentVersion), UpdateChecker.IsDevBuild(_currentVersion));
        _dashboardVm.SettingsOpened += () => _ = RefreshStartupTaskStateAsync();
        _startupTaskService = new StartupTaskService();

        ClickThrough.Set(_overlay, _settings.OverlayClickThrough);

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += ToggleOverlay;
        ApplyHotkey();

        _store.ResizeAll(HistoryCapacity.Compute(_settings.HistoryWindowMinutes, _settings.PollIntervalSeconds));

        _dashboardVm.OpenPeaksRequested += ShowPeaks;
        _dashboardVm.OpenFansRequested += ShowFans;
        _dashboardVm.DashboardMetricsChanged += () => { _peaksVm?.RebuildRows(); ApplyFrameTracing(); };
        _dashboardVm.InstallUpdateRequested += OnInstallUpdateRequested;
        _dashboardVm.OpenReleasePageRequested += OnOpenReleasePageRequested;
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
        // HealthChanged fires on the poll thread (see SensorPoller); dispatch its already-immutable state to the
        // dashboard VM the same way every other poll-thread → UI signal here is marshaled.
        _poller.HealthChanged += state => Dispatcher.BeginInvoke(() => _dashboardVm.SetSensorHealth(state));

        _dashboard.AllowClose = false; // close button hides to tray; exit via tray menu
        _dashboard.LocationChanged += (_, _) => SaveWindowBounds();
        _dashboard.SizeChanged += (_, _) => SaveWindowBounds();
        SessionEnding += (_, _) => ExitApp();
        _pendingFirstShowRefresh = true; // consumed by the first ShowDashboard() call, whether that's now or a later tray click
        if (!startMinimized) _dashboard.Show(); // --minimized: dashboard/tray/services/poller are still fully constructed above, just not shown
        _poller.Start();
        StartUpdateChecks();
        _ = RefreshStartupTaskStateAsync(); // settings load: reflect the actual "Stats" logon task state, not a persisted setting
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _installCts?.Cancel();
        _installCts?.Dispose();
        _manualCheckCts?.Cancel();
        _manualCheckCts?.Dispose();
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

    /// <summary>WPF's last-chance handler for an exception that reached the Dispatcher's message loop unhandled.
    /// Deliberately never sets e.Handled — this is fatal-cleanup-then-terminate, not error recovery.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        FatalCleanup("DispatcherUnhandledException", e.Exception);

    /// <summary>Last-resort handler for an exception that unwound an entire thread (e.g. a background Task
    /// continuation with no awaiter). There is no "mark handled" here — the CLR is already terminating the
    /// process by the time this fires when e.IsTerminating is true.</summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        FatalCleanup("AppDomain.UnhandledException", e.ExceptionObject as Exception
            ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown non-Exception payload"));

    /// <summary>A Task's exception was never observed (awaited/handled) before its finalizer ran. This is not
    /// treated as fatal — the process keeps running — so it is only logged and flushed, and deliberately left
    /// unobserved (not calling e.SetObserved()) so the condition stays visible rather than being silently hidden.</summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Trace.WriteLine("[Stats] UnobservedTaskException: " + e.Exception);
        FlushTraceListeners();
    }

    /// <summary>Runs once per process, however many fatal handlers race to call it. Traces the failure,
    /// best-effort restores every fan to device control (the same release path OnExit uses — no second release
    /// path), then flushes so the trace log survives process termination. Never swallows or converts the fault
    /// into continued execution.</summary>
    private void FatalCleanup(string source, Exception ex)
    {
        if (Interlocked.Exchange(ref _fatalCleanupDone, 1) != 0) return;
        try { Trace.WriteLine($"[Stats] FATAL ({source}): {ex}"); }
        catch (Exception) { /* logging must never block cleanup */ }
        try { _fanController?.RestoreAll(); }
        catch (Exception restoreError)
        {
            try { Trace.WriteLine("[Stats] Fatal fan restore failed: " + restoreError); }
            catch (Exception) { /* logging must never block cleanup */ }
        }
        FlushTraceListeners();
    }

    private static void FlushTraceListeners()
    {
        try { Trace.Flush(); }
        catch (Exception) { /* best-effort */ }
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

    /// <summary>Called after Dashboard/Peaks/Fans LocationChanged/SizeChanged already updated the in-memory
    /// AppSettings bounds. Restarts a single shared ~5s timer so a drag or resize that fires many events only
    /// persists once, shortly after the last one — window bounds are no longer left only to the exit-time save.
    /// UI-thread only, like the window events that call it.</summary>
    private void ScheduleBoundsSave()
    {
        if (_boundsSaveTimer is null)
        {
            _boundsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _boundsSaveTimer.Tick += (_, _) =>
            {
                _boundsSaveTimer!.Stop();
                SaveSettings();
            };
        }
        _boundsSaveTimer.Stop();
        _boundsSaveTimer.Start();
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
        ScheduleBoundsSave();
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

    /// <summary>Tray icon left-click, "Open dashboard", and "Settings" all funnel through here. The very first
    /// call refreshes the dashboard VM against the already-current MetricStore before showing/activating —
    /// closes the gap between a --minimized launch's skipped initial Show() and whatever poll tick last ran.
    /// </summary>
    private void ShowDashboard()
    {
        if (_dashboard is null) return;
        if (_pendingFirstShowRefresh)
        {
            _pendingFirstShowRefresh = false;
            _dashboardVm?.RefreshAll();
        }
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
        ScheduleBoundsSave();
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
        ScheduleBoundsSave();
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

    /// <summary>Settings "Open log folder". Creates the folder if RollingTraceLog.Install() never got that far
    /// (e.g. it failed before creating the directory) and opens it with the shell; a failure is surfaced inline
    /// rather than silently ignored, per the Diagnostics section's explicit error state.</summary>
    private void OpenLogFolder()
    {
        if (_settingsVm is null) return;
        try
        {
            var dir = RollingTraceLog.LogDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stats", "logs");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            _settingsVm.DiagnosticsError = "";
        }
        catch (Exception ex)
        {
            _settingsVm.DiagnosticsError = "Couldn't open log folder: " + ex.Message;
        }
    }

    // ---- startup (autostart) ----

    /// <summary>Settings checkbox toggled. Runs the same schtasks /Create the installer's Scheduled Task uses
    /// (see StartupTaskCommands.Create) or /Delete, then re-queries so the checkbox always reflects actual OS
    /// state rather than the click that requested it — a failed mutation snaps the checkbox back automatically.
    /// </summary>
    private async void OnStartupToggleRequested(bool enable)
    {
        if (_settingsVm is null || _startupTaskService is null) return;
        _settingsVm.ApplyStartupState(enable, busy: true, error: "");

        string error = "";
        try
        {
            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    throw new InvalidOperationException("Could not resolve the running executable's path.");
                var result = await _startupTaskService.CreateAsync(exePath).ConfigureAwait(true);
                if (!result.Succeeded) error = FormatStartupTaskError("create", result);
            }
            else
            {
                var result = await _startupTaskService.DeleteAsync().ConfigureAwait(true);
                if (!result.Succeeded) error = FormatStartupTaskError("remove", result);
            }
        }
        catch (Exception ex)
        {
            error = $"Could not {(enable ? "enable" : "disable")} startup: {ex.Message}";
        }

        await RefreshStartupTaskStateAsync(error, desiredState: enable, allowWhileBusy: true).ConfigureAwait(true);
    }

    /// <summary>Re-queries the "Stats" logon Scheduled Task and pushes the actual result into the Settings VM.
    /// <paramref name="pendingError"/> (from a just-completed /Create or /Delete) takes priority over a query
    /// failure so the user sees why the mutation didn't take, not a generic "couldn't check" message.</summary>
    private async Task RefreshStartupTaskStateAsync(
        string pendingError = "",
        bool? desiredState = null,
        bool allowWhileBusy = false)
    {
        if (_settingsVm is null || _startupTaskService is null) return;
        if (_settingsVm.StartupBusy && !allowWhileBusy) return;
        _settingsVm.ApplyStartupState(_settingsVm.StartupEnabled, busy: true, error: "");

        bool enabled;
        string queryError = "";
        try
        {
            var result = await _startupTaskService.QueryAsync().ConfigureAwait(true);
            enabled = result.Succeeded;
            if (!enabled && !StartupTaskIsMissing(result))
                queryError = FormatStartupTaskError("check", result);
        }
        catch (Exception ex)
        {
            enabled = false;
            queryError = "Could not check startup status: " + ex.Message;
        }

        if (queryError.Length == 0 && desiredState == enabled) pendingError = "";
        _settingsVm.ApplyStartupState(enabled, busy: false, pendingError.Length > 0 ? pendingError : queryError);
    }

    private static bool StartupTaskIsMissing(StartupTaskResult result)
    {
        var detail = result.StandardError + "\n" + result.StandardOutput;
        return detail.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatStartupTaskError(string verb, StartupTaskResult result)
    {
        var detail = !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError
            : !string.IsNullOrWhiteSpace(result.StandardOutput) ? result.StandardOutput
            : $"exit code {result.ExitCode}";
        return $"Could not {verb} the startup task ({detail}).";
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

    /// <summary>Settings Hardware "Restart now". Starts a brand-new instance of the running executable — with
    /// <c>UseShellExecute = false</c> so the child directly inherits our elevated token instead of going through
    /// the shell's elevation broker, which would otherwise re-prompt UAC even though we're already elevated —
    /// and only then calls the existing <see cref="ExitApp"/> so the poller stops, fans restore, and settings
    /// flush through the normal clean-exit path. A launch failure is surfaced inline; this process is left
    /// running rather than exiting into nothing.</summary>
    private void OnRestartNowRequested()
    {
        if (_settingsVm is null) return;
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                throw new InvalidOperationException("Could not resolve the running executable's path.");
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false });
            _settingsVm.RestartError = "";
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[Stats] restart now failed: " + ex.Message);
            _settingsVm.RestartError = "Couldn't restart Stats: " + ex.Message;
            return;
        }
        ExitApp();
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
        if (UpdateChecker.IsDevBuild(_currentVersion)) return;

        _updateCts = new CancellationTokenSource();
        _ = RunUpdateLoopAsync(_currentVersion, _updateCts.Token);
    }

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

    /// <summary>Settings About "Check for updates" was clicked. Reuses <see cref="UpdateService.CheckAsync"/>
    /// exactly like the automatic loop, but with its manual (<c>throwOnFailure: true</c>) mode so a genuine
    /// network/HTTP failure can be told apart from a legitimate "no update" null and shown as an explicit inline
    /// error — unlike the automatic loop, which stays quiet either way. A found update is handed to the
    /// dashboard banner, not duplicated in the About section's own status line. A dev build never reaches this
    /// (the SettingsViewModel command itself guards it), but the guard is repeated here since App owns Process/
    /// Assembly access and must never call GitHub for one regardless of how it's invoked.</summary>
    private async void OnManualCheckForUpdatesRequested()
    {
        if (_settingsVm is null || _updateService is null) return;

        if (UpdateChecker.IsDevBuild(_currentVersion))
        {
            _settingsVm.ApplyManualCheckResult("Development build — updates are not available.");
            return;
        }

        _manualCheckCts?.Cancel();
        _manualCheckCts?.Dispose();
        var cts = _manualCheckCts = new CancellationTokenSource();
        try
        {
            var info = await _updateService.CheckAsync(_currentVersion, cts.Token, throwOnFailure: true).ConfigureAwait(true);
            if (info is null)
                _settingsVm.ApplyManualCheckResult("Up to date");
            else
            {
                _dashboardVm?.OfferUpdate(info);
                _settingsVm.ApplyManualCheckResult(""); // the dashboard banner carries the news
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // superseded by a newer click, or the app is exiting — leave whatever the newer call reports.
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[Stats] manual update check failed: " + ex.Message);
            _settingsVm.ApplyManualCheckResult("Couldn't check for updates — try again later.", failed: true);
        }
    }

    /// <summary>Dashboard banner "What's new" was clicked. Opens <see cref="UpdateInfo.ReleasePageUrl"/> with the
    /// shell; a launch failure is caught explicitly and surfaced next to the banner rather than left to throw.</summary>
    private void OnOpenReleasePageRequested(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The release page URL is not a valid GitHub HTTPS URL.");
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[Stats] failed to open release page: " + ex.Message);
            _dashboardVm?.SetReleasePageError("Couldn't open the release page.");
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
