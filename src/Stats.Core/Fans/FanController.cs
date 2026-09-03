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
    public static readonly TimeSpan IdentifyDuration = TimeSpan.FromSeconds(2);

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
        /// <summary>Set by <see cref="Identify"/>; while <c>nowUtc</c> is before this, Tick forces the channel to
        /// MaxPercent regardless of its own mode. Never touches prefs/ActiveFanProfile.</summary>
        public DateTime? IdentifyUntil;
    }

    private readonly IFanControlBackend _backend;
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly IFanArmedMarker _marker;
    /// <summary>The settings graph's own lock (<see cref="AppSettings.SyncRoot"/>): whoever serializes the
    /// settings holds it too, so a poll-thread channel/profile change can never race a save.</summary>
    private readonly object _gate;
    private readonly Dictionary<string, Runtime> _rt = new();
    private DateTime? _firstTick;
    private bool _pendingSave;
    private bool _armed;

    public FanController(IFanControlBackend backend, AppSettings settings, Action saveSettings, IFanArmedMarker? marker = null)
    {
        _backend = backend;
        _settings = settings;
        _gate = settings.SyncRoot;
        _save = saveSettings;
        _marker = marker ?? new NullFanArmedMarker();
    }

    public IReadOnlyList<FanChannel> Channels => _backend.Channels;

    public string? ActiveProfile { get { lock (_gate) return _settings.ActiveFanProfile; } }

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

    /// <summary>Pulses the channel to <see cref="FanChannel.MaxPercent"/> for <see cref="IdentifyDuration"/> so the
    /// user can see which physical fan a row corresponds to. Deliberately NOT routed through <see cref="Mutate"/>:
    /// it never touches <see cref="FanChannelPref"/> or <see cref="AppSettings.ActiveFanProfile"/>, and it never
    /// writes to the backend itself — only <see cref="Tick"/> (the poller thread) does that. A no-op while the
    /// master switch is off: Tick's disabled branch releases everything and never looks at IdentifyUntil.</summary>
    public void Identify(string id, DateTime nowUtc)
    {
        lock (_gate) { Rt(id).IdentifyUntil = nowUtc + IdentifyDuration; }
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
            _settings.ActiveFanProfile = null;
        }
        _save();
    }

    private static FanChannelPref Clone(FanChannelPref p) => new()
    {
        Mode = p.Mode, ManualPercent = p.ManualPercent, SourceMetricId = p.SourceMetricId,
        SourceMetricIds = p.SourceMetricIds.ToList(), Points = p.Points.ToList(), Name = p.Name,
    };

    public FanProfile SnapshotProfile(string name)
    {
        lock (_gate)
        {
            var prof = new FanProfile { Name = name };
            foreach (var (id, p) in _settings.FanChannels) prof.Channels[id] = Clone(p);
            return prof;
        }
    }

    /// <summary>Replace every channel's desired state with the profile's (channels absent from the profile → Auto,
    /// names preserved). deferSave: poll-thread callers — the save runs at the end of the next Tick under _gate.
    /// resetFailures: only a user-initiated switch counts as "the user acted on this channel" and clears the
    /// write fail-safe (CLAUDE.md rule 6). An automatic switch (game mode) passes false so a control that is
    /// failing mid-write stays parked in Auto with its WriteFailed status, costing one probe write per
    /// transition instead of three. LastSourceUsed is cleared either way — the curve must re-evaluate at once.</summary>
    public void ApplyProfile(FanProfile profile, bool deferSave = false, bool resetFailures = true)
    {
        lock (_gate)
        {
            var names = _settings.FanChannels.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
            _settings.FanChannels.Clear();
            foreach (var (id, p) in profile.Channels) _settings.FanChannels[id] = Clone(p);
            foreach (var ch in _backend.Channels)
                if (!_settings.FanChannels.ContainsKey(ch.Id)) _settings.FanChannels[ch.Id] = new FanChannelPref();
            foreach (var (id, name) in names)
                if (_settings.FanChannels.TryGetValue(id, out var p) && name is not null) p.Name = name;
            foreach (var rt in _rt.Values)
            {
                rt.LastSourceUsed = null;
                if (resetFailures) { rt.Failures = 0; rt.FailedOver = false; }
            }
            _settings.ActiveFanProfile = profile.Name;
            if (deferSave) { _pendingSave = true; return; }
        }
        _save();
    }

    /// <summary>Set which saved profile name currently matches live state without touching any channel
    /// (e.g. after the caller itself snapshotted/edited <c>settings.FanProfiles</c>). Pass null for "Custom".</summary>
    public void SetActiveProfile(string? name)
    {
        lock (_gate) { _settings.ActiveFanProfile = name; }
        _save();
    }

    // ---- saved profiles (the list itself lives in settings; every access takes _gate because the poll thread
    // reads it inside GameModeSwitcher.Apply while the UI thread adds and removes entries) ----

    public bool TryGetProfile(string name, out FanProfile? profile)
    {
        lock (_gate)
        {
            profile = _settings.FanProfiles.FirstOrDefault(p => p.Name == name);
            return profile is not null;
        }
    }

    public IReadOnlyList<string> ProfileNames()
    {
        lock (_gate) return _settings.FanProfiles.Select(p => p.Name).ToList();
    }

    /// <summary>Store <paramref name="profile"/> under its name, replacing any profile with the same name.</summary>
    public void AddOrReplaceProfile(FanProfile profile)
    {
        lock (_gate)
        {
            int idx = _settings.FanProfiles.FindIndex(p => p.Name == profile.Name);
            if (idx >= 0) _settings.FanProfiles[idx] = profile;
            else _settings.FanProfiles.Add(profile);
        }
    }

    /// <summary>Returns false when no profile of that name existed.</summary>
    public bool RemoveProfile(string name)
    {
        lock (_gate) return _settings.FanProfiles.RemoveAll(p => p.Name == name) > 0;
    }

    /// <summary>Adds the profiles whose names are not taken yet; returns the ones actually added, in order.</summary>
    public IReadOnlyList<FanProfile> AddProfilesIfMissing(IEnumerable<FanProfile> profiles)
    {
        var added = new List<FanProfile>();
        lock (_gate)
        {
            foreach (var prof in profiles)
            {
                if (_settings.FanProfiles.Any(p => p.Name == prof.Name)) continue;
                _settings.FanProfiles.Add(prof);
                added.Add(prof);
            }
        }
        return added;
    }

    public static IReadOnlyList<FanProfile> CreateDefaultProfiles(IReadOnlyList<FanChannel> channels, string? cpuTempId, string? gpuTempId)
    {
        var silent = new[] { new FanPoint(30, 20), new FanPoint(50, 30), new FanPoint(70, 55), new FanPoint(85, 100) };
        var gaming = new[] { new FanPoint(30, 40), new FanPoint(50, 60), new FanPoint(70, 90), new FanPoint(85, 100) };
        return new[]
        {
            Build("Silent", silent), Build("Balanced", FanCurve.DefaultPoints), Build("Gaming", gaming),
        };

        FanProfile Build(string name, IReadOnlyList<FanPoint> points)
        {
            var prof = new FanProfile { Name = name };
            foreach (var ch in channels)
            {
                bool isPump = ch.Name.Contains("pump", StringComparison.OrdinalIgnoreCase);
                bool isGpu = ch.Device.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || ch.Device.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
                          || ch.Device.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || ch.Device.Contains("RTX", StringComparison.OrdinalIgnoreCase)
                          || ch.Device.Contains("RX ", StringComparison.OrdinalIgnoreCase);
                string? src = isGpu ? gpuTempId : cpuTempId;
                prof.Channels[ch.Id] = isPump || src is null
                    ? new FanChannelPref()
                    : new FanChannelPref { Mode = FanMode.Curve, SourceMetricIds = new() { src }, SourceMetricId = src, Points = points.ToList() };
            }
            return prof;
        }
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

                    // An unexpired Identify pulse overrides the channel's own mode entirely: MaxPercent, ramp
                    // limit skipped (below) so the pulse is visible right away. Once it expires we fall straight
                    // through to the channel's own mode this same tick — Auto releases, Manual/Curve ramp back.
                    bool identifying = rt.IdentifyUntil is DateTime identifyUntil && nowUtc < identifyUntil;
                    if (rt.IdentifyUntil is not null && !identifying) rt.IdentifyUntil = null;

                    if (identifying)
                    {
                        target = ch.MaxPercent;
                    }
                    else switch (mode)
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
                        if (!identifying && rt.LastWritten is float last)
                            want = last + Math.Clamp(want - last, -MaxStepPerTick, MaxStepPerTick);
                        want = MathF.Round(want);
                        rt.Target = want;
                        if (!rt.InSoftware || rt.LastWritten != want) WriteLocked(ch, rt, want, pref);
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

            // Poll-thread save: the write fail-safe mode flip, or a game-mode profile switch. Kept inside _gate
            // (which IS AppSettings.SyncRoot) so even a save callback that serializes straight from this thread
            // cannot enumerate FanChannels while the UI thread's Mutate modifies it.
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

    /// <inheritdoc cref="RecoverFromUncleanShutdown(out bool)"/>
    public bool RecoverFromUncleanShutdown() => RecoverFromUncleanShutdown(out _);

    /// <summary>Call once at startup, before the poller starts. If the marker from a previous run exists, every
    /// backend channel is handed back to device control (runtime state is gone). If the backend currently exposes
    /// no channels (e.g. LHM failed to open and the app fell back to perf counters), the marker is left in place
    /// so a later, healthy launch still performs the recovery — releasing nothing now but reporting success would
    /// be a false "fans returned to device control" claim. A channel whose SetAuto throws is still pinned at the
    /// PWM the crashed run left it at, so it is marked InSoftware (the next Tick/RestoreAll retries the release
    /// through ReleaseLocked) and the marker is kept: <see cref="UpdateMarkerLocked"/> clears it only once every
    /// channel is genuinely released. <paramref name="partial"/> reports that case so the caller can say so.</summary>
    public bool RecoverFromUncleanShutdown(out bool partial)
    {
        partial = false;
        if (!_marker.Exists()) return false;
        lock (_gate)
        {
            if (_backend.Channels.Count == 0) return false;

            bool allReleased = true;
            foreach (var ch in _backend.Channels)
            {
                try { _backend.SetAuto(ch.Id); }
                catch (Exception ex)
                {
                    allReleased = false;
                    Rt(ch.Id).InSoftware = true; // still driven: keep it tracked so the release is retried
                    Trace.WriteLine($"[Stats.FanController] recovery SetAuto {ch.Id} failed: {ex.Message}");
                }
            }
            if (allReleased) { _marker.Clear(); _armed = false; }
            else _armed = true;
            partial = !allReleased;
        }
        Trace.WriteLine(partial
            ? "[Stats.FanController] previous run did not shut down cleanly; some fans could not be returned to device control"
            : "[Stats.FanController] previous run did not shut down cleanly; all fans returned to device control");
        return true;
    }

    /// <summary><paramref name="pref"/> is null when Identify pulses a channel that has no saved preference (an
    /// Auto channel that was never touched) — the fail-safe below must not fabricate one, so it only flips a mode
    /// that already exists.</summary>
    private void WriteLocked(FanChannel ch, Runtime rt, float percent, FanChannelPref? pref)
    {
        // LHM's SetSoftware may switch the control into software mode before the hardware write itself fails
        // (a "partial write"), so mark InSoftware before the call — otherwise a failed write would leave the
        // channel pinned at whatever PWM it landed on while we believe it's still under device control.
        rt.InSoftware = true;
        // Only latch _armed once the marker is actually on disk — a transient failure (AV lock, disk full) must
        // not disable crash recovery for the rest of the session; the next write retries the marker.
        if (!_armed && _marker.Set()) _armed = true;
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
                if (pref is not null)
                {
                    pref.Mode = FanMode.Auto;
                    _pendingSave = true; // persist the fail-safe mode flip at the end of the tick, still under _gate
                }
                rt.FailedOver = true; // keep the Auto branch reporting WriteFailed until the user changes something
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
