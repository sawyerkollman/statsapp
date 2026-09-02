namespace Stats.Core.Sensors;

/// <summary>Immutable snapshot of SensorPoller's current failure episode, raised by <see cref="SensorPoller.HealthChanged"/>.
/// A "failed read" is either a thrown top-level Read() or a tick whose snapshot carries one or more
/// <see cref="SensorBackendFailure"/> entries. <see cref="FirstFailureLocalTime"/> is set once when the episode
/// starts (ConsecutiveFailures goes 0 → 1) and stays fixed for the rest of the episode; the other unhealthy
/// fields refresh on every subsequent failed tick. The episode resets to <see cref="Healthy"/> on the next fully
/// healthy read.</summary>
public sealed record SensorHealthState(
    bool IsHealthy,
    int ConsecutiveFailures,
    DateTime FirstFailureLocalTime,
    string LatestErrorFirstLine,
    IReadOnlyList<string> FailingBackends)
{
    public static readonly SensorHealthState Healthy = new(true, 0, default, "", Array.Empty<string>());
}
