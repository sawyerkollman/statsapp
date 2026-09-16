using Stats.App.Views;
using Stats.Core.ViewModels;
using Stats.Core.Settings;
using Stats.Core.Sensors;
using Stats.Core.Frames;
using Stats.Core.Alerts;
using System.Diagnostics;
using H.NotifyIcon.Core;

namespace Stats.App;

public partial class App
{
    private ThemeStudioWindow? _themeStudio;
    private LabViewModel? _labVm;
    private LabWindow? _labWindow;
    private SceneComposerWindow? _sceneWindow;
    private bool _autoRecordingOwned, _startingAutoRecording;
    private void InitializeBetaLab(string directory)
    {
        _labVm = new LabViewModel(directory, _definitions);
        _labVm.RecordingRequested += OnLabRecordingRequested;
        _labVm.GameStateChanged += (gaming, name) =>
        {
            if (!gaming || _settings is null) return;
            var game = _labVm.Games.FirstOrDefault(g => string.Equals(g.ExecutableBaseName, name, StringComparison.OrdinalIgnoreCase));
            if (game is null) return;
            if (!string.IsNullOrWhiteSpace(game.LayoutName))
            {
                var profile = _settings.LayoutProfiles.FirstOrDefault(p => p.Name == game.LayoutName);
                if (profile is not null && SafeAutomaticFrameSelection(profile.DashboardMetrics.Concat(profile.OverlayMetrics)))
                    _dashboardVm?.ApplyLayoutProfile(game.LayoutName);
            }
            var scene = _settings.Scenes.FirstOrDefault(s => s.Name == game.OverlaySceneName);
            if (scene is not null && SafeAutomaticFrameSelection(scene.Layout.OverlayMetrics)) ApplyScene(scene, true);
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
                _labVm.Status = $"Post-game recording: {name}. Open Session lab for replay and analysis.";
            }
        }
        catch (Exception ex) { _labVm.Error = "Automatic recording failed: " + ex.Message; }
    }
    private void RefreshBetaLab(SensorSnapshot snapshot)
    {
        if (_labVm is null || _settings is null) return;
        var selected = _settings.DashboardMetrics.Concat(_settings.OverlayMetrics).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(128)
            .ToDictionary(id => id, id => snapshot.Values.GetValueOrDefault(id));
        _labVm.ObserveSnapshot(selected, snapshot.TimestampUtc, _settings.AlertsEnabled, snapshot.Values, _settings.PollIntervalSeconds);
        _labVm.SetManualRecording(_recorder?.IsRecording == true && !_autoRecordingOwned);
        string? name = null;
        if (_labVm.Options.AutoGamingEnabled && _frameReader?.IsActive == true)
        {
            try { if (ForegroundProcess.CurrentPid() is int pid) { using var process = Process.GetProcessById(pid); name = process.ProcessName; } }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        _labVm.ObserveGaming(name, snapshot.Values.GetValueOrDefault(FrameMetrics.FpsId), snapshot.TimestampUtc);
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
