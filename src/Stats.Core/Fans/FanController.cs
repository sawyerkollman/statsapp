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
        public bool FailedOver; // fail-safe flipped this channel to Auto: keep reporting WriteFailed until the user acts
        public FanChannelStatus Status = FanChannelStatus.Idle;
        public float? Rpm, Percent, Target, SourceTemp;
    }

    private readonly IFanControlBackend _backend;
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly IFanArmedMarker _marker;
    private readonly object _gate = new();
    private readonly Dictionary<string, Runtime> _rt = new();
    private DateTime? _firstTick;
    private bool _pendingSave;
    private bool _armed;

    public FanController(IFanControlBackend backend, AppSettings settings, Action saveSettings, IFanArmedMarker? marker = null)
    {
        _backend = backend;
        _settings = settings;
        _save = saveSettings;
        _marker = marker ?? new NullFanArmedMarker();
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
    public void SetSources(string id, IEnumerable<string> metricIds) => Mutate(id, p =>
    {
        p.SourceMetricIds = metricIds.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct().ToList();
        p.SourceMetricId = p.SourceMetricIds.FirstOrDefault();
    });
    public void SetSource(string id, string? metricId) => SetSources(id, metricId is null ? Array.Empty<string>() : new[] { metricId });
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
            var rt = Rt(id);
            rt.LastSourceUsed = null; // re-evaluate immediately on next tick
            rt.Failures = 0;          // the user acted on this channel: give it a fresh retry budget
            rt.FailedOver = false;    // …and stop reporting the old fail-safe flip
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
                // One misbehaving channel (a control that vanished, a driver that throws on read) must never
                // abort the loop and leave the remaining channels un-serviced.
                try
                {
                    rt.Rpm = Value(snapshot, ch.RpmMetricId);
                    float? reported = Value(snapshot, ch.PercentMetricId);

                    if (!_settings.FanControlEnabled)
                    {
                        ReleaseLocked(ch.Id, rt, FanChannelStatus.Idle);
                        rt.Percent = reported; rt.Target = null; rt.SourceTemp = null;
                        continue;
                    }

                    if (!(ch.MaxPercent > 0f))
                    {
                        // A control that reports no usable headroom can only be driven to 0 % — which would stop the
                        // fan. Leave it to the device and surface the problem instead.
                        ReleaseLocked(ch.Id, rt, FanChannelStatus.WriteFailed);
                        rt.Percent = reported; rt.SourceTemp = null;
                        continue;
                    }

                    _settings.FanChannels.TryGetValue(ch.Id, out var pref);
                    var mode = pref?.Mode ?? FanMode.Auto;
                    float? target = null;
                    rt.SourceTemp = null;

                    switch (mode)
                    {
                        case FanMode.Auto:
                            ReleaseLocked(ch.Id, rt, rt.FailedOver ? FanChannelStatus.WriteFailed : FanChannelStatus.Idle);
                            break;

                        case FanMode.Manual:
                            target = pref!.ManualPercent;
                            break;

                        case FanMode.Curve:
                            float? src = MaxSource(snapshot, pref!);
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
                                ReleaseLocked(ch.Id, rt, FanChannelStatus.SourceUnavailable);
                                rt.LastSourceUsed = null;
                                break;
                            }
                            if (rt.LastSourceUsed is not float useTemp)
                            {
                                if (rt.Status != FanChannelStatus.WriteFailed) rt.Status = FanChannelStatus.WaitingForSource;
                                break; // no value yet (or holding through a short gap): keep current output
                            }
                            if (!FanCurve.TryCreate(pref!.Points, out var curve)) curve = FanCurve.Default;
                            target = curve!.Evaluate(useTemp);
                            break;
                    }

                    if (target is float want)
                    {
                        float min = ch.MinPercent;
                        if (ch.Name.Contains("pump", StringComparison.OrdinalIgnoreCase)) min = MathF.Max(min, PumpFloorPercent);
                        min = MathF.Min(min, ch.MaxPercent); // a floor above the ceiling would make Math.Clamp throw
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
                catch (Exception ex)
                {
                    Trace.WriteLine($"[Stats.FanController] tick for {ch.Id} failed: {ex}");
                    rt.Status = FanChannelStatus.WriteFailed;
                }
            }

            UpdateMarkerLocked();

            // Rare path (the fail-safe mode flip). Saving inside _gate keeps JsonSerializer from enumerating
            // FanChannels while the UI thread's Mutate — which also takes _gate — modifies the dictionary.
            if (_pendingSave)
            {
                _pendingSave = false;
                _save();
            }
        }
    }

    /// <summary>Clears the fans-armed marker once no runtime channel is still under software control.
    /// A failed release keeps a channel InSoftware, which correctly keeps the marker around.</summary>
    private void UpdateMarkerLocked()
    {
        if (_armed && !_rt.Values.Any(r => r.InSoftware)) { _marker.Clear(); _armed = false; }
    }

    /// <summary>Call once at startup, before the poller starts. If the marker from a previous run exists, every
    /// backend channel is handed back to device control (runtime state is gone) and the marker cleared.</summary>
    public bool RecoverFromUncleanShutdown()
    {
        if (!_marker.Exists()) return false;
        lock (_gate)
        {
            foreach (var ch in _backend.Channels)
            {
                try { _backend.SetAuto(ch.Id); } catch (Exception ex) { Trace.WriteLine($"[Stats.FanController] recovery SetAuto {ch.Id} failed: {ex.Message}"); }
            }
            _marker.Clear(); _armed = false;
        }
        Trace.WriteLine("[Stats.FanController] previous run did not shut down cleanly; all fans returned to device control");
        return true;
    }

    private void WriteLocked(FanChannel ch, Runtime rt, float percent, FanChannelPref pref)
    {
        // LHM's SetSoftware may switch the control into software mode before the hardware write itself fails
        // (a "partial write"), so mark InSoftware before the call — otherwise a failed write would leave the
        // channel pinned at whatever PWM it landed on while we believe it's still under device control.
        rt.InSoftware = true;
        if (!_armed) { _marker.Set(); _armed = true; }
        try
        {
            _backend.SetPercent(ch.Id, percent);
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
                rt.FailedOver = true; // keep the Auto branch reporting WriteFailed until the user changes something
                _pendingSave = true;  // persist the fail-safe mode flip at the end of the tick, still under _gate
                ReleaseLocked(ch.Id, rt, FanChannelStatus.WriteFailed);
                Trace.WriteLine($"[Stats.FanController] {ch.Id} set to Auto after repeated write failures");
            }
        }
    }

    /// <summary>
    /// Hands the channel back to device control if we were driving it. Only clears InSoftware/LastWritten when
    /// SetAuto actually succeeds — if it throws, the channel is still pinned in software, so we keep tracking
    /// it (and keep reporting WriteFailed) so the next Tick or RestoreAll retries the release. One attempt per call.
    /// </summary>
    private void ReleaseLocked(string id, Runtime rt, FanChannelStatus status)
    {
        if (rt.InSoftware)
        {
            try
            {
                _backend.SetAuto(id);
                rt.InSoftware = false;
                rt.LastWritten = null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Stats.FanController] SetAuto {id} failed: {ex.Message}");
                rt.Status = FanChannelStatus.WriteFailed;
                rt.Target = null;
                return; // InSoftware stays true — retry on the next Tick/RestoreAll
            }
        }
        rt.Target = null;
        rt.Status = status;
    }

    /// <summary>Release every channel we ever wrote to, including ones no longer reported by the backend
    /// (a driven channel that vanished from discovery must still be handed back). Call after the poller is
    /// stopped (or from Tick's thread). Safe to call repeatedly — a failed release is retried on the next call.</summary>
    public void RestoreAll()
    {
        lock (_gate)
        {
            foreach (var (id, rt) in _rt)
            {
                if (rt.InSoftware) ReleaseLocked(id, rt, FanChannelStatus.Idle);
            }
            UpdateMarkerLocked();
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
                    pref?.Points ?? FanCurve.DefaultPoints,
                    (IReadOnlyList<string>?)pref?.SourceMetricIds ?? Array.Empty<string>()));
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

    /// <summary>Max over every configured source id that has a value in this snapshot; null if none do.</summary>
    private static float? MaxSource(SensorSnapshot s, FanChannelPref pref)
    {
        float? best = null;
        var ids = pref.SourceMetricIds.Count > 0 ? pref.SourceMetricIds : (pref.SourceMetricId is null ? new List<string>() : new List<string> { pref.SourceMetricId });
        foreach (var id in ids)
            if (Value(s, id) is float v && (best is null || v > best)) best = v;
        return best;
    }
}
