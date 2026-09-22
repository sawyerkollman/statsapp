using System.Text.Json;
using Stats.Core.Metrics;
using Stats.Core.Recording;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public sealed class SessionLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Stats.SessionLibrary", Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition Cpu = new("cpu", "CPU", MetricGroup.Cpu, "CPU", "%");

    [Fact]
    public async Task Library_GroupsFiltersPinsAndSurfacesUnreadableHeaders()
    {
        var game = Write("game", "Game", Start);
        var old = Write("old", null, Start.AddMinutes(-1));
        File.WriteAllText(Path.Combine(_directory, "broken.stats-session.jsonl"), "{");
        var library = new SessionLibrary(_directory);
        var entries = library.Read();
        Assert.Equal("Game", Assert.Single(entries, entry => entry.Path == game).GameName);
        Assert.Equal("Unassigned", Assert.Single(entries, entry => entry.Path == old).GameName);
        Assert.NotNull(Assert.Single(entries, entry => entry.Path.EndsWith("broken.stats-session.jsonl")).Error);
        library.SetPinned(game);
        Assert.Equal(game, new SessionLibrary(_directory).PinnedPath);
        File.WriteAllText(Path.Combine(_directory, ".library.json"), "{");
        var malformed = new SessionLibrary(_directory);
        Assert.Throws<InvalidDataException>(() => malformed.SetPinned(old));
        Assert.Equal("{", File.ReadAllText(Path.Combine(_directory, ".library.json")));

        using var recorder = new SessionRecorder(_directory);
        var vm = new SessionViewModel(recorder, () => [Cpu], recordingDirectory: _directory);
        vm.RefreshLibrary(); vm.LibraryFilter = "game";
        Assert.Single(vm.LibraryGroups); Assert.Equal("Game", vm.LibraryGroups[0].GameName);
        vm.PinnedBaselinePath = Path.Combine(_directory, "missing.stats-session.jsonl");
        await vm.CompareWithBaselineCommand.ExecuteAsync(null);
        Assert.Contains("missing", vm.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"type\":[]}")]
    [InlineData("{\"type\":\"header\",\"version\":[]}")]
    public void WrongHeaderKindsAreIsolatedToAnUnreadableEntry(string json)
    {
        Write("good", null, Start);
        File.WriteAllText(Path.Combine(_directory, "bad.stats-session.jsonl"), json);
        var entries = new SessionLibrary(_directory).Read();
        Assert.Equal(2, entries.Count);
        Assert.Single(entries, entry => entry.Error is not null);
    }

    [Theory]
    [InlineData(@"\\server\share\bad.stats-session.jsonl")]
    [InlineData(@"C:\outside.stats-session.jsonl")]
    [InlineData(@"\\?\C:\outside.stats-session.jsonl")]
    public void BaselinePathsCannotEscapeTheLibrary(string path)
    {
        Assert.Throws<InvalidDataException>(() => SessionLibrary.NormalizePinnedPath(_directory, path));
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(new { PinnedPath = path });
        File.WriteAllText(Path.Combine(_directory, ".library.json"), json);
        var library = new SessionLibrary(_directory);
        Assert.Null(library.PinnedPath);
        Assert.NotNull(library.Error);
        Assert.Throws<InvalidDataException>(() => library.SetPinned(null));
        Assert.Equal(json, File.ReadAllText(Path.Combine(_directory, ".library.json")));
    }

    [Fact]
    public async Task GamingCanFinalizeAPartialRecordingAfterWriterFailureAndStartAgain()
    {
        using var recorder = new SessionRecorder(_directory, () => Start.AddSeconds(3));
        var sessions = new SessionViewModel(recorder, () => [Cpu], () => Start);
        using var gaming = new LabViewModel(Path.Combine(_directory, "lab"), [Cpu]);
        sessions.StartCommand.Execute(null);
        recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = 1 }, Start.AddSeconds(1)));
        recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = 1 }, Start)); // visible clock failure
        sessions.RefreshRecorderState();
        gaming.GamingRecording = sessions.IsRecording;
        gaming.GamingCanStart = sessions.CanStart;
        gaming.GamingCanStop = sessions.CanStop;
        Assert.False(gaming.GamingRecording);
        Assert.False(gaming.GamingCanStart);
        Assert.True(gaming.GamingCanStop);
        Task finalize = Task.CompletedTask;
        gaming.GamingRecordingRequested += start => { if (!start) finalize = sessions.StopCommand.ExecuteAsync(null); };
        gaming.StopGamingRecordingCommand.Execute(null);
        await finalize;
        Assert.True(sessions.CanStart);
        Assert.True(sessions.HasReport);
        sessions.StartCommand.Execute(null);
        Assert.True(recorder.IsRecording);
        await sessions.StopCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task RawFrames_AreSessionOnlyAndStoppingIsAnnouncedBeforeTheRecorderStops()
    {
        using var recorder = new SessionRecorder(_directory);
        var captureChecks = 0;
        var unavailable = new SessionViewModel(recorder, () => [Cpu], recordingDirectory: _directory, rawCaptureAvailable: () => { captureChecks++; return false; }) { IncludeRawFrames = true };
        unavailable.StartCommand.Execute(null);
        Assert.False(recorder.IsRecording); Assert.Equal(1, captureChecks); Assert.Contains("does not start capture", unavailable.Error);

        var vm = new SessionViewModel(recorder, () => [Cpu], recordingDirectory: _directory, rawCaptureAvailable: () => true) { IncludeRawFrames = true, RecordingGameName = "Game" };
        var startedWithRaw = false; var stoppingWhileRecording = false;
        vm.RecordingStarted += () => startedWithRaw = vm.RecordingIncludesRawFrames;
        vm.RecordingStopping += () => stoppingWhileRecording = recorder.IsRecording;
        vm.StartCommand.Execute(null);
        Assert.True(startedWithRaw); Assert.False(vm.IncludeRawFrames); Assert.True(vm.RecordingIncludesRawFrames);
        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(stoppingWhileRecording);
    }

    [Fact]
    public void Report_DistinguishesRawExactSummaryFromPollOnlyAndReplayWindow()
    {
        var data = new SessionData(Start, Start.AddSeconds(1), true, 1, [], []) { IncludesRawFrames = true, FrameSummary = new(2, 20, 30, 20, 30, 30, 0) };
        Assert.Contains("p95 30", SessionReports.Build(data).Text);
        Assert.Contains("not opted in", SessionReports.Build(data with { IncludesRawFrames = false, FrameSummary = null }).Text);
        Assert.Contains("Reopen the full recording", SessionReports.Build(data with { FrameSummary = null }, windowSummaries: true).Text);
    }

    private string Write(string name, string? game, DateTime started)
    {
        Directory.CreateDirectory(_directory); var path = Path.Combine(_directory, name + ".stats-session.jsonl");
        File.WriteAllText(path, JsonSerializer.Serialize(new { type = "header", version = game is null ? 1 : 2, startedUtc = started, metrics = new[] { Cpu }, gameName = game }) + "\n");
        return path;
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
