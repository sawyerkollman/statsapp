# Fan control backlog: zero-RPM stop, per-channel tuning, profile import/export — design

Date: 2026-09-11. Base: `feature/v1.10` (master v1.9.2 + layout modes + graph effects). Branch `feature/fan-backlog`.
Three items recorded as out of scope by the fan-control and v1.4 specs and never picked up. Live hardware
re-discovery stays out (rule 5: restart). This is the write path to hardware: every rule-6 invariant is restated
below and pinned by tests. No new NuGet packages; JSON via `System.Text.Json`, dialogs via `Microsoft.Win32`.

## Owner decisions (already made — do not re-litigate)

1. Zero-RPM is only offered for channels that are **not pumps** and whose **sanitized `FanRange` minimum is 0**;
   it stops the fan (writes 0 %) when the source temperature is below X and restarts at **X + 3 °C** (fixed);
   every existing safety invariant holds: pumps floor at 50 %, GPU min clamp (so zero-RPM is unavailable on any
   channel whose minimum is above 0, GPU included), master switch off → no writes, missing source → failsafe Auto,
   three failed writes → Auto.
2. Per-channel hysteresis (0.5–5 °C, default = today's global 2 °C) and slew (2–50 points per tick, default =
   today's 10) live on `FanChannelPref`, are sanitized at load, and are edited in an **Advanced** expander on the
   channel card; existing files load with today's behaviour.
3. Import/export: the Fans window profile menu gains **Export…** and **Import…** (standard Win32 dialogs); an
   exported profile is one JSON file; import validates (curve validation, range sanitation, unknown channels kept)
   and adds a new named profile **without applying it** — applying stays an explicit user action.
4. Every new behaviour gets `FanControllerTests` with the `FakeBackend` and fixed clock, including "zero-RPM never
   writes 0 to a pump" and "zero-RPM restart hysteresis".

## Owner decisions assumed

1. **Model:** `FanChannelPref.StopBelowC` (`float?`, null = off; honoured in **Curve mode only**),
   `FanChannelPref.HysteresisC` (`float?`, null = default `FanController.HysteresisC` 2), `FanChannelPref.MaxStepPerTick`
   (`float?`, null = default `FanController.MaxStepPerTick` 10). Nullable so an untouched channel and a pre-1.10
   file mean "default" and nothing changes for existing users.
2. **Eligibility predicate** = `!IsPump(channel) && channel.MinPercent <= 0f`, where `IsPump` is the existing
   pump test the `PumpFloorPercent` floor already uses (share the helper, do not duplicate the string test). The
   controller enforces it (a `StopBelowC` on an ineligible channel is ignored, never written) and the UI hides the
   control for ineligible channels with a one-line caption ("Zero-RPM not available: this channel's minimum is
   30 %" / "…on pumps").
3. **Stop mechanics:** in Curve mode, when the (hysteresis-filtered) source temperature is `< StopBelowC`, the
   channel's target is 0 %: it stays `InSoftware` (we are still driving it; the armed marker stays), the normal
   slew ramps it down, and the write dedupe applies. The channel reports the new appended status
   `FanChannelStatus.Stopped` (`StatusText` "Stopped (below X °C)") while its last written value is 0 and the stop
   condition holds. Restart when the temperature is `≥ StopBelowC + 3`: the first tick's target is
   `max(curveTarget, KickPercent)` with `KickPercent = 30f` (a public const), written **without** slew for that one
   tick (fans stall below a minimum duty; a slewed climb 0→10→20 can fail to spin up); from the next tick the
   normal slew applies from the kick value. Between X and X + 3 the last decision (stopped or running) holds.
4. **Failsafe precedence:** source stale/unavailable → the existing failsafe (release to Auto with status) wins
   over zero-RPM; three failed writes → Auto as today; master off → nothing. A stopped channel that loses its
   source therefore goes to hardware Auto (spins), never stays stopped blind.
5. **Tuning semantics:** hysteresis applies where `HysteresisC` applies today (Curve source filtering); slew
   applies where `MaxStepPerTick` applies today (Manual and Curve). Units stay **points per tick**; the README's
   "10 %/s" becomes "10 points per poll (10 %/s at the default 1 s interval)".
6. **Sanitation:** `SettingsService`'s per-pref `Sanitize` (promoted from a local function to
   `internal static SanitizeChannelPref(FanChannelPref)` — `InternalsVisibleTo` already covers the test project;
   if not, `public static`) clamps `HysteresisC` to [0.5, 5] and `MaxStepPerTick` to [2, 50], and sets NaN/out-of-
   range/≤ 0 values to **null** (default) rather than clamping, since a corrupt value carries no intent;
   `StopBelowC` outside [0, 120] or NaN → null. It runs, as today, over `FanChannels` and every profile's channels,
   and over every imported profile.
7. **`Clone` copies the three new fields** (`SnapshotProfile`/`ApplyProfile` carry them; `SnapshotProfile_IsDeepCopy`
   extended). `FanChannelView` gains, appended at the end: `float? StopBelowC, float? HysteresisC, float? MaxStepPerTick, bool ZeroRpmEligible`.
8. **Setters** `SetStopBelow(id, float?)`, `SetHysteresis(id, float?)`, `SetSlew(id, float?)` go through `Mutate`
   like the others (so they mark the profile Custom/modified and save once each); values are sanitized on the way
   in with the same rules as load.
9. **Import/export file format:** one profile per file, envelope
   `{ "Format": "stats-fan-profile", "Version": 1, "Profile": { "Name": …, "Channels": { … } } }`, indented,
   enum members by name, extension `.json`, default folder `Documents`, default file name `<profile>.fan-profile.json`.
   Channel ids are LHM identifiers from the exporting machine; import keeps channels this machine lacks (dormant:
   `ApplyProfile` only ever drives backend channels) — the import result message says how many channels matched.
10. **Parsing lives in Core:** `Stats.Core/Settings/FanProfileFile.cs` (static): `Serialize(FanProfile) → string`
    and `TryParse(string json, IReadOnlyCollection<string> knownChannelIds, out FanProfile? profile, out string error,
    out int matchedChannels)`. It uses a lenient `FanMode` converter (copy of `DashboardLayoutModeConverter`;
    unknown → `Auto`) so a foreign file never throws; rejects a wrong `Format`, a `Version` > 1, a blank name, or
    a missing `Profile`; runs `SanitizeChannelPref` on every channel; never touches settings or hardware.
11. **Import conflict policy:** if a profile with the same name exists, the window prompts with `InputDialog`
    pre-filled `"<name> (2)"`; cancel aborts. The imported profile is added via `FanController.AddOrReplaceProfile`
    and appears in `ProfileNames`; it is **not** applied. Export reads the profile under the gate via
    `TryGetProfile`, clones it (`FanController.Clone`-based deep copy) and serializes outside the gate.
12. **View-model surface (WPF-free):** `FansViewModel.ExportProfileText(string name) → string?` and
    `FansViewModel.ImportProfileText(string json, Func<string, string?> resolveNameConflict) → (bool ok, string message)`;
    the window owns the dialogs and passes the text in/out. A new `[ObservableProperty] string ProfileMessage`
    (transient, shown under the profile row, cleared on the next profile action) carries "Imported 'Silent'
    (4 of 4 channels match this PC)" or the parse error.
13. **Curve editor:** `FanCurveEditor` gains `StopBelowTemp` (double, NaN = off, `AffectsRender`) and shades the
    band left of X with the existing floor brush at low alpha, plus a 1 px dashed line at X — same theme resync
    pattern as `FloorBrush`.
14. **No `SettingsChange` member, no `App.xaml.cs` change**: everything routes through `FansViewModel` +
    `FanController` as today (fan prefs persist via `FanController._save`).
15. **Default profiles:** `CreateDefaultProfiles` is unchanged (Silent does not use zero-RPM; the owner can save
    their own).

## Settings (rule 3)

- `FanChannelPref` += `public float? StopBelowC { get; set; }`, `public float? HysteresisC { get; set; }`,
  `public float? MaxStepPerTick { get; set; }` (XML docs: null = off / default). Nested in `FanProfile.Channels`
  automatically.
- `SettingsService.SanitizeChannelPref` per assumed decision 6; `Normalize` unchanged otherwise.
- `FanChannelStatus` += `Stopped` (appended, rule-2 style).
- Old files: every field absent → null → today's behaviour exactly.

## Core (`Stats.Core`)

- `FanController`:
  - `public const float KickPercent = 30f; public const float StopRestartDeltaC = 3f;`
  - `Runtime` gains `bool Stopped` (decision state for the X..X+3 band).
  - Phase 1, Curve branch: `hyst = pref.HysteresisC ?? HysteresisC` replaces the const in the source filter;
    after `target = curve.Evaluate(useTemp)`: if `pref.StopBelowC is float x && ZeroRpmEligible(ch)`:
    `if (rt.Stopped ? useTemp >= x + StopRestartDeltaC : useTemp < x) rt.Stopped = !rt.Stopped;` then
    `if (rt.Stopped) target = 0f; else if (justRestarted) { target = MathF.Max(target, KickPercent); bypassSlew = true; }`.
    Any other mode, or an ineligible channel, clears `rt.Stopped`.
  - Clamp/slew block: `step = pref.MaxStepPerTick ?? MaxStepPerTick`; when `bypassSlew` the kick value is written
    directly (still clamped to the channel range). The pump floor and `Math.Clamp(want, min, max)` stay exactly
    where they are, so a pump or a min-30 channel can never reach 0 even if a pref carries `StopBelowC`.
  - Status: `Stopped` when `rt.Stopped && rt.LastWritten == 0`; otherwise as today.
  - `Views()` fills the four new record fields; `Clone` copies the three prefs; setters per assumed decision 8.
- `FanProfileFile` per assumed decision 10, with `FanModeConverter` beside it.
- `FanChannelViewModel`: `[ObservableProperty] float? StopBelowC` (+ `bool StopBelowEnabled` mirror for the
  checkbox, `double StopBelowValue` for the slider, default 40 when first enabled), `float? HysteresisC`,
  `float? MaxStepPerTick`, `bool ZeroRpmEligible`, `string ZeroRpmUnavailableText`; `Apply(FanChannelView)` maps
  them; `On…Changed` push to the controller under the `_refreshing` guard; `StatusText` maps `Stopped`.
- `FansViewModel`: `ExportProfileText`, `ImportProfileText`, `ProfileMessage` per assumed decision 12.

## App (`Stats.App`)

- `FansWindow.xaml` channel card: in the Curve section, a "Stop fan below" `CheckBox` + `Slider` (20–80 °C,
  step 1, `Delay=250`) + value text, visible only when `ZeroRpmEligible`, else the caption; an **Advanced**
  `Expander` (collapsed by default, implicit style) at the bottom of the card, visible in Manual and Curve, with
  "Hysteresis" (0.5–5 °C, 0.5 steps) and "Max change per poll" (2–50 points) sliders and a "Reset to defaults"
  link that nulls both. `FanCurveEditor` gets `StopBelowTemp="{Binding StopBelowC, TargetNullValue=NaN}"`.
- `FansWindow.xaml.cs` `ProfileMenu_Click`: "Export '<name>'…" (disabled when nothing is selected) →
  `SaveFileDialog` (owner = this window) → `File.WriteAllText(path, vm.ExportProfileText(name))`; "Import…" →
  `OpenFileDialog` → `vm.ImportProfileText(File.ReadAllText(path), suggested => InputDialog.Show(this, "Profile
  name", "A profile with that name exists. Import as:", suggested))`. I/O exceptions land in `ProfileMessage`.
- `ProfileMessage` `TextBlock` under the profile row (secondary text, collapsed when empty).
- `README.md`: fan paragraphs updated (zero-RPM, Advanced tuning, "points per poll", Import/Export).

## Preview harness

- Fans substates: `stop-below` (`/lpc/it8696e/0/control/0` in Curve mode with `StopBelowC = 75`, source
  `cpu.temp.tctl` ≈ 68 → after `TickFan` the card shows "Stopped (below 75 °C)", 0 %, the editor's shaded band);
  `advanced` (a fixture channel with `HysteresisC = 1`, `MaxStepPerTick = 25`; VM-level — the expander's open
  state is a visual substate in `CaptureHost`: expand the first card's Advanced expander before capture);
  `imported` (fixture `FanProfiles` gains "Imported (USB PC)" and `ProfileMessage` is set through the VM's
  import path with a serialized fixture profile whose channel ids only partly match — proves the message text).
  `baseline.json`: each × Dark Amber at 760×640, `stop-below` also Light. Harness tests
  (`FanBacklogSubstateTests`): catalog accept/reject, `stop-below` ends with status `Stopped` and last write 0 on
  that channel and no write on the pump, `advanced` prefs round-trip through the VM, `imported` adds the profile
  without changing `ActiveProfileName`; `CompositionIsolationTests`' hard-coded fans substate list extended, no
  new command verbs (parsing is pure; dialogs are never opened in the harness).
- **Cannot be captured:** the file dialogs and the profile context menu (popups), the kick-start write timing,
  real fan stall/spin-up behaviour, the restart hysteresis in real time, and whether a given chip's "0 %" is a
  true stop. All on the owner checklist.

## Non-goals

Live hardware re-discovery; BIOS curve readback; detecting other fan software beyond the existing warning; ARM64;
a background service; zero-RPM in Manual mode (set 0 % by hand where the range allows); per-profile tuning
defaults; importing whole settings files; multi-profile export; applying on import.

## Acceptance

- `dotnet build --nologo` 0 warnings; `dotnet test --nologo` green, 0 warnings.
- **`FanControllerTests` (append):** `ZeroRpm_StopsBelowX_RestartsAtXPlusThree_WithKick` (Linear curve, X = 40:
  temps 45 → writes 15; 39 → ramps to 0 over ticks; 41, 42 → still 0; 43 → one write of `max(13, 30)` = 30
  without slew, then slewed toward the curve); `ZeroRpm_NeverWritesZeroToAPump` (pump pref with `StopBelowC`
  → floor 50 stays); `ZeroRpm_IgnoredOnChannelWithMinAboveZero` (GPU min 30 stays ≥ 30);
  `ZeroRpm_SourceStale_FailsafeToAutoWins` (stopped channel, source null for 10 s → `SetAuto`, status as today);
  `ZeroRpm_MasterOff_NoWrites`; `ZeroRpm_StatusStopped_WhileLastWrittenIsZero`;
  `PerChannelHysteresis_OverridesDefault` (hysteresis 0.5: a 1 °C change re-evaluates; default 2 does not);
  `PerChannelSlew_OverridesDefault` (slew 25: 0→100 in 4 ticks; default 10 in 10 — existing
  `SlewLimit_TenPointsPerTick` unchanged); `NullTuning_UsesDefaults_ExistingTestsUnchanged`;
  `SnapshotProfile_IsDeepCopy` extended for the three fields; `Setters_StopBelowHysteresisSlew_PersistAndSaveOnceEach`
  (own test, not folded into the existing save-count test); `SetStopBelow_OnIneligibleChannel_IsStoredButNeverWritten`.
- **`SettingsServiceTests` (append):** `Load_PreFanBacklogFile_TuningFieldsNull`;
  `Load_SanitizesTuningFields_OutOfRangeBecomesNull` (theory over hysteresis 0, 6, NaN; slew 1, 51, NaN; stop 130,
  -1, NaN); `SaveThenLoad_TuningFields_RoundTrip`; `Load_SanitizesTuningInsideProfiles`.
- **`FanProfileFileTests` (new):** `Serialize_ThenTryParse_RoundTrips`; `TryParse_WrongFormat_Fails_WithMessage`;
  `TryParse_FutureVersion_Fails`; `TryParse_BlankName_Fails`; `TryParse_UnknownFanMode_FallsBackToAuto`;
  `TryParse_MalformedCurve_FallsBackToDefault`; `TryParse_ReportsMatchedChannelCount`;
  `TryParse_NeverThrows_OnGarbage` (theory: "", "{}", "[]", "not json").
- **`FansViewModelTests` (append):** `ExportProfileText_UnknownName_ReturnsNull_KnownName_ReturnsEnvelope`;
  `ImportProfileText_AddsProfile_DoesNotApply_SetsMessage`; `ImportProfileText_NameConflict_UsesResolver_CancelAborts`;
  `ImportProfileText_BadFile_SetsErrorMessage_NoProfileAdded`; `Channel_ZeroRpmEligible_FollowsMinAndPump`;
  `Channel_StopBelowToggle_PushesToController_OnceEach`; `Channel_AdvancedSliders_PushNullOnReset`;
  `Channel_StatusText_Stopped`.
- **Harness:** the `FanBacklogSubstateTests` above; captures `stop-below` (Dark Amber, Light), `advanced`,
  `imported`, every sidecar `Warnings` empty.
- **Owner checklist (real hardware):**
  1. Zero-RPM on a case fan whose range is 0–100: below X the fan truly stops (RPM 0), at X + 3 it restarts
     (the kick at 30 % spins it up), and it does not oscillate around X.
  2. The control is hidden on the pump and on the GPU fan with the caption explaining why.
  3. A chip that reports a bogus range (sanitized to 0–100) — if the fan cannot actually restart from 0, switch
     zero-RPM off for it; note the chip.
  4. Advanced: hysteresis 0.5 makes the curve follow small changes; slew 50 makes a Manual change land in two
     polls; Reset returns both to the defaults and the profile shows Custom/modified as for any other edit.
  5. Export a profile, delete it, import the file: same name, same curves and tuning, not applied until Load.
  6. Import the file on the laptop: the message reports how many channels match; loading it drives only the
     matching channels.
  7. Import a hand-broken file (wrong Format, junk): a readable error under the profile row, nothing added.
  8. Source loss on a stopped fan: it goes to hardware Auto (spins) within ~10 s.
