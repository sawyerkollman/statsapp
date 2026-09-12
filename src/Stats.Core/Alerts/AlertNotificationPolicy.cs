namespace Stats.Core.Alerts;

/// <summary>Decides whether a raised AlertEvent becomes a Windows notification. Pure and clock-injected like
/// AlertEngine: the App passes the same nowUtc it passed to AlertEngine.Tick. Holds two timestamps and a
/// per-metric dictionary; nothing runs between alerts.</summary>
public sealed class AlertNotificationPolicy
{
    public static readonly TimeSpan GlobalCooldown = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PerMetricCooldown = TimeSpan.FromSeconds(60);

    private DateTime? _lastAcceptedUtc;
    private readonly Dictionary<string, DateTime> _lastAcceptedByMetricUtc = new();

    /// <summary>The settings/foreground gate, evaluated before any cool-down so a skipped notification never
    /// consumes one: notificationsEnabled &amp;&amp; !(skipWhenForeground &amp;&amp; dashboardIsForeground).</summary>
    public static bool ShouldNotify(bool notificationsEnabled, bool skipWhenForeground, bool dashboardIsForeground) =>
        notificationsEnabled && !(skipWhenForeground && dashboardIsForeground);

    /// <summary>True — and both stamps recorded — when nowUtc is at least GlobalCooldown after the last accepted
    /// notification (any metric) AND at least PerMetricCooldown after the last accepted one for metricId (or
    /// there was none). False records nothing. Boundaries are inclusive (exactly 10 s / 60 s later is accepted).</summary>
    public bool TryAccept(string metricId, DateTime nowUtc)
    {
        if (_lastAcceptedUtc is DateTime lastGlobal && nowUtc - lastGlobal < GlobalCooldown) return false;
        if (_lastAcceptedByMetricUtc.TryGetValue(metricId, out var lastForMetric) && nowUtc - lastForMetric < PerMetricCooldown) return false;

        _lastAcceptedUtc = nowUtc;
        _lastAcceptedByMetricUtc[metricId] = nowUtc;
        return true;
    }

    /// <summary>Forgets every stamp. Called alongside AlertEngine.Reset when alerts are switched off.</summary>
    public void Reset()
    {
        _lastAcceptedUtc = null;
        _lastAcceptedByMetricUtc.Clear();
    }
}
