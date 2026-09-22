using Stats.App.Views;
using Stats.Core.ViewModels;
using Stats.Core.Settings;
using Stats.Core.Sensors;
using Stats.Core.Frames;
using Stats.Core.Alerts;
using Stats.Core.Lab;
using System.Diagnostics;
using H.NotifyIcon.Core;

namespace Stats.App;

public partial class App
{
    private ThemeStudioWindow? _themeStudio;
    private LabViewModel? _labVm;
    private LabWindow? _labWindow;
    private GamingWindow? _gamingWindow;
    private GameAppearanceRestore? _gameAppearanceRestore;
    private bool _applyingGameAppearance;
    private string? _recordingGameName;
    private SceneComposerWindow? _sceneWindow;
    private bool _autoRecordingOwned, _startingAutoRecording;
    private string? _postGameRecordingPath;
    private void InitializeBetaLab(string directory)
    {
        _labVm = new LabViewModel(directory, _definitions);
        _labVm.RecordingRequested += OnLabRecordingRequested;
        _labVm.GamingRecordingRequested += OnGamingRecordingRequested;
        _labVm.OpenGamingSessionsRequested += ShowSessions;
        _labVm.OpenLabOptionsRequested += ShowBetaLab;
        _labVm.AppearanceOptionsSaved += ObserveGameAppearanceChanges;
        _labVm.OpenPostGameReportRequested += () =>
        {
            if (_postGameRecordingPath is { } path) OpenNotebookRecordings(path, null);
        };
        _labVm.OpenNotebookRecordingsRequested += OpenNotebookRecordings;
        _labVm.GameStateChanged += (gaming, name) =>
        {
            if (_settings is null) return;
            if (!gaming) { RestoreDesktopAppearance(); return; }
            var game = _labVm.Games.FirstOrDefault(g => string.Equals(g.ExecutableBaseName, name, StringComparison.OrdinalIgnoreCase));
            if (game is null) return;
            _gameAppearanceRestore = game.RestoreDesktopAfterGame ? new GameAppearanceRestore() : null;
            _gameAppearanceRestore?.CaptureBefore(_settings, _labVm.Rules);
            var applied = GameAppearanceDomains.None;
            _applyingGameAppearance = true;
            try
            {
            if (Stats.Core.Lab.GameAppearance.Apply(_settings, game.ThemeName))
            {
                applied |= GameAppearanceDomains.Theme;
                _settingsVm?.SyncTheme(); OnSettingsChanged(SettingsChange.Theme); SaveSettings();
            }
            if (_labVm.ApplyGameAlertSet(game.AlertSetName)) applied |= GameAppearanceDomains.CompoundRules;
            if (!string.IsNullOrWhiteSpace(game.LayoutName))
            {
                var profile = _settings.LayoutProfiles.FirstOrDefault(p => p.Name == game.LayoutName);
                if (profile is not null && SafeAutomaticFrameSelection(profile.DashboardMetrics.Concat(profile.OverlayMetrics)))
                    if (_dashboardVm?.ApplyLayoutProfile(game.LayoutName) == true)
                        applied |= GameAppearanceDomains.Layout | GameAppearanceDomains.Overlay;
            }
            var scene = _settings.Scenes.FirstOrDefault(s => s.Name == game.OverlaySceneName);
            if (scene is not null && SafeAutomaticFrameSelection(scene.Layout.OverlayMetrics))
            { ApplyScene(scene, true); applied |= GameAppearanceDomains.Overlay; }
            _gameAppearanceRestore?.Complete(_settings, _labVm.Rules, applied);
            }
            finally { _applyingGameAppearance = false; }
        };
        _labVm.CompoundNotificationRequested += entry =>
        {
            if (_settings is null || !AlertNotificationPolicy.ShouldNotify(_settings.AlertNotificationsEnabled,
                _settings.AlertNotificationsSkipWhenForeground, _dashboard is { IsVisible: true, IsActive: true }) ||
                !_alertNotifications.TryAccept("compound:" + entry.RuleKey, entry.AtUtc)) return;
            try { _tray?.ShowNotification("Stats compound alert", entry.Text, NotificationIcon.Warning, sound: false); }
            catch (Exception ex) { _labVm.Error = "Notification unavailable: " + ex.Message; }
        };
    }
    private bool SafeAutomaticFrameSelection(IEnumerable<string> ids) =>
        _frameReader?.IsActive == true || !ids.Any(FrameMetrics.IsFrameMetric);

    private async void OnLabRecordingRequested(bool start, string? name)
    {
        if (_labVm is null) return;
        try
        {
            EnsureSessionViewModel();
            if (_sessionVm is null) return;
            if (start)
            {
                if (!_sessionVm.CanStart) { _labVm.AcknowledgeRecording(false, false); return; }
                _sessionVm.RecordingGameName = name ?? "";
                _sessionVm.IncludeRawFrames = false; // automatic recording never reuses manual per-frame consent
                _startingAutoRecording = true;
                try { _sessionVm.StartCommand.Execute(null); }
                finally { _startingAutoRecording = false; }
                _autoRecordingOwned = _sessionVm.IsRecording;
                _labVm.AcknowledgeRecording(_autoRecordingOwned, _autoRecordingOwned);
                if (!_autoRecordingOwned) _labVm.Error = _sessionVm.Error;
            }
            else if (_autoRecordingOwned)
            {
                _autoRecordingOwned = false;
                await _sessionVm.StopCommand.ExecuteAsync(null);
                _postGameRecordingPath = _sessionVm.HasReport ? _sessionVm.FilePath : null;
                _labVm.SetPostGameReport(_sessionVm.HasReport ? _sessionVm.ReportText : null);
                _labVm.Status = _sessionVm.HasReport ? $"Post-game report ready: {name}." : "Post-game report unavailable.";
                if (!string.IsNullOrEmpty(_sessionVm.Error)) _labVm.Error = _sessionVm.Error;
            }
        }
        catch (Exception ex) { _labVm.Error = "Automatic recording failed: " + ex.Message; }
    }
    private async void OpenNotebookRecordings(string first, string? second)
    {
        try
        {
            EnsureSessionViewModel();
            if (_sessionVm is null || !await _sessionVm.OpenAsync(first))
            { if (_labVm is not null) _labVm.Error = _sessionVm?.Error ?? "Session tools unavailable."; return; }
            if (!string.IsNullOrWhiteSpace(second) && !await _sessionVm.OpenComparisonAsync(second) && _labVm is not null)
                _labVm.Error = _sessionVm.Error;
            ShowSessions();
        }
        catch (Exception ex) { if (_labVm is not null) _labVm.Error = "Could not open notebook recordings: " + ex.Message; }
    }
    private void RefreshBetaLab(SensorSnapshot snapshot)
    {
        if (_labVm is null || _settings is null) return;
        var selected = _settings.DashboardMetrics.Concat(_settings.OverlayMetrics).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(128)
            .ToDictionary(id => id, id => snapshot.Values.GetValueOrDefault(id));
        _labVm.ObserveSnapshot(selected, snapshot.TimestampUtc, _settings.AlertsEnabled, snapshot.Values, _settings.PollIntervalSeconds);
        _labVm.SetManualRecording(_recorder?.IsRecording == true && !_autoRecordingOwned);
        string? name = null;
        ObserveGameAppearanceChanges();
        try { if (ForegroundProcess.CurrentPid() is int pid) { using var process = Process.GetProcessById(pid); name = process.ProcessName; } }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _labVm.ObserveGaming(_frameReader?.IsActive == true ? name : null, snapshot.Values.GetValueOrDefault(FrameMetrics.FpsId), snapshot.TimestampUtc);
        _recordingGameName = name;
        _labVm.RefreshGamingStatus(name, _frameReader?.CaptureStatus ?? new(FrameCaptureState.Inactive, "FPS capture is off."),
            snapshot.Values.GetValueOrDefault(FrameMetrics.FpsId), _settings.ActiveLayoutProfile, _recorder?.IsRecording == true);
        RefreshGamingRecordingState();
    }

    private void ObserveGameAppearanceChanges()
    {
        if (!_applyingGameAppearance && _settings is not null && _labVm is not null)
            _gameAppearanceRestore?.ObserveChanges(_settings, _labVm.Rules);
    }

    private void RestoreDesktopAppearance()
    {
        var restore = _gameAppearanceRestore;
        _gameAppearanceRestore = null;
        if (restore is null || _settings is null || _labVm is null) return;
        _applyingGameAppearance = true;
        try
        {
            var result = restore.Restore(_settings, _labVm.Rules);
            if (result.Theme) { _settingsVm?.SyncTheme(); OnSettingsChanged(SettingsChange.Theme); }
            if (result.Layout || result.Overlay) _dashboardVm?.RefreshRestoredLayout();
            if (result.Overlay)
            {
                _overlayVm?.Rebuild(); _overlayVm?.ApplyLayout(); _settingsVm?.SyncOverlay();
                if (_overlay is not null) _overlay.Opacity = _settings.OverlayOpacity;
                ApplyFrameTracing();
            }
            if (result.CompoundRules) _labVm.SaveCommand.Execute(null);
            SaveSettings();
        }
        finally { _applyingGameAppearance = false; }
    }

    private void ShowGaming()
    {
        if (_labVm is null) return;
        if (_gamingWindow is null)
        {
            _gamingWindow = new GamingWindow { DataContext = _labVm };
            _gamingWindow.Closed += (_, _) => _gamingWindow = null;
        }
        ShowMonitoringWindow(_gamingWindow);
    }

    private async void OnGamingRecordingRequested(bool start)
    {
        EnsureSessionViewModel();
        if (_sessionVm is null || _labVm is null) return;
        try
        {
            if (start)
            {
                _sessionVm.RecordingGameName = _recordingGameName ?? "";
                _sessionVm.IncludeRawFrames = false; // use Sessions for the explicit per-frame opt-in
                _sessionVm.StartCommand.Execute(null);
            }
            else
            {
                _autoRecordingOwned = false;
                _labVm.AcknowledgeRecording(false, false);
                await _sessionVm.StopCommand.ExecuteAsync(null);
            }
            _labVm.GamingRecording = _recorder?.IsRecording == true;
            _labVm.Error = _sessionVm.Error;
            RefreshGamingRecordingState();
        }
        catch (Exception ex) { _labVm.Error = "Recording failed: " + ex.Message; }
    }

    private void RefreshGamingRecordingState()
    {
        if (_labVm is null) return;
        _labVm.GamingRecordingStatus = _sessionVm?.Status ?? "Not recording";
        _labVm.GamingRecordingError = _sessionVm?.Error ?? "";
        _labVm.GamingCanStart = _sessionVm?.CanStart ?? true;
        _labVm.GamingCanStop = _sessionVm?.CanStop ?? false;
    }
    private void ShowBetaLab()
    {
        if (_labVm is null) return;
        long? workingSet = null;
        double? cpuSeconds = null, uptimeSeconds = null;
        try
        {
            using var self = Process.GetCurrentProcess();
            workingSet = self.WorkingSet64; cpuSeconds = self.TotalProcessorTime.TotalSeconds;
            uptimeSeconds = Math.Max(0, (DateTime.UtcNow - self.StartTime.ToUniversalTime()).TotalSeconds);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { _labVm.Error = "Process overhead counters are unavailable; other lab features remain available."; }
        _labVm.SetDiagnostics(_currentInformationalVersion ?? _currentVersion.ToString(), Environment.OSVersion.VersionString,
            Environment.Version.ToString(), Environment.Is64BitProcess, workingSet, cpuSeconds, uptimeSeconds);
        if (_labWindow is null)
        {
            _labWindow = new LabWindow { DataContext = _labVm };
            _labWindow.Closed += (_, _) => _labWindow = null;
        }
        ShowMonitoringWindow(_labWindow);
    }
    private void ShowScenes()
    {
        if (_settings is null) return;
        if (_sceneWindow is null)
        {
            var vm = new SceneComposerViewModel(_settings, _definitions, SaveSettings);
            vm.SceneApplied += ApplyScene;
            vm.EditOverlayRequested += ShowOverlayEditor;
            _sceneWindow = new SceneComposerWindow { DataContext = vm };
            _sceneWindow.Closed += (_, _) => _sceneWindow = null;
        }
        ShowMonitoringWindow(_sceneWindow);
    }
    private void ApplyScene(DashboardScene scene, bool overlayOnly)
    {
        if (_settings is null) return;
        scene.Validate(); scene.RestoreOverlay(_settings);
        if (!overlayOnly)
        {
            _settings.SceneSectionLabels = new(scene.SectionLabels);
            _dashboardVm?.ApplySceneLayout(scene.Layout);
        }
        else
        {
            _settings.OverlayMetrics.Clear(); _settings.OverlayMetrics.AddRange(scene.Layout.OverlayMetrics);
            _dashboardVm?.SyncOverlaySelection();
        }
        _overlayVm?.Rebuild(); _overlayVm?.ApplyLayout();
        _settingsVm?.SyncOverlay();
        if (_overlay is not null) _overlay.Opacity = _settings.OverlayOpacity;
        ApplyFrameTracing(); SaveSettings();
    }
    private void ShowThemeStudio()
    {
        if (_settings is null) return;
        if (_themeStudio is null)
        {
            var vm = new ThemeStudioViewModel(_settings, SaveSettings);
            vm.Applied += () => { _settingsVm?.SyncTheme(); OnSettingsChanged(SettingsChange.Theme); _dashboardVm?.RefreshAll(); };
            _themeStudio = new ThemeStudioWindow { DataContext = vm };
            _themeStudio.Closed += (_, _) => _themeStudio = null;
        }
        ShowMonitoringWindow(_themeStudio);
    }
}
