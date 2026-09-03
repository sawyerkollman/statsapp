using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Alerts;
using Stats.Core.Metrics;

namespace Stats.Core.ViewModels;

/// <summary>One alert log row. DurationText reads "ongoing" until the episode ends and <see cref="Complete"/>
/// finalizes it (e.g. "1m 12s").</summary>
public sealed partial class AlertRowViewModel : ObservableObject
{
    public AlertRowViewModel(AlertEvent evt)
    {
        MetricId = evt.MetricId;
        _timeText = evt.RaisedAtLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _metricText = evt.DisplayName;
        var def = new MetricDefinition(evt.MetricId, evt.DisplayName, default, "", evt.Unit);
        _peakText = ValueFormatter.Format(def, evt.PeakValue);
        var symbol = evt.LowerIsWorse ? "≤" : "≥";
        _thresholdText = $"{symbol} {ValueFormatter.Format(def, evt.Threshold)}";
    }

    public string MetricId { get; }
    [ObservableProperty] private string _timeText;
    [ObservableProperty] private string _metricText;
    [ObservableProperty] private string _peakText;
    [ObservableProperty] private string _thresholdText;
    [ObservableProperty] private string _durationText = "ongoing";

    /// <summary>True once <see cref="Complete"/> has finalized this row's duration — a metric can only have one
    /// ongoing row at a time, so <see cref="AlertLogViewModel.Complete"/> looks for the first row that isn't.</summary>
    public bool IsComplete { get; private set; }

    public void Complete(TimeSpan duration)
    {
        DurationText = FormatDuration(duration);
        IsComplete = true;
    }

    private static string FormatDuration(TimeSpan d)
    {
        var totalSeconds = Math.Max(0, (int)Math.Round(d.TotalSeconds));
        if (totalSeconds < 60) return $"{totalSeconds}s";
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        if (minutes < 60) return $"{minutes}m {seconds}s";
        var hours = minutes / 60;
        minutes %= 60;
        return $"{hours}h {minutes}m";
    }
}

/// <summary>Session-scoped alert log: newest first, capped at 200 rows. Populated by the composition root as
/// <see cref="Alerts.AlertEngine"/> raises events and ends episodes; independent of whether the Peaks window
/// (which displays it in its Alerts tab) is currently open.</summary>
public sealed partial class AlertLogViewModel : ObservableObject
{
    public const int MaxRows = 200;

    public ObservableCollection<AlertRowViewModel> Rows { get; } = new();

    public void Add(AlertEvent evt)
    {
        Rows.Insert(0, new AlertRowViewModel(evt));
        while (Rows.Count > MaxRows) Rows.RemoveAt(Rows.Count - 1);
    }

    /// <summary>Finalizes the most recent still-ongoing row for a metric — a no-op if none exists (e.g. the
    /// episode never actually raised, so no row was ever added).</summary>
    public void Complete(string metricId, TimeSpan duration)
    {
        var row = Rows.FirstOrDefault(r => r.MetricId == metricId && !r.IsComplete);
        row?.Complete(duration);
    }

    [RelayCommand]
    private void Clear() => Rows.Clear();
}
