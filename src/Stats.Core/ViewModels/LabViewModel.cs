using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Lab;
using Stats.Core.Metrics;

namespace Stats.Core.ViewModels;

public sealed partial class LabViewModel : ObservableObject, IDisposable
{
    private readonly string _directory;
    private readonly CompoundEvaluator _compound = new();
    private readonly Dictionary<string, bool> _availability = new();
    private CompactHistory? _history;
    private Task? _historyDrain;
    private bool _historyRestarting;
    private DateTime? _candidateSince, _idleSince;
    private string? _candidateName, _activeName;
    private bool _gaming, _ownedRecording, _pendingStart, _manualRecording, _timelineDirty;
    private bool _notebookLoadFailed;
    public LabViewModel(string directory, IEnumerable<MetricDefinition>? definitions = null)
    {
        _directory = Path.GetFullPath(directory);
        Options = LabOptionsStore.Load(directory);
        Metrics = (definitions ?? []).ToArray();
        Games = new(Options.Games); Rules = new(Options.Rules); NamedRuleSets = new(Options.NamedCompoundRuleSets);
        Error = LabOptionsStore.Error ?? "";
        LoadTimeline();
        try { foreach (var entry in TuningNotebook.Load(_directory).Entries) NotebookEntries.Add(entry); } catch (Exception ex) { _notebookLoadFailed = true; Error = "Notebook unavailable; fix or remove it before saving: " + ex.Message; }
    }
    public LabOptions Options { get; }
    public IReadOnlyList<MetricDefinition> Metrics { get; }
    public ObservableCollection<GameLabOption> Games { get; }
    public ObservableCollection<CompoundRule> Rules { get; }
    public ObservableCollection<NamedCompoundRuleSet> NamedRuleSets { get; }
    public ObservableCollection<LabTimelineEvent> Timeline { get; } = new();
    public ObservableCollection<HistoryAggregate> History { get; } = new();
    public ObservableCollection<TuningNotebookEntry> NotebookEntries { get; } = new();
    [ObservableProperty] private string _status = "Automation is off. No fan settings are changed by Beta lab.";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _bookmarkText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private GameLabOption? _selectedGame;
    [ObservableProperty] private CompoundRule? _selectedRule;
    [ObservableProperty] private string _diagnosticPreview = "{}";
    [ObservableProperty] private TuningNotebookEntry? _selectedNotebookEntry;
    [ObservableProperty] private string _feedbackText = "";
    [ObservableProperty] private string _ruleSetName = "";
    [ObservableProperty] private string _postGameReportText = "";
    public bool HasPostGameReport => PostGameReportText.Length > 0;
    public event Action<bool, string?>? RecordingRequested;
    public event Action<bool, string?>? GameStateChanged;
    public event Action<LabTimelineEvent>? CompoundNotificationRequested;
    public event Action<string, string?>? OpenNotebookRecordingsRequested;
    public event Action? OpenPostGameReportRequested;

    public void SetManualRecording(bool value) => _manualRecording = value && !_ownedRecording;
    public void AcknowledgeRecording(bool started, bool owned)
    {
        _pendingStart = false; _ownedRecording = started && owned;
        if (_ownedRecording && !Options.AutoGamingEnabled) StopOwnedRecording();
    }
    public void ObserveGaming(string? name, float? fps, DateTime utc)
    {
        if (!Options.AutoGamingEnabled)
        { LeaveGame(utc); _candidateName = null; _candidateSince = _idleSince = null; return; }
        var active = !string.IsNullOrWhiteSpace(name) && fps is float value && float.IsFinite(value) && value >= 10;
        if (active)
        {
            _idleSince = null;
            if (_gaming && !string.Equals(name, _activeName, StringComparison.OrdinalIgnoreCase)) LeaveGame(utc);
            if (!string.Equals(name, _candidateName, StringComparison.OrdinalIgnoreCase))
            { _candidateName = name; _candidateSince = utc; }
            _candidateSince ??= utc;
            if (!_gaming && utc - _candidateSince.Value >= TimeSpan.FromSeconds(5))
            {
                _gaming = true; _activeName = name;
                Status = "Gaming: " + name; AddTimeline("game", "Entered " + name, utc);
                GameStateChanged?.Invoke(true, name);
                var game = Games.FirstOrDefault(g => string.Equals(g.ExecutableBaseName, name, StringComparison.OrdinalIgnoreCase));
                if (game?.AutoRecord == true && !_manualRecording && !_ownedRecording && !_pendingStart)
                { _pendingStart = true; RecordingRequested?.Invoke(true, name); }
            }
        }
        else
        {
            _candidateName = null; _candidateSince = null; _idleSince ??= utc;
            if (_gaming && utc - _idleSince.Value >= TimeSpan.FromSeconds(20)) LeaveGame(utc);
        }
    }
    private void StopOwnedRecording()
    {
        if (!_ownedRecording) return;
        _ownedRecording = false; RecordingRequested?.Invoke(false, _activeName);
    }
    private void LeaveGame(DateTime utc)
    {
        StopOwnedRecording();
        if (!_gaming) return;
        _gaming = false; Status = "Desktop · post-game recording available in Session lab.";
        AddTimeline("game", "Left " + _activeName, utc);
        GameStateChanged?.Invoke(false, _activeName); _activeName = null;
    }
    public void ObserveSnapshot(IReadOnlyDictionary<string, float?> values, DateTime utc, bool alertsEnabled = true,
        IReadOnlyDictionary<string, float?>? ruleValues = null, double pollSeconds = 1)
    {
        foreach (var (id, value) in values.Take(128))
        {
            var available = value is float number && float.IsFinite(number);
            if (_availability.TryGetValue(id, out var previous) && previous != available)
                AddTimeline("sensor", (available ? "Recovered: " : "Missing: ") + MetricName(id), utc);
            if (_availability.Count < 128 || _availability.ContainsKey(id)) _availability[id] = available;
        }
        if (alertsEnabled)
            foreach (var rule in _compound.Tick(Rules, ruleValues ?? values, utc, pollSeconds))
            {
                var entry = new LabTimelineEvent(utc, rule.TestOnly ? "test" : "compound",
                    $"{MetricName(rule.FirstMetricId)} and {MetricName(rule.SecondMetricId)} held for {rule.HoldSeconds}s", rule.Id);
                AddTimeline(entry.Kind, entry.Text, utc);
                if (!rule.TestOnly) CompoundNotificationRequested?.Invoke(entry);
            }
        else _compound.Reset();
        if (Options.HistoryEnabled && !_historyRestarting && (_historyDrain is null || _historyDrain.IsCompleted))
        {
            _history ??= new CompactHistory(_directory);
            _history.Record(values, utc, Options.RetentionDays, Options.MaximumHistoryMb);
            if (_history.Error is { } error) Error = error;
        }
        // Keep the bounded writer idle while disabled; shutdown drains it after the poller/fan sequence.
    }
    private string MetricName(string id) => Metrics.FirstOrDefault(m => m.Id == id)?.DisplayName ?? id;
    public void AddThresholdEvent(string metricId, bool raised, DateTime utc) =>
        AddTimeline("threshold", (raised ? "Raised: " : "Ended: ") + MetricName(metricId), utc);
    public void AddTimeline(string kind, string text, DateTime utc)
    {
        Timeline.Insert(0, new(utc, kind, text[..Math.Min(text.Length, 200)]));
        while (Timeline.Count > 500) Timeline.RemoveAt(Timeline.Count - 1);
        _timelineDirty = true;
    }
    [RelayCommand] private void Bookmark()
    {
        if (string.IsNullOrWhiteSpace(BookmarkText)) return;
        AddTimeline("bookmark", BookmarkText, DateTime.UtcNow); BookmarkText = ""; SaveTimeline();
    }
    [RelayCommand] private void AddGame()
    {
        if (Games.Count >= 100) return;
        var game = new GameLabOption(); Games.Add(game); SelectedGame = game;
    }
    [RelayCommand] private void RemoveGame() { if (SelectedGame is { } game) Games.Remove(game); }
    [RelayCommand] private void AddRule()
    {
        if (Rules.Count >= 32) return;
        var rule = new CompoundRule { TestOnly = true }; Rules.Add(rule); SelectedRule = rule;
    }
    [RelayCommand] private void RemoveRule() { if (SelectedRule is { } rule) Rules.Remove(rule); }
    [RelayCommand] private void SaveRuleSet()
    {
        var name = RuleSetName.Trim(); if (name.Length == 0 || Rules.Count > 32) { Error = "Enter a rule-set name."; return; }
        var item = new NamedCompoundRuleSet { Name = name[..Math.Min(100, name.Length)], Rules = Rules.Select(LabOptionsStore.Clone).ToList() };
        var old = NamedRuleSets.FirstOrDefault(x => x.Name == item.Name); if (old is not null) NamedRuleSets[NamedRuleSets.IndexOf(old)] = item; else if (NamedRuleSets.Count < 16) NamedRuleSets.Add(item); else { Error = "Rule-set limit is 16."; return; }
        Options.NamedCompoundRuleSets = NamedRuleSets.ToList(); SaveCommand.Execute(null);
    }
    [RelayCommand] private void ApplyRuleSet(string? name) => ApplyGameAlertSet(name);
    [RelayCommand] private void DeleteRuleSet(string? name) { var item = NamedRuleSets.FirstOrDefault(x => x.Name == name); if (item is not null) NamedRuleSets.Remove(item); Options.NamedCompoundRuleSets = NamedRuleSets.ToList(); SaveCommand.Execute(null); }
    public bool ApplyGameAlertSet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var set = NamedRuleSets.FirstOrDefault(x => x.Name == name); if (set is null) return false;
        Rules.Clear(); foreach (var rule in set.Rules.Take(32)) Rules.Add(LabOptionsStore.Clone(rule)); _compound.Reset(); Options.Rules = Rules.ToList(); LabOptionsStore.Save(_directory, Options); Error = LabOptionsStore.Error ?? ""; return Error.Length == 0;
    }
    [RelayCommand] private void AddNotebookEntry() { if (NotebookEntries.Count < 100) { var entry = new TuningNotebookEntry(); NotebookEntries.Add(entry); SelectedNotebookEntry = entry; } }
    [RelayCommand] private void RemoveNotebookEntry() { if (SelectedNotebookEntry is { } entry) NotebookEntries.Remove(entry); SelectedNotebookEntry = NotebookEntries.FirstOrDefault(); }
    [RelayCommand] private void SaveNotebook() { if (_notebookLoadFailed) { Error = "Notebook was not loaded; refusing to overwrite it."; return; } try { TuningNotebook.Save(_directory, new TuningNotebook { Entries = NotebookEntries.ToList() }); Error = ""; } catch (Exception ex) { Error = "Notebook could not be saved: " + ex.Message; } }
    [RelayCommand] private void CompareNotebookRuns() { if (SelectedNotebookEntry is { RecordingA: { Length: > 0 } a }) OpenNotebookRecordingsRequested?.Invoke(a, SelectedNotebookEntry.RecordingB); }
    [RelayCommand] private void OpenPostGameReport() => OpenPostGameReportRequested?.Invoke();
    public void SetPostGameReport(string? text) { PostGameReportText = text ?? ""; OnPropertyChanged(nameof(HasPostGameReport)); }
    [RelayCommand] private void Save()
    {
        Options.Games = Games.ToList(); Options.Rules = Rules.ToList();
        LabOptionsStore.Save(_directory, Options); Error = LabOptionsStore.Error ?? "";
        OnPropertyChanged(nameof(Options)); OnPropertyChanged(nameof(SelectedRule));
        if (!Options.AutoGamingEnabled) ObserveGaming(null, null, DateTime.UtcNow);
        SaveTimeline();
    }
    [RelayCommand] private async Task RefreshHistoryAsync()
    {
        if (IsBusy) return; IsBusy = true;
        try
        {
            var rows = await Task.Run(() => { using var reader = new CompactHistory(_directory); return reader.ReadRecent(); });
            History.Clear(); foreach (var row in rows) History.Add(row);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private async Task RestartHistoryAsync()
    {
        _historyRestarting = true;
        DrainHistory();
        if (_historyDrain is not null) await _historyDrain;
        _historyRestarting = false;
        if (Error.StartsWith("History", StringComparison.OrdinalIgnoreCase)) Error = "";
    }
    private void DrainHistory()
    {
        if (_history is null) return;
        var history = _history; _history = null;
        _historyDrain = Task.Run(history.Dispose);
    }
    public void SetDiagnostics(string version, string os, string runtime, bool is64Bit, long? workingSetBytes = null, double? cpuSeconds = null, double? uptimeSeconds = null)
    {
        DiagnosticPreview = JsonSerializer.Serialize(new { Version = version, OS = os, Runtime = runtime,
            Is64Bit = is64Bit, MetricCount = Metrics.Count, CompoundRuleCount = Rules.Count,
            HistoryEnabled = Options.HistoryEnabled, WorkingSetBytes = workingSetBytes, ProcessCpuSeconds = cpuSeconds,
            ProcessUptimeSeconds = uptimeSeconds,
            AverageCpuPercentOfOneCore = uptimeSeconds > 0 ? cpuSeconds / uptimeSeconds * 100 : null,
            Measurement = "Process totals sampled when this window opens; average since launch, not instantaneous or a hardware benchmark." }, new JsonSerializerOptions { WriteIndented = true });
    }
    private void LoadTimeline()
    {
        try
        {
            var path = Path.Combine(_directory, "timeline.json");
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Timeline exceeds 1 MB.");
            foreach (var entry in (JsonSerializer.Deserialize<LabTimelineEvent[]>(File.ReadAllText(path)) ?? [])
                .Where(e => e is not null && e.Text is not null && e.Kind is not null && e.Text.Length <= 200 && e.Kind.Length <= 30).Take(500))
                Timeline.Add(entry);
        }
        catch (Exception ex) { Error = "Timeline unavailable: " + ex.Message; }
    }
    public void SaveTimeline()
    {
        if (!_timelineDirty) return;
        var path = Path.Combine(_directory, "timeline.json"); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory); File.WriteAllText(temp, JsonSerializer.Serialize(Timeline.ToArray()));
            File.Move(temp, path, true); _timelineDirty = false;
        }
        catch (Exception ex) { Error = "Timeline could not be saved: " + ex.Message; }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
    }
    public void Dispose() { DrainHistory(); _historyDrain?.GetAwaiter().GetResult(); SaveTimeline(); }
}
