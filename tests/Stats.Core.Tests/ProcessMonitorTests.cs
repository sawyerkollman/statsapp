using Stats.Core.Processes;
using Stats.Core.ViewModels;
using System.Collections.Concurrent;

namespace Stats.Core.Tests;

public class ProcessMonitorTests
{
    [Fact]
    public void CpuTracker_GroupsAndClampsDeltas()
    {
        var tracker = new ProcessCpuTracker(4);
        var at = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        tracker.Apply(new(new[] { Sample(1, "Chrome", 0), Sample(2, "chrome", 0) }, at, true, null));
        var usage = tracker.Apply(new(new[] { Sample(1, "Chrome", 10_000), Sample(2, "chrome", 100_000) }, at.AddSeconds(1), true, null));
        var group = Assert.Single(ProcessGrouping.Group(usage, new Dictionary<int, float> { [1] = 3, [2] = 99 }));
        Assert.Equal(2, group.PidCount);
        Assert.Equal(100, group.CpuPercent);
        Assert.Equal(100, group.GpuPercent);
    }

    [Fact]
    public void ProcessList_SortsTopTwelveAndProducesStableTsv()
    {
        var vm = new ProcessListViewModel();
        var groups = Enumerable.Range(0, 14).Select(i => new ProcessGroup($"p{i:D2}", 1, i, null, i * 1024L * 1024, 0)).ToList();
        vm.Apply(new(groups, false, new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(12, vm.Rows.Count);
        Assert.Equal("p13", vm.Rows[0].Name);
        var chrome = new ProcessRowViewModel(new("chrome", 14, null, null, 0, 0));
        Assert.Equal("chrome \u00d714", chrome.Name);
        Assert.Equal("\u2014", ProcessFormat.Percent(null));
        Assert.Equal("Sampling processes\u2026", ProcessListViewModel.SamplingText);
        Assert.Equal("Process\tProcesses\tCPU %\tGPU %\tWorking set MB\tPrivate MB", vm.ToTsv().Split('\n')[0]);
        Assert.False(vm.IsGpuAvailable);
    }

    [Fact]
    public void ProcessList_BeginSampling_ClearsStaleRowsAndGpuState()
    {
        var vm = new ProcessListViewModel();
        vm.Apply(new(new[] { new ProcessGroup("chrome", 1, 1, 1, 1, 1) }, true, DateTime.UtcNow));
        vm.BeginSampling();
        Assert.Empty(vm.Rows);
        Assert.Null(vm.Latest);
        Assert.False(vm.IsGpuAvailable);
        Assert.Equal(ProcessListViewModel.SamplingText, vm.StatusText);
    }

    [Fact]
    public void Sampler_FailurePublishesUnavailableThenRecovers()
    {
        var source = new FakeSource { Throw = true };
        using var sampler = new ProcessSampler(source, 4);
        Assert.True(sampler.SampleOnce().IsUnavailable);
        source.Throw = false;
        var snapshot = sampler.SampleOnce();
        Assert.False(snapshot.IsUnavailable);
        Assert.Single(snapshot.Groups);
    }

    [Fact]
    public void Resume_QueuesResetBehindInFlightSample_AndPrimesAgain()
    {
        using var source = new BlockingSource();
        using var sampler = new ProcessSampler(source, 4) { Interval = TimeSpan.FromSeconds(5) };
        var published = new ConcurrentQueue<ProcessListSnapshot>();
        sampler.SampleAvailable += published.Enqueue;
        sampler.Start();
        Assert.True(source.SampleEntered.Wait(TimeSpan.FromSeconds(1)));
        sampler.Pause();
        sampler.Resume();
        Assert.Equal(0, source.ResetCalls);
        source.ReleaseSample.Set();
        Assert.True(SpinWait.SpinUntil(() => source.ResetCalls == 1 && !published.IsEmpty, TimeSpan.FromSeconds(1)));
        Assert.True(sampler.Stop());
        Assert.True(published.TryDequeue(out var resumed));
        Assert.NotNull(resumed);
        Assert.Null(Assert.Single(resumed.Groups).CpuPercent);
    }

    [Theory]
    [InlineData("pid_1234_luid_0x0", true, 1234)]
    [InlineData("pid_x_luid_0x0", false, 0)]
    [InlineData("eng_3d", false, 0)]
    public void GpuEngineCounters_TryParsePid_RejectsMalformedNames(string name, bool expected, int pid)
    {
        Assert.Equal(expected, GpuEngineCounters.TryParsePid(name, out var actual));
        Assert.Equal(pid, actual);
    }

    private static ProcessSample Sample(int pid, string name, int milliseconds) => new(pid, name, TimeSpan.FromMilliseconds(milliseconds), 1, 1);

    private sealed class FakeSource : IProcessSource
    {
        public bool Throw { get; set; }
        public string Name => "fake";
        public ProcessSourceSnapshot Sample()
        {
            if (Throw) throw new InvalidOperationException("denied");
            return new(new[] { ProcessMonitorTests.Sample(1, "fake", 1) }, DateTime.UtcNow, false, null);
        }
        public void Reset() { }
        public void Dispose() { }
    }

    private sealed class BlockingSource : IProcessSource
    {
        private int _samples;
        private int _resetCalls;
        public ManualResetEventSlim SampleEntered { get; } = new();
        public ManualResetEventSlim ReleaseSample { get; } = new();
        public int ResetCalls => Volatile.Read(ref _resetCalls);
        public string Name => "blocking";
        public ProcessSourceSnapshot Sample()
        {
            if (Interlocked.Increment(ref _samples) == 1) { SampleEntered.Set(); ReleaseSample.Wait(); }
            return new(new[] { ProcessMonitorTests.Sample(1, "fake", _samples) }, DateTime.UtcNow, false, null);
        }
        public void Reset() => Interlocked.Increment(ref _resetCalls);
        public void Dispose() { SampleEntered.Dispose(); ReleaseSample.Dispose(); }
    }
}
