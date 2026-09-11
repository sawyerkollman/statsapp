using Stats.Core.Sensors;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

/// <summary>With the "fans" scenario's composition, exercising the FansViewModel's SetAllAutoCommand and a
/// channel's IdentifyCommand, then ticking the controller, must produce only FakeFanControlBackend log entries —
/// no exception, and nothing resembling a real hardware/service call.</summary>
public class FanCommandRecordingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Stats.UiPreview.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (Exception) { /* best effort */ }
    }

    [Fact]
    public void SetAllAuto_And_Identify_ThenTick_OnlyLogFakeBackendCalls()
    {
        var c = PreviewComposition.Build("fans", _root, new[] { "on" });
        Assert.NotEmpty(c.FanBackend.Chans);

        var before = c.Commands.Entries.Count;

        c.Fans.SetAllAutoCommand.Execute(null);

        var firstChannelVm = c.Fans.Devices.SelectMany(g => g.Channels).First();
        var ex = Record.Exception(() => firstChannelVm.IdentifyCommand.Execute(null));
        Assert.Null(ex);

        // Drive one poller tick so Identify's pulse and the SetAllAuto release actually reach the backend —
        // exactly like the real FanController.Tick loop, just fed fixture values instead of a live snapshot.
        var values = new Dictionary<string, float?>();
        foreach (var d in c.Definitions) { c.Store.TryGet(d.Id, out var h); values[d.Id] = h?.Current; }
        var tickEx = Record.Exception(() => c.FanController.Tick(new SensorSnapshot(values, TimeSeries.FixedTimeUtc), TimeSeries.FixedTimeUtc));
        Assert.Null(tickEx);

        var newEntries = c.Commands.Entries.Skip(before).ToList();
        Assert.NotEmpty(newEntries);
        foreach (var entry in newEntries)
            Assert.True(entry.Contains("fan.SetPercent", StringComparison.Ordinal) || entry.Contains("fan.SetAuto", StringComparison.Ordinal) || entry.Contains("settings.save", StringComparison.Ordinal),
                $"Unexpected non-fake command entry: {entry}");
    }

    [Fact]
    public void NoChannelsSubstate_SetAllAuto_IsANoOp_NoException()
    {
        var c = PreviewComposition.Build("fans", _root, new[] { "no-channels" });
        Assert.Empty(c.FanBackend.Chans);
        var ex = Record.Exception(() => c.Fans.SetAllAutoCommand.Execute(null));
        Assert.Null(ex);
    }
}
