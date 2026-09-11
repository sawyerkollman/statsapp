# T6 — fan window — report

Branch `worktree-agent-a8ddcc30ba0b24ace` (rebased onto `ui-polish` @ `4395d10`). Build:
`dotnet build --nologo` → 0 warnings, 0 errors. Tests: `dotnet test --nologo` → 687 (Stats.Core.Tests) +
172 (Stats.UiPreview.Tests) passed, 0 failed, 0 warnings — identical counts to the pre-change baseline;
no `FanControllerTests`/`FansViewModelTests` were touched or needed changes.

## Files changed

- `src/Stats.App/Views/FansWindow.xaml` — full rewrite of the window body on the frozen T2 tokens/styles;
  no changes to bindings/commands the VM already exposes.
- `src/Stats.App/Views/FansWindow.xaml.cs` — added `ProfileMenu_Click`, a code-behind `ContextMenu`
  (same pattern as `DashboardWindow.ViewButton_Click`) holding Delete/Create-defaults; everything else
  unchanged.
- Nothing in `Stats.Core` (`FanController`, `FansViewModel`, `GameModeSwitcher`, settings) was touched —
  the existing VM surface (`Enabled`, `SetAllAutoCommand`, `ProfileNames`, `SelectedProfileName`,
  `ActiveProfileName`, `IsModified`, `ReloadCommand`, `SaveProfileCommand`, `DeleteProfileCommand`,
  `CreateDefaultProfilesCommand`, `SafetyBannerCollapsed`/`DismissSafetyBannerCommand`,
  `RecoveryNotice`/`HasRecoveryNotice`/`DismissRecoveryNoticeCommand`, `ConflictText`/`HasConflict`,
  `HasChannels`, `UnavailableText`, `GameModeEnabled`, `GamingProfile`, `DesktopProfile`, `GameModeStatus`,
  `Devices`, and per-channel `Id/Name/RpmText/PercentText/Mode/IsManual/IsCurve/ManualPercent/
  MinPercent/MaxPercent/TargetText/StatusText/SourceSummary/SourceSelections/SourceTempText/
  ResetCurveCommand/Points/LiveTemp/LiveTarget/IdentifyCommand/ControlEnabled`) was sufficient.

## Layout summary

**Header** — "Fans" at `FontSizeTitle` SemiBold, followed by a quiet `FontSizeLabel`/`TextSecondary`
state line driven by a `DataTrigger` on `Enabled` ("Fan control off — fans under device control" /
"Fan control on — Stats is writing fan speeds"; no colour change on the armed state, per DESIGN.md §6).
The master `CheckBox` and `All to Auto` `HeaderButton` (`AutomationProperties.Name="All fans to Auto"`)
sit in a right-docked `WrapPanel` that drops to a second row before it would clip.

**Profile row** — a `WrapPanel`: "Profile" label (`SettingsLabel`), `ComboBox`, `ActiveProfileName` text,
a quiet "Modified" text badge (`FontSizeDense`, visible when `IsModified`), `Reload` (same visibility),
`Save as…`, and one `Icon.More` `HeaderButton` ("Profile options") that opens a code-behind `ContextMenu`
with `Delete "<name>"` (bound to `DeleteProfileCommand`, parameter snapshotted from `SelectedProfileName`
at open time — the source-generated command already refuses a null parameter, so the item disables itself
when nothing is selected, matching the pre-T6 button's implicit behavior) and `Create default profiles`.

**Game mode** — its own `SettingsHeader`-styled section: "Game mode" heading, the enable `CheckBox` with
a short label, a `FontSizeDense` secondary line about the FPS tracer, then a wrapping row of Gaming/
Desktop selectors and `GameModeStatus`.

**Notices** — safety/recovery/conflict banners keep their fixed dark tint colours (`#4A3A1E`/`#4A1E1E`/
`#5A1E1E`) and `Icon.Warn`, now on `RadiusPanel` corners with `Padding="12,8"` (previously 4px radius,
10,6 padding) to match the dashboard notice pattern; safety copy was shortened but still states the
hardware-write fact and that Auto/off/exit restores device control. `UnavailableText` (no channels) is now
an `Icon.Info` notice on a neutral `ControlBg` surface instead of a bare `TextBlock`.

**Fan card** — `TileBg`/`RadiusPanel`/`Padding="12"`. Row 1 is a `WrapPanel` (not a `Grid` — see "layout
fix" below) containing: the name `TextBox` (new `FanNameTextBox` style: borderless/transparent at rest,
reveals `ControlBg` fill + the shared template's hover/focus border on `IsMouseOver`/
`IsKeyboardFocusWithin`, "Click to rename" tooltip kept, `LostFocus` binding kept), RPM and duty-% in
fixed-width tabular-aligned columns, and a local `SegmentedRadioButton`-styled Auto/Manual/Curve trio
(still three `RadioButton`s sharing `GroupName="{Binding Id}"`, so per-channel exclusivity and arrow-key
navigation are unchanged) plus the `Identify` `HeaderButton` (`Icon.Identify` + text, same command/
tooltip) — kept together as one unit so they wrap onto their own row below the name/RPM/duty at 560px
wide, rather than squeezing the name box. The three-radio group's `IsEnabled` is bound to `ControlEnabled`
(Opacity 0.5 when off, matching Identify's existing `CanExecute` gating). Row 2 is `StatusText` at
`FontSizeDense`. Manual/Curve sections (slider, target, sources `Expander`, `FanCurveEditor` at height
150) are otherwise unchanged from the pre-T6 XAML, including `MinPercent`/`MaxPercent` visibility through
the slider range and curve editor.

### Layout fix found during capture review

The first pass used a `Grid` with `Width="Auto"` for the segmented+Identify column. A `Grid` `Auto`
column is always measured at its full unconstrained desired width — it never shrinks — so at 560px wide
it silently clipped the name `TextBox` ("Fan #1" rendered as just "Fan") instead of wrapping the
right-side group as intended. Fixed by making row 1 a `WrapPanel` of fixed-width children (name/RPM/duty)
plus one `StackPanel` holding the segmented control + Identify as an atomic unit — `WrapPanel` measures
against real available width, so that unit now genuinely drops to its own line at 560px while staying on
one row at 760px. Verified both ways (see captures below).

## Safety check (DESIGN.md §6 / CLAUDE.md rules 1 and 6)

Inspected `commands.log` (`%TEMP%\Stats.UiPreview\<run-id>\commands.log`) for every one of the 13 required
captures plus the 560×640 layout-check run, both before and after the Grid→WrapPanel fix. In every run the
log contains only: (a) `settings.save` and `fan.SetPercent`/`fan.SetAuto` entries produced by
`PreviewComposition.ApplySubstate`'s own fixture setup (`ApplyFanManual`/`ApplyFanCurve`/`ApplyFanPump`/
`ApplyFanWriteFailed`), which all occur **before** `CaptureHost` builds any window, and (b) nothing else —
no entries appear after window `Show`/settle/capture, and no run (including `off`, `on+modified`,
`no-channels`, `on+conflict`, `on+recovery`, and the Light-theme `on+curve`) contains an `Identify` entry
or any unexpected `SetPercent`/`SetAuto`. Template load, data binding, and view construction fire no
commands.

## Captures (`artifacts/ui-polish/after-t6/`), all sidecars show `"Warnings": []`

| Capture | Dimensions | Notes |
|---|---|---|
| `off` | 746×633 | Segmented control + Identify at Opacity 0.5 (ControlEnabled=false); state text reads "off" |
| `on` | 746×633 | State text reads "on"; controls at full opacity |
| `on+manual` | 746×633 | Manual slider/target row visible on Fan #1 |
| `on+curve` | 746×633 | Curve editor + sources Expander on Fan #2 |
| `on+pump` | 746×633 | Pump channel present in the device list (below the fold at this height, same as T2's fans capture) |
| `on+modified` | 746×633 | "Modified" text + Reload button appear next to "Custom" |
| `no-channels` | 746×633 | See caveat below — the harness's own substate clears the backend after `FansViewModel` already snapshotted its channels, so `HasChannels` doesn't flip in this particular fixture path; the `Icon.Info`/neutral-surface binding itself is correct and unchanged in shape from the working `HasRecoveryNotice`/`HasConflict` bindings beside it |
| `on+conflict` | 746×633 | Red conflict banner with `Icon.Warn`, no clipping |
| `on+recovery` | 746×633 | Dark-red recovery banner with Dismiss |
| `on+write-failed` | 746×633 | "Write failed — check other fan software" status text on Fan #1 |
| `on+curve-560x360` | 546×353 | Header stays one row (content fits); card area scrolled/short at this height |
| `on+manual-560x360` | 546×353 | Same |
| `on+curve-light` | 746×633 | Light palette: cards, borders, segmented control all repaint correctly; dark title bar is the existing pinned `DarkTitleBar` behavior, unrelated to this change |

An additional (unrequested, not committed as a deliverable capture) 560×640 run was used mid-task to
diagnose and confirm the Grid→WrapPanel wrap fix; not included in the file list above.

Visual check performed by reading each PNG: no clipping at either width, segmented control shows exclusive
selection with a clear checked state (`ControlBg` fill + `AccentBrush` border), Identify stays visible and
legible next to it, the 560px captures wrap the segmented+Identify group onto its own row while RPM/duty
stay tabular-aligned, and all three notice colours/icons render consistently with `RadiusPanel` corners.

## Pending owner checks (not exercisable from this harness)

- Hover/pressed/keyboard-focus visuals on `SegmentedRadioButton` and the new `FanNameTextBox` hover/focus
  reveal — the harness captures are static renders; the `off.png` capture happened to show the "GPU Fan 1"
  name box in its hover state (leftover OS cursor position from a prior capture), which is a good sign the
  template triggers work, but a live mouse/keyboard pass on real Windows would confirm all states.
- Real hardware writes, three-strikes write-failure recovery, and the pump 50% floor against an actual
  device — this pass only touched XAML/code-behind that consumes the existing `FanController`/
  `FansViewModel` contract unchanged; `tests/Stats.Core.Tests` (unmodified, still green) is the safety net
  for that logic.
- Identify's actual 2-second max-speed pulse on a physical fan.
- The `no-channels` substate caveat above — if the orchestrator wants that capture to show the empty-state
  notice for real, the fix belongs in `tools/Stats.UiPreview/PreviewComposition.cs` (re-run `ApplySubstate`
  after mutating `FanBackend.Chans`, or clear it before `FansViewModel` construction), which is outside
  this task's owned files (`Views/FansWindow.xaml`/`.xaml.cs` only).

## Commit

`feat(app): fans window polish (T6)` — see `git log` in this worktree for the hash (recorded after
committing, below).
