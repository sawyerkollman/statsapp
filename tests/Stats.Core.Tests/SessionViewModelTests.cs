using Stats.Core.Recording;
using Stats.Core.Sensors;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.ViewModels;
using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class SessionViewModelTests
{
    [Fact]
    public void StartWithoutMetrics_IsVisibleValidationError_WithoutStartingRecorder()
    {
        var vm = new SessionViewModel(new SessionRecorder(Path.GetTempPath()), () => Array.Empty<Stats.Core.Metrics.MetricDefinition>());
        vm.StartCommand.Execute(null);

        Assert.False(vm.IsRecording);
        Assert.Contains("Select at least one", vm.Error);
    }

    [Fact]
    public void SeriesRow_UsesMetricFormatter_ForSummaryText()
    {
        var definition = new MetricDefinition("cpu", "CPU", default, "", "°C", "F1");
        var row = new SessionSeriesViewModel(new(definition, [], []), new("cpu", 1.26f, 2.5f, 3.75f));
        Assert.Equal("1.3 °C", row.MinText);
        Assert.Equal("2.5 °C", row.AverageText);
        Assert.Equal("3.8 °C", row.MaxText);
    }

    [Fact]
    public async Task Stop_LoadsRecordedSummaries()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var definition = new MetricDefinition("cpu", "CPU", default, "", "%", "F1");
        using var recorder = new SessionRecorder(directory);
        var vm = new SessionViewModel(recorder, () => [definition], () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        vm.StartCommand.Execute(null);
        recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = 12.5f }, new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)));
        vm.StopCommand.Execute(null);
        await ((IAsyncRelayCommand)vm.StopCommand).ExecutionTask!;
        Assert.Equal("12.5 %", Assert.Single(vm.Rows).AverageText);
    }

    [Fact]
    public async Task Refresh_DoesNotReplaceOpenedSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var definition = new MetricDefinition("cpu", "CPU", default, "", "%");
        using var recorder = new SessionRecorder(directory);
        recorder.Start([definition], DateTime.UtcNow);
        await recorder.StopAsync();
        var path = recorder.FilePath!;
        var vm = new SessionViewModel(recorder, () => []);
        Assert.True(vm.Open(path));
        vm.RefreshRecorderState();
        Assert.Equal(path, vm.FilePath);
    }

    [Fact]
    public async Task WriterFailure_BlocksOpenUntilStopThenAllowsRetry()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var definition = new MetricDefinition("cpu", "CPU", default, "", "%");
        using var recorder = new SessionRecorder(directory);
        var vm = new SessionViewModel(recorder, () => [definition], () => start);
        vm.StartCommand.Execute(null);
        recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = 42 }, start));
        recorder.Record(new(new Dictionary<string, float?>(), start.AddSeconds(-1)));
        vm.RefreshRecorderState();
        Assert.False(vm.IsRecording);
        Assert.NotEmpty(vm.Error);
        Assert.False(vm.CanOpenOrExport);
        Assert.False(vm.Open(recorder.FilePath!));
        Assert.False(vm.Export(Path.Combine(directory, "export.csv")));
        vm.StopCommand.Execute(null);
        await ((IAsyncRelayCommand)vm.StopCommand).ExecutionTask!;
        Assert.True(vm.CanOpenOrExport);
        Assert.True(vm.CanStart);
        vm.StartCommand.Execute(null);
        Assert.True(vm.IsRecording);
    }
}
