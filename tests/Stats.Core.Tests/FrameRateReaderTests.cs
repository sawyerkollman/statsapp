using Stats.Core.Frames;

namespace Stats.Core.Tests;

public class FrameRateReaderTests
{
    private const string Header = "Application,ProcessID,FrameTime";

    private sealed class FakeSource : IFrameSource
    {
        public int Starts, Stops;
        public bool ThrowOnStart;
        public event Action<string>? LineReceived;
        public event Action<int, string>? Exited;
        public bool IsRunning { get; private set; }
        public void Start() { if (ThrowOnStart) throw new System.ComponentModel.Win32Exception(2, "not found"); Starts++; IsRunning = true; }
        public void Stop() { Stops++; IsRunning = false; }
        public void Dispose() => Stop();
        public void Emit(string line) => LineReceived?.Invoke(line);
        public void Die(int code, string stderr) { IsRunning = false; Exited?.Invoke(code, stderr); }
    }

    private sealed class Harness
    {
        public readonly FakeSource Source = new();
        public int? ForegroundPid = 1234;
        public DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        public readonly List<TimeSpan> Delays = new();
        public readonly List<TaskCompletionSource> Pending = new();
        public FrameRateReader Reader;
        public Harness(string? exePath = @"C:\fake\PresentMon.exe")
        {
            Reader = new FrameRateReader(exePath, () => Source, () => ForegroundPid, () => Now,
                (d, _) => { Delays.Add(d); var tcs = new TaskCompletionSource(); Pending.Add(tcs); return tcs.Task; });
        }
        public void EmitFrames(int pid, int count, double ms)
        {
            for (int i = 0; i < count; i++) Source.Emit($"game.exe,{pid},{ms}");
        }
        /// <summary>Let the most recent pending backoff delay elapse.</summary>
        public void ElapseBackoff() { var t = Pending[^1]; Pending.RemoveAt(Pending.Count - 1); t.SetResult(); }
    }

    [Fact]
    public void Discover_EmptyWhenNoExe()
    {
        var h = new Harness(exePath: null);
        Assert.Empty(h.Reader.Discover());
        Assert.False(h.Reader.IsAvailable);
    }

    [Fact]
    public void Discover_ThreeDefinitions_DoesNotStartProcess()
    {
        var h = new Harness();
        Assert.Equal(3, h.Reader.Discover().Count);
        Assert.Equal(0, h.Source.Starts);
    }

    [Fact]
    public void ShouldBeActive_AnyFpsIdInEitherList()
    {
        Assert.False(FrameRateReader.ShouldBeActive(new[] { "cpu.temp" }, Array.Empty<string>()));
        Assert.True(FrameRateReader.ShouldBeActive(new[] { "fps.avg" }, Array.Empty<string>()));
        Assert.True(FrameRateReader.ShouldBeActive(Array.Empty<string>(), new[] { "gpu.temp", "fps.frametime" }));
    }

    [Fact]
    public void SetActive_Idempotent_StartsOnceStopsOnce()
    {
        var h = new Harness();
        h.Reader.SetActive(true); h.Reader.SetActive(true);
        Assert.Equal(1, h.Source.Starts);
        Assert.True(h.Reader.IsActive);
        h.Reader.SetActive(false); h.Reader.SetActive(false);
        Assert.Equal(1, h.Source.Stops);
        Assert.False(h.Reader.IsActive);
    }

    [Fact]
    public void Read_Inactive_AllNull()
    {
        var h = new Harness();
        var s = h.Reader.Read();
        Assert.Equal(3, s.Values.Count);
        Assert.Contains("fps.avg", s.Values.Keys);
        Assert.Contains("fps.low1", s.Values.Keys);
        Assert.Contains("fps.frametime", s.Values.Keys);
        Assert.All(s.Values.Values, v => Assert.Null(v));
    }

    [Fact]
    public void Read_Active_ForegroundPidFrames_ProduceValues()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Emit(Header);
        h.EmitFrames(1234, 60, 16.6);
        var s = h.Reader.Read();
        Assert.Equal(60f, s.Values["fps.avg"]);
        Assert.Equal(16.6f, s.Values["fps.frametime"]!.Value, 2);
        Assert.Null(s.Values["fps.low1"]); // < 100 frames
    }

    [Fact]
    public void Read_ForegroundIsSomeoneElse_Null()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Emit(Header);
        h.EmitFrames(1234, 60, 16.6);
        h.ForegroundPid = 999;
        Assert.Null(h.Reader.Read().Values["fps.avg"]);
        h.ForegroundPid = null;
        Assert.Null(h.Reader.Read().Values["fps.avg"]);
    }

    [Fact]
    public void Window_ControlsFpsDenominator()
    {
        var h = new Harness();
        h.Reader.Window = TimeSpan.FromSeconds(2);
        h.Reader.SetActive(true);
        h.Source.Emit(Header);
        h.EmitFrames(1234, 60, 16.6);
        Assert.Equal(30f, h.Reader.Read().Values["fps.avg"]);
    }

    [Fact]
    public void SetActiveFalse_ClearsAggregator()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Emit(Header);
        h.EmitFrames(1234, 60, 16.6);
        h.Reader.SetActive(false);
        h.Reader.SetActive(true);
        Assert.Null(h.Reader.Read().Values["fps.avg"]); // old frames gone, header must arrive again
    }

    [Fact]
    public void Crash_RestartsWithBackoff_1_5_30_ThenGivesUp()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(1, "boom");
        Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, h.Delays);
        h.ElapseBackoff();
        Assert.Equal(2, h.Source.Starts);
        h.Source.Die(1, "boom");
        Assert.Equal(TimeSpan.FromSeconds(5), h.Delays[^1]);
        h.ElapseBackoff();
        h.Source.Die(1, "boom");
        Assert.Equal(TimeSpan.FromSeconds(30), h.Delays[^1]);
        h.ElapseBackoff();
        Assert.Equal(4, h.Source.Starts);
        h.Source.Die(1, "boom");
        Assert.Equal(3, h.Delays.Count);           // no fourth delay
        Assert.False(h.Reader.IsAvailable);
        Assert.Contains("gave up", h.Reader.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Crash_AfterSuccessfulFrames_ResetsBackoff()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(1, "boom");
        h.ElapseBackoff();                          // 1 s
        h.Source.Emit(Header);
        h.EmitFrames(1234, 20, 16.6);               // healthy again
        h.Source.Die(1, "boom");
        Assert.Equal(TimeSpan.FromSeconds(1), h.Delays[^1]);
    }

    [Fact]
    public void AccessDenied_ExitCode6_NoRestart_Unavailable()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(6, "error: failed to start trace session: access denied.");
        Assert.Empty(h.Delays);
        Assert.False(h.Reader.IsAvailable);
        Assert.Contains("access denied", h.Reader.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(h.Reader.Read().Values["fps.avg"]);
    }

    [Fact]
    public void AccessDenied_StderrText_NoRestart()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(1, "something something Access Denied");
        Assert.Empty(h.Delays);
        Assert.False(h.Reader.IsAvailable);
    }

    [Fact]
    public void Reactivate_AfterGivingUp_TriesAgain()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Die(6, "access denied");
        h.Reader.SetActive(false);
        h.Reader.SetActive(true);
        Assert.Equal(2, h.Source.Starts);
        Assert.True(h.Reader.IsAvailable);
    }

    [Fact]
    public void BadHeader_StopsSource_Unavailable()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Source.Emit("Application,Nothing,Useful");
        Assert.Equal(1, h.Source.Stops);
        Assert.False(h.Reader.IsAvailable);
        Assert.Contains("header", h.Reader.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartThrows_Unavailable_NoCrash()
    {
        var h = new Harness();
        h.Source.ThrowOnStart = true;
        h.Reader.SetActive(true);
        Assert.False(h.Reader.IsAvailable);
        Assert.NotNull(h.Reader.StatusMessage);
        Assert.Null(h.Reader.Read().Values["fps.avg"]);
    }

    [Fact]
    public void Dispose_StopsSource()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Reader.Dispose();
        Assert.True(h.Source.Stops >= 1);
    }

    [Fact]
    public void LateExitedEvent_AfterDeactivate_IsIgnored()
    {
        var h = new Harness();
        h.Reader.SetActive(true);
        h.Reader.SetActive(false);
        h.Source.Die(1, "late");
        Assert.Empty(h.Delays);
        Assert.Equal(1, h.Source.Starts);
    }
}
