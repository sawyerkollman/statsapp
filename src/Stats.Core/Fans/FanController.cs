using System.Diagnostics;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Fans;

/// <summary>
/// The fan control loop. Tick() runs on the poller thread after each snapshot (the only thread that may touch
/// the backend). UI-thread setters only change desired state (settings) under _gate; the next Tick applies it.
/// </summary>
public sealed class FanController
{
    public const float HysteresisC = 2f;
    public const float MaxStepPerTick = 10f;
    public const float PumpFloorPercent = 50f;
    public const int MaxWriteFailures = 3;
    public static readonly TimeSpan SourceStaleAfter = TimeSpan.FromSeconds(10);

    private sealed class Runtime
    {
        public bool InSoftware;
        public float? LastWritten;
        public float? LastSourceUsed;
        public DateTime? LastSourceSeen;
        public int Failures;
        public FanChannelStatus Status = FanChannelStatus.Idle;
        public float? Rpm, Percent, Target, SourceTemp;
    }

    private readonly IFanControlBackend _backend;
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly object _gate = new();
    private readonly Dictionary<string, Runtime> _rt = new();
    private DateTime? _firstTick;

    public FanController(IFanControlBackend backend, AppSettings settings, Action saveSettings)
    {
        _backend = backend;
        _settings = settings;
        _save = saveSettings;
    }

    public IReadOnlyList<FanChannel> Channels => _backend.Channels;

    public bool Enabled
    {
        get { lock (_gate) return _settings.FanControlEnabled; }
        set { lock (_gate) { if (_settings.FanControlEnabled == value) return; _settings.FanControlEnabled = value; } _save(); }
    }

    // ---- desired-state setters (any thread) ----

    public void SetMode(string id, FanMode mode) => Mutate(id, p => p.Mode = mode);
    public void SetManualPercent(string id, float percent) => Mutate(id, p => p.ManualPercent = Math.Clamp(percent, 0f, 100f));
    public void SetSource(string id, string? metricId) => Mutate(id, p => p.SourceMetricId = string.IsNullOrWhiteSpace(metricId) ? null : metricId);
    public void SetName(string id, string? name) => Mutate(id, p => p.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim());
    public void ResetCurve(string id) => Mutate(id, p => p.Points = FanCurve.DefaultPoints.ToList());

    public bool TrySetPoints(string id, IEnumerable<FanPoint> points)
    {
        if (!FanCurve.TryCreate(points, out var curve)) return false;
        Mutate(id, p => p.Points = curve!.Points.ToList());
        return true;
    }

    private void Mutate(string id, Action<FanChannelPref> change)
    {
        lock (_gate)
        {
            if (!_settings.FanChannels.TryGetValue(id, out var pref))
                _settings.FanChannels[id] = pref = new FanChannelPref();
            change(pref);
            Rt(id).LastSourceUsed = null; // re-evaluate immediately on next tick
        }
        _save();
    }

    // ---- loop (poll thread) ----

    public void Tick(SensorSnapshot snapshot, DateTime nowUtc)
    {
        lock (_gate)
        {
            _firstTick ??= nowUtc;
            foreach (var ch in _backend.Channels)
            {
                var rt = Rt(ch.Id);
                rt.Rpm = Value(snapshot, ch.RpmMetricId);
                float? reported = Value(snapshot, ch.PercentMetricId);

                if (!_settings.FanControlEnabled)
                {
                    ReleaseLocked(ch, rt, FanChannelStatus.Idle);
                    rt.Percent = reported; rt.Target = null; rt.SourceTemp = null;
                    continue;
                }

                _settings.FanChannels.TryGetValue(ch.Id, out var pref);
                var mode = pref?.Mode ?? FanMode.Auto;
                float? target = null;
                rt.SourceTemp = null;

                switch (mode)
                {
                    case FanMode.Auto:
                        ReleaseLocked(ch, rt, FanChannelStatus.Idle);
                        break;

                    case FanMode.Manual:
                        target = pref!.ManualPercent;
                        break;

                    case FanMode.Curve:
                        float? src = Value(snapshot, pref!.SourceMetricId);
                        rt.SourceTemp = src;
                        if (src is float t)
                        {
                            rt.LastSourceSeen = nowUtc;
                            if (rt.LastSourceUsed is not float used || MathF.Abs(t - used) >= HysteresisC)
                                rt.LastSourceUsed = t;
                        }
                        var lastSeen = rt.LastSourceSeen ?? _firstTick.Value;
                        if (src is null && nowUtc - lastSeen > SourceStaleAfter)
                        {
                            ReleaseLocked(ch, rt, FanChannelStatus.SourceUnavailable);
                            rt.LastSourceUsed = null;
                            break;
                        }
                        if (rt.LastSourceUsed is not float useTemp)
                        {
                            if (rt.Status != FanChannelStatus.WriteFailed) rt.Status = FanChannelStatus.WaitingForSource;
                            break; // no value yet (or holding through a short gap): keep current output
                        }
                        if (!FanCurve.TryCreate(pref.Points, out var curve)) curve = FanCurve.Default;
                        target = curve!.Evaluate(useTemp);
                        break;
                }

                if (target is float want)
                {
                    float min = ch.MinPercent;
                    if (ch.Name.Contains("pump", StringComparison.OrdinalIgnoreCase)) min = MathF.Max(min, PumpFloorPercent);
                    want = Math.Clamp(want, min, ch.MaxPercent);
                    if (rt.LastWritten is float last)
                        want = last + Math.Clamp(want - last, -MaxStepPerTick, MaxStepPerTick);
                    want = MathF.Round(want);
                    rt.Target = want;
                    if (!rt.InSoftware || rt.LastWritten != want) WriteLocked(ch, rt, want, pref!);
                    else if (rt.Status != FanChannelStatus.WriteFailed) rt.Status = FanChannelStatus.Active;
                }
                else if (mode != FanMode.Curve || rt.Status == FanChannelStatus.SourceUnavailable || rt.Status == FanChannelStatus.Idle)
                {
                    rt.Target = null;
                }

                rt.Percent = reported ?? (rt.InSoftware ? rt.LastWritten : null);
            }
        }
    }

    private void WriteLocked(FanChannel ch, Runtime rt, float percent, FanChannelPref pref)
    {
        try
        {
            _backend.SetPercent(ch.Id, percent);
            rt.InSoftware = true;
            rt.LastWritten = percent;
            rt.Failures = 0;
            rt.Status = FanChannelStatus.Active;
        }
        catch (Exception ex)
        {
            rt.Failures++;
            rt.Status = FanChannelStatus.WriteFailed;
            Trace.WriteLine($"[Stats.FanController] write {ch.Id}={percent} failed ({rt.Failures}/{MaxWriteFailures}): {ex.Message}");
            if (rt.Failures >= MaxWriteFailures)
            {
                pref.Mode = FanMode.Auto;
                ReleaseLocked(ch, rt, FanChannelStatus.WriteFailed);
                Trace.WriteLine($"[Stats.FanController] {ch.Id} set to Auto after repeated write failures");
            }
        }
    }

    /// <summary>Hands the channel back to device control if we were driving it. Always sets the status.</summary>
    private void ReleaseLocked(FanChannel ch, Runtime rt, FanChannelStatus status)
    {
        if (rt.InSoftware)
        {
            try { _backend.SetAuto(ch.Id); }
            catch (Exception ex) { Trace.WriteLine($"[Stats.FanController] SetAuto {ch.Id} failed: {ex.Message}"); }
            rt.InSoftware = false;
            rt.LastWritten = null;
        }
        rt.Target = null;
        rt.Status = status;
    }

    /// <summary>Return every channel we ever wrote to device control. Call after the poller is stopped (or from Tick's thread).</summary>
    public void RestoreAll()
    {
        lock (_gate)
        {
            foreach (var ch in _backend.Channels)
                ReleaseLocked(ch, Rt(ch.Id), FanChannelStatus.Idle);
        }
    }

    public IReadOnlyList<FanChannelView> Views()
    {
        lock (_gate)
        {
            var list = new List<FanChannelView>(_backend.Channels.Count);
            foreach (var ch in _backend.Channels)
            {
                var rt = Rt(ch.Id);
                _settings.FanChannels.TryGetValue(ch.Id, out var pref);
                list.Add(new FanChannelView(
                    ch.Id,
                    string.IsNullOrWhiteSpace(pref?.Name) ? ch.Name : pref!.Name!,
                    ch.Device,
                    pref?.Mode ?? FanMode.Auto,
                    rt.Rpm, rt.Percent, rt.Target, rt.SourceTemp, rt.Status,
                    ch.MinPercent, ch.MaxPercent,
                    pref?.SourceMetricId,
                    pref?.ManualPercent ?? 50f,
                    pref?.Points ?? FanCurve.DefaultPoints));
            }
            return list;
        }
    }

    private Runtime Rt(string id)
    {
        if (!_rt.TryGetValue(id, out var rt)) _rt[id] = rt = new Runtime();
        return rt;
    }

    private static float? Value(SensorSnapshot s, string? id) =>
        id is not null && s.Values.TryGetValue(id, out var v) && v is float f && !float.IsNaN(f) ? f : null;
}
