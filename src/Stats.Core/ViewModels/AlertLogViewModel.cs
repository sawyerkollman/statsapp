using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Alerts;
using Stats.Core.Metrics;
using Stats.Core.Recording;
using Stats.Core.Sensors;

namespace Stats.Core.ViewModels;

public sealed partial class AlertRowViewModel : ObservableObject
{
    public AlertRowViewModel(AlertEvent evt) : this(new AlertRecord(Guid.NewGuid(), evt.RaisedAtLocal.ToUniversalTime(), evt, null, AlertState.Ongoing, null)) { }
    public AlertRowViewModel(AlertRecord record) => Replace(record);

    public AlertRecord Record { get; private set; } = null!;
    public MetricSeries? Context => Record.Context;
    public string MetricId => Record.Event.MetricId;
    public string State => Record.State.ToString();
    public bool IsComplete => Record.State != AlertState.Ongoing;
    public string RaisedAtText => Record.RaisedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string TimeText => Record.Event.RaisedAtLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string DateText => Record.RaisedUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string MetricText => Record.Event.DisplayName;
    public string PeakText => Format(Record.Event, Record.Event.PeakValue);
    public string ThresholdText => $"{(Record.Event.LowerIsWorse ? "≤" : "≥")} {Format(Record.Event, Record.Event.Threshold)}";
    public string DurationText => Record.State == AlertState.Interrupted ? "interrupted"
        : Record.Duration is { } duration ? FormatDuration(duration)
        : Record.State == AlertState.Ongoing ? "ongoing" : "unknown";

    public void Complete(TimeSpan duration) => Replace(Record with { Duration = duration, State = AlertState.Completed });

    internal void Replace(AlertRecord record)
    {
        Record = record;
        OnPropertyChanged(nameof(Record));
        OnPropertyChanged(nameof(Context));
        OnPropertyChanged(nameof(MetricId));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(RaisedAtText));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(MetricText));
        OnPropertyChanged(nameof(PeakText));
        OnPropertyChanged(nameof(ThresholdText));
        OnPropertyChanged(nameof(DurationText));
    }

    private static string Format(AlertEvent evt, float value) => ValueFormatter.Format(new MetricDefinition(evt.MetricId, evt.DisplayName, default, "", evt.Unit, evt.Format), value);
    private static string FormatDuration(TimeSpan duration)
    {
        var seconds = Math.Max(0, (int)Math.Round(duration.TotalSeconds));
        if (seconds < 60) return $"{seconds}s";
        var minutes = seconds / 60;
        if (minutes < 60) return $"{minutes}m {seconds % 60}s";
        return $"{minutes / 60}h {minutes % 60}m";
    }
}

public sealed partial class AlertLogViewModel : ObservableObject
{
    public const int MaxRows = AlertHistoryStore.MaxRecords;
    public ObservableCollection<AlertRowViewModel> Rows { get; } = new();
    [ObservableProperty] private string _historyStatus = "";
    public event Action? Changed;
    public event Action? PersistenceRequested;
    public event Action<MetricSeries>? OpenContextRequested;

    public void Add(AlertEvent evt) => Add(evt, evt.RaisedAtLocal.ToUniversalTime(), null);

    public void Add(AlertEvent evt, DateTime raisedUtc, MetricSeries? context)
    {
        var clipped = ClipBefore(context, out var before);
        Rows.Insert(0, new AlertRowViewModel(new AlertRecord(Guid.NewGuid(), ToUtc(raisedUtc), evt, null, AlertState.Ongoing, clipped) { PreContextCount = before }));
        Trim();
        Changed?.Invoke();
        PersistenceRequested?.Invoke();
    }

    public void Complete(string metricId, TimeSpan duration) => CompleteCore(Rows.FirstOrDefault(row => row.MetricId == metricId && !row.IsComplete), duration);
    public void Complete(string metricId, DateTime raisedAtLocal, TimeSpan duration) => CompleteCore(Rows.FirstOrDefault(row => row.MetricId == metricId && !row.IsComplete && row.Record.RaisedUtc == ToUtc(raisedAtLocal)), duration);

    public void UpdateOngoing(SensorSnapshot snapshot)
    {
        var changed = false;
        foreach (var row in Rows.Where(row => !row.IsComplete).ToArray())
        {
            var record = row.Record;
            var value = snapshot.Values.TryGetValue(record.Event.MetricId, out var current) && current is { } number && float.IsFinite(number) ? current : null;
            if (value is { } finite && (record.Event.LowerIsWorse ? finite < record.Event.PeakValue : finite > record.Event.PeakValue))
                record = record with { Event = record.Event with { PeakValue = finite } };

            if (record.Context is { } context && context.Values.Length - record.PreContextCount < 120 &&
                (context.TimesUtc.Length == 0 || context.TimesUtc[^1] != ToUtc(snapshot.TimestampUtc)))
            {
                record = record with { Context = new MetricSeries(context.Definition,
                    context.TimesUtc.Append(ToUtc(snapshot.TimestampUtc)).ToArray(), context.Values.Append(value).ToArray()) };
            }
            changed |= record != row.Record;
            row.Replace(record);
        }
        if (changed) Changed?.Invoke();
    }

    public IReadOnlyList<AlertRecord> ExportRecords() => Rows.Select(row => row.Record).ToArray();

    public void Load(IEnumerable<AlertRecord> records)
    {
        Rows.Clear();
        foreach (var record in records.Where(record => record.Id != Guid.Empty && record.Event is not null).OrderByDescending(record => record.RaisedUtc).Take(MaxRows))
            Rows.Add(new AlertRowViewModel(record.State == AlertState.Ongoing ? record with { State = AlertState.Interrupted } : record));
        Changed?.Invoke();
    }

    [RelayCommand]
    private void OpenContext(AlertRowViewModel? row)
    {
        if (row?.Context is { } context) OpenContextRequested?.Invoke(context);
    }

    [RelayCommand]
    private void Clear()
    {
        Rows.Clear();
        Changed?.Invoke();
        PersistenceRequested?.Invoke();
    }

    private void CompleteCore(AlertRowViewModel? row, TimeSpan duration)
    {
        if (row is null) return;
        row.Complete(duration);
        Changed?.Invoke();
        PersistenceRequested?.Invoke();
    }

    private void Trim()
    {
        while (Rows.Count > MaxRows) Rows.RemoveAt(Rows.Count - 1);
    }

    private static MetricSeries? ClipBefore(MetricSeries? context, out int before)
    {
        before = 0;
        if (context is null) return null;
        var length = Math.Min(context.TimesUtc.Length, context.Values.Length);
        var count = Math.Min(120, length);
        if (count == 0) return new MetricSeries(context.Definition, [], []);
        var start = length - count;
        before = count;
        return new MetricSeries(context.Definition, context.TimesUtc.Skip(start).Take(count).Select(ToUtc).ToArray(),
            context.Values.Skip(start).Take(count).Select(value => value is { } number && float.IsFinite(number) ? value : null).ToArray());
    }

    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
