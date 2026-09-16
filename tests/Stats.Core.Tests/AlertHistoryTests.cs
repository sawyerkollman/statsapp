using Stats.Core.Alerts;
using Stats.Core.Metrics;
using Stats.Core.Recording;
using Stats.Core.Sensors;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public sealed class AlertHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Stats.AlertHistoryTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Store_RoundTripsAtomically_AndInterruptsOngoingRecords()
    {
        var store = new AlertHistoryStore(_directory);
        var record = Record(AlertState.Ongoing);

        store.Save([record]);
        var loaded = Assert.Single(store.Load());

        Assert.Equal(record.Id, loaded.Id);
        Assert.Equal(AlertState.Interrupted, loaded.State);
        Assert.Null(store.Error);
        Assert.False(File.Exists(Path.Combine(_directory, "alerts.json.tmp")));
    }

    [Fact]
    public void Store_ContainsCorruptFileError()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "alerts.json"), "not json");

        var store = new AlertHistoryStore(_directory);

        Assert.Empty(store.Load());
        Assert.StartsWith("Alert history could not be loaded:", store.Error);
    }

    [Fact]
    public void Complete_UsesMetricAndRaisedIdentity()
    {
        var log = new AlertLogViewModel();
        var first = DateTime.SpecifyKind(new DateTime(2026, 9, 15, 10, 0, 0), DateTimeKind.Local);
        var second = first.AddMinutes(1);
        log.Add(Event(first), first.ToUniversalTime(), null);
        log.Add(Event(second), second.ToUniversalTime(), null);

        log.Complete("fps.avg", first, TimeSpan.FromSeconds(8));

        Assert.Equal(AlertState.Ongoing, log.Rows[0].Record.State);
        Assert.Equal(AlertState.Completed, log.Rows[1].Record.State);
    }

    [Fact]
    public void OngoingContext_Keeps120BeforeAnd120AfterWithoutDuplicateTimestamp_AndTracksWorstPeak()
    {
        var definition = new MetricDefinition("fps.avg", "FPS", default, "", "fps");
        var start = DateTime.UtcNow.AddMinutes(-3);
        var preTimes = Enumerable.Range(0, 121).Select(i => start.AddSeconds(i)).ToArray();
        var context = new MetricSeries(definition, preTimes, Enumerable.Range(0, 121).Select(i => (float?)i).ToArray());
        var log = new AlertLogViewModel();
        log.Add(Event(start.AddSeconds(120), lowerIsWorse: true), start.AddSeconds(120), context);

        log.UpdateOngoing(Snapshot(start.AddSeconds(120), 15)); // already present: do not duplicate
        for (var i = 1; i <= 121; i++) log.UpdateOngoing(Snapshot(start.AddSeconds(120 + i), i == 121 ? 1 : null));

        var record = Assert.Single(log.ExportRecords());
        Assert.Equal(120, record.PreContextCount);
        Assert.Equal(240, record.Context!.Values.Length);
        Assert.Equal(240, record.Context.TimesUtc.Distinct().Count());
        Assert.Contains(record.Context.Values, value => value is null);
        Assert.Equal(1, record.Event.PeakValue); // lower-is-worse continues after context is full
    }

    [Fact]
    public void Store_SanitizesOversizedAndNonFiniteContext()
    {
        var definition = new MetricDefinition("fps.avg", "FPS", default, "", "fps");
        var times = Enumerable.Range(0, 300).Select(i => DateTime.UtcNow.AddSeconds(i)).ToArray();
        var values = Enumerable.Range(0, 300).Select(i => i == 10 ? (float?)float.NaN : i).ToArray();
        var record = Record(AlertState.Completed) with { Context = new MetricSeries(definition, times, values), PreContextCount = 150 };
        var store = new AlertHistoryStore(_directory);

        store.Save([record]);
        var loaded = Assert.Single(store.Load());

        Assert.Equal(240, loaded.Context!.Values.Length);
        Assert.Equal(120, loaded.PreContextCount);
        Assert.Equal(times[30], loaded.Context.TimesUtc[0]);
        Assert.Equal(30, loaded.Context.Values[0]);
    }

    [Fact]
    public void Clear_RaisesChangedAndPersistsEmptyHistory()
    {
        var store = new AlertHistoryStore(_directory);
        var log = new AlertLogViewModel();
        var changes = 0;
        var saves = 0;
        log.Changed += () => { changes++; store.Save(log.ExportRecords()); };
        log.PersistenceRequested += () => saves++;
        log.Add(Event(DateTime.Now), DateTime.UtcNow, null);

        log.ClearCommand.Execute(null);

        Assert.True(changes >= 2);
        Assert.Equal(2, saves);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void LoadedInterruptedAlertDoesNotDisplayOngoing_AndInvalidContextDoesNotLoseRow()
    {
        var definition = new MetricDefinition("fps.avg", "FPS", default, "", "fps");
        var record = Record(AlertState.Ongoing) with { Context = new MetricSeries(definition,
            [DateTime.UtcNow, DateTime.UtcNow.AddDays(-1)], [20, 21]) };
        var store = new AlertHistoryStore(_directory);
        store.Save([record]);
        var log = new AlertLogViewModel();
        log.Load(store.Load());
        var row = Assert.Single(log.Rows);
        Assert.Equal("interrupted", row.DurationText);
        Assert.Null(row.Context);
    }

    [Fact]
    public void StoreBoundsNewestRows_AndSaveErrorPreservesPriorFile()
    {
        var store = new AlertHistoryStore(_directory);
        var records = Enumerable.Range(0, 205).Select(i => Record(AlertState.Completed) with
            { RaisedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(i) }).ToArray();
        store.Save(records);
        Assert.Equal(200, store.Load().Count);
        Assert.Equal(records[^1].Id, store.Load()[0].Id);
        var prior = File.ReadAllText(Path.Combine(_directory, "alerts.json"));
        Directory.CreateDirectory(Path.Combine(_directory, "alerts.json.tmp"));
        store.Save([]);
        Assert.NotNull(store.Error);
        Assert.Equal(prior, File.ReadAllText(Path.Combine(_directory, "alerts.json")));
    }

    private static SensorSnapshot Snapshot(DateTime timestampUtc, float? value) => new(new Dictionary<string, float?> { ["fps.avg"] = value }, timestampUtc);
    private static AlertEvent Event(DateTime time, bool lowerIsWorse = false) => new(time, "fps.avg", "FPS", "fps", 20, 30, lowerIsWorse);
    private static AlertRecord Record(AlertState state) => new(Guid.NewGuid(), DateTime.UtcNow, Event(DateTime.Now), null, state, null);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
