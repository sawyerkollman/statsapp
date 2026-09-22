using Stats.Core.Recording;
namespace Stats.Core.Alerts;
public enum AlertState { Ongoing, Completed, Interrupted }
public sealed record AlertRecord(Guid Id, DateTime RaisedUtc, AlertEvent Event, TimeSpan? Duration, AlertState State, MetricSeries? Context)
{
    /// <summary>Number of context samples captured before the alert was raised.</summary>
    public int PreContextCount { get; init; }
}
