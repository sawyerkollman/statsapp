# Dashboard and overlay layout profiles with Game-mode switching — design

Date: 2026-09-11. Base: `feature/v1.10` @ `4323b0b` (master `75115aa` v1.9.2 + dashboard layout modes + graph
effects — read `docs/superpowers/specs/2026-09-11-dashboard-layout-modes-design.md` and
`2026-09-11-graph-effects-design.md` first; this feature snapshots and restores the state those two define, it
never changes their semantics). Branch `feature/layout-profiles`. `AppSettings`, `SettingsService`,
`SettingsViewModel`, `App.xaml.cs`'s `OnSettingsChanged` and the Fans window's Game-mode section are only
*appended* to, so the branch merges cleanly with anything else based on the integration branch.

Owner ask (brief): named layout profiles that capture the whole dashboard + overlay arrangement so the user can
keep e.g. a **Gaming** and a **Desk** layout and switch between them from the header's **View** menu, and so
**Game mode** (which already switches fan profiles between a gaming and a desktop profile) can switch layout
profiles the same way.

## Goal

A **layout profile** is a named, saved copy of exactly these `AppSettings` fields:

| Field | Meaning in a profile |
| --- | --- |
| `DashboardMetrics` | tile selection **and** order |
| `OverlayMetrics` | overlay selection (rendered in store order, as today) |
| `TilePrefs` — arrangement half only: `Kind`, `Size`, `X`, `Y` | per-tile kind, size, Free/Snap position (also for tiles not currently on the dashboard — positions are kept per tile today, README "Dashboard layout") |
| `CollapsedGroups` | which Auto-mode groups are collapsed |
| `ShowCoreMatrix` | core-matrix block on/off |
| `DashboardLayoutMode` | Auto / Free / Grid |
| `CoreMatrixX`, `CoreMatrixY` | core-matrix block position |

`TilePref.Name` and `TilePref.Max` are **not** part of a profile: they are per-metric display preferences
shared by every profile, preserved across a switch exactly the way `FanController.ApplyProfile` preserves each
channel's `Name` override (`src/Stats.Core/Fans/FanController.cs`, "names preserved").

Behaviour, end to end:

| User action | Result |
| --- | --- |
| View ▸ Layout profiles ▸ **Save as…** `Gaming` | the live arrangement is snapshotted into `LayoutProfiles` under that name; `Gaming` becomes the active profile (checked in the menu), not modified. Saving onto an existing name replaces it. |
| Edit the arrangement (add/remove/move/resize/re-kind a tile, drag/nudge, collapse a group, change layout mode, Reset tile positions, toggle Show CPU core matrix) | the menu header reads `Layout profile: Gaming (modified)`; **Save** and **Revert** become enabled. Nothing is auto-saved into the profile. |
| **Save** | re-snapshots the live arrangement into the active profile; modified clears. |
| **Revert** | re-applies the active profile; unsaved edits are discarded; modified clears. |
| Check another profile (`Desk`) | `Desk` is applied atomically (one settings write, one `RebuildSections`, one overlay rebuild, one settings save). `Gaming` — the profile being left — is untouched in `LayoutProfiles`; if it had unsaved edits they are lost (the same as picking another fan profile while Modified). |
| **Rename…** | renames the active profile; the active name and any Game-mode pick naming it follow. |
| **Delete** | removes the active profile; the live arrangement stays exactly as it is, nothing is checked (the implicit unnamed layout), Game-mode picks naming it are cleared. |
| Fans ▸ Game mode ▸ **Gaming layout** / **Desktop layout** picks | when Game mode enters gaming (≥ 10 fps for 5 s) the Gaming layout profile is applied; when it exits (20 s below) the Desktop one is. **None** leaves the layout alone. Same `GameModeEnabled` switch and hysteresis as the fan picks. |

When no profile has ever been saved the app behaves byte-for-byte as before apart from one extra submenu in the
View menu and one extra row in the Fans window's Game-mode section.

## Owner decisions (already made — do not re-litigate)

1. Profiles live in `AppSettings` as a named list plus an active-profile name, mirroring
   `FanProfiles`/`ActiveFanProfile`. Edits made while a profile is active mark it **modified** (mirror the
   fan-profile Modified state, with Save and Revert); they do not auto-save into the profile.
2. The View menu gains a **Layout profiles** submenu: checkable list of profiles, **Save**, **Save as…**,
   **Rename…**, **Delete** using the existing `InputDialog`; deleting the active profile falls back to the
   implicit unnamed current layout.
3. Game mode: the existing gaming/desktop fan-profile pickers get a layout-profile counterpart
   (`GameModeGamingLayoutProfile` / `GameModeDesktopLayoutProfile`), applied by the same game-mode signal that
   switches fan profiles; **none** means leave the layout alone.
4. Switching a profile applies atomically (one settings mutation + one `RebuildSections` + one overlay rebuild +
   one save), never loses positions of the profile being left when it was saved, and never writes fan or
   hardware state.

## Owner decisions assumed (decided here because the brief left them open — flip any of them before T1 starts)

1. **What "arrangement" means for a tile.** `Kind`, `Size`, `X`, `Y` are profile-owned; `Name` and `Max` are
   shared and preserved on apply (see Goal). A snapshot stores `Name`/`Max` as `null` so the JSON never claims a
   value that apply would ignore. Consequence: renaming a tile or setting a gauge max never marks a profile
   modified.
2. **Modified tracking** is a persisted flag, `AppSettings.LayoutProfileModified`, and `ActiveLayoutProfile`
   is *kept* while modified. This deviates from fans (where `Mutate` nulls `ActiveFanProfile`) because the
   menu's **Save** item needs a target name and the checkable list must keep showing which profile the user is
   editing. Only user-driven arrangement edits set the flag (the exact call sites are listed under Core); the
   seed pack, `SetCoreMatrixSize`'s fix-up, `RebuildSections`, rename, max and threshold edits never do.
3. **Where the Game-mode layout picks live:** the Fans window's Game-mode section, a second row under the fan
   pickers, gated by the same `GameModeEnabled` checkbox and hysteresis. No separate "layout only" switch: a
   user who wants only layout switching enables Game mode and leaves the fan picks empty (`GameModeSwitcher.Apply`
   already treats a null fan pick as leave-as-is). `ApplyFrameTracing` and `GameModeSwitcher.Tick`'s early
   return are untouched. The pickers get an explicit **None (leave layout as is)** entry — unlike the fan
   ComboBoxes, a null pick must be selectable, not just the result of deleting a profile.
4. **Transition signal:** a new `GameModeSwitcher.GamingChanged` event raised on the poll thread once per
   transition, after the fan apply has returned and `IsGaming` has latched; the composition root marshals it
   with `Dispatcher.BeginInvoke` (like `_poller.HealthChanged`) and the UI thread resolves the profile name from
   settings. No polling from `RunCoalescedRefresh`, no sibling switcher.
5. **Game-mode apply skips** when the pick names the profile that is already active *and* unmodified
   (mirrors `GameModeSwitcher.Apply`'s "already live" check); when modified it re-applies and the unsaved edits
   are lost (mirrors fans, where an edited profile is null-active and gets re-applied). An explicit menu click
   always applies.
6. A game-mode-triggered apply **persists** like any other (it is the live state), so a restart mid-game loads
   the gaming layout with it checked. Exiting gaming with a **None** desktop pick leaves the layout as-is.
7. **Names:** trimmed, ordinal match, blank ignored, no cap, no reserved names. **Save as…** onto an existing
   name replaces without confirmation (mirrors `FansViewModel.SaveProfile`). **Rename…** onto an existing name
   is a no-op (never silently merges two profiles). **Delete** has no confirmation (mirrors
   `FansWindow.ProfileMenu_Click`); the brief's "Delete…" ellipsis is dropped for that reason.
8. **UI surface** is the View submenu only: no header badge, no tray submenu, no "Create default layouts"
   generator. The submenu's own header carries the state (`Layout profiles` / `Layout profile: Gaming` /
   `Layout profile: Gaming (modified)`).
9. **Undiscovered ids** (a profile saved when a sensor existed that is gone now) stay in the profile and in the
   restored lists verbatim; `RebuildSections` and `OverlayViewModel.Rebuild` already filter through the store.
   Nothing is surfaced.
10. **Only `OverlayMetrics`** is the overlay's contribution; orientation, font scale, opacity and position stay
    global settings.
11. **`SettingsService.Normalize`** nulls `ActiveLayoutProfile` when it names no profile (and clears the modified
    flag whenever the active is null); the two Game-mode layout picks are **kept** even when dangling, for the
    same reason `FansViewModel.IsComboBoxCoercion` exists (a pick naming a profile this file no longer has must
    not be silently erased).
12. **Drag race guard:** `DashboardWindow.CommitFreeDrag`/`CommitCoreDrag` skip the VM write when the container
    is no longer in a `PresentationSource` (a game-mode apply rebuilt the canvas mid-drag).
13. The harness builds its profiles through the view model's own commands (no `ScenarioFixture` field), the way
    the `modified` fan substate goes through `SaveProfileCommand`.

## Settings (rule 3: every field defaulted, null-guarded and sanitized; old files load)

New `src/Stats.Core/Settings/LayoutProfile.cs`:

```csharp
public sealed class LayoutProfile
{
    public string Name { get; set; } = "";
    public List<string> DashboardMetrics { get; set; } = new();
    public List<string> OverlayMetrics { get; set; } = new();
    public Dictionary<string, TilePref> TilePrefs { get; set; } = new();   // arrangement-only clones (Name/Max null)
    public List<string> CollapsedGroups { get; set; } = new();
    public bool ShowCoreMatrix { get; set; } = true;
    [JsonConverter(typeof(DashboardLayoutModeConverter))]                  // REQUIRED — see DashboardLayoutMode.cs's
    public DashboardLayoutMode DashboardLayoutMode { get; set; } = DashboardLayoutMode.Auto; // converter remarks
    public double? CoreMatrixX { get; set; }
    public double? CoreMatrixY { get; set; }

    public static TilePref CloneArrangement(TilePref p);          // Kind, Size, X, Y copied; Name and Max left null
    public static LayoutProfile Capture(AppSettings s, string name);
    public void Restore(AppSettings s);
}
```

- `Capture(settings, name)`: `Name = name`; `DashboardMetrics`/`OverlayMetrics`/`CollapsedGroups` = `ToList()`
  copies; `TilePrefs` = every live entry cloned with `CloneArrangement` (entries for tiles not on the dashboard
  included); `ShowCoreMatrix`, `DashboardLayoutMode`, `CoreMatrixX/Y` copied. Pure; no side effects.
- `Restore(settings)`: writes **in place** (the three lists are `Clear()`+`AddRange`, never replaced — other code
  holds no cached references today, but keeping the instances means it never will matter). Then
  `ShowCoreMatrix`, `DashboardLayoutMode`, `CoreMatrixX/Y`. `TilePrefs`: remember every live `(id, Name, Max)`;
  `Clear()`; add `CloneArrangement` of every profile entry; then for each remembered `(id, name, max)` with
  `name is not null || max is not null`, `settings.PrefFor(id)` and set `Name`/`Max` (so an id the profile lacks
  keeps its display prefs on a fresh default pref, exactly like a channel absent from a fan profile reverts to a
  fresh `FanChannelPref` with its name kept). `Restore` never touches `ActiveLayoutProfile`,
  `LayoutProfileModified`, the Game-mode picks, `MetricLimits`, thresholds, `FpsHintDismissed`, overlay style or
  position, window bounds, or any `Fan*` field — the view model owns the active/modified pair and nothing owns
  the rest.
- Both are `Stats.Core`-pure and unit-tested on their own (Acceptance).

`AppSettings` — append a new section after `GraphEffects`:

```csharp
// ---- layout profiles ----
public List<LayoutProfile> LayoutProfiles { get; set; } = new();
/// Name of the profile the live arrangement was last loaded from or saved to; null = the implicit unnamed layout.
public string? ActiveLayoutProfile { get; set; }
/// True once a user arrangement edit happened after ActiveLayoutProfile was loaded/saved; meaningless while it is null.
public bool LayoutProfileModified { get; set; }
/// Layout profile applied on Game-mode enter / exit; null = leave the layout alone.
public string? GameModeGamingLayoutProfile { get; set; }
public string? GameModeDesktopLayoutProfile { get; set; }
```

`SettingsService.Normalize` — append after the `FanProfiles` block (mirror it line for line):

- `settings.LayoutProfiles ??= new(); settings.LayoutProfiles.RemoveAll(p => p is null);`
- per profile: `Name ??= ""`; each of the three lists `= (list ?? new()).Where(id => id is not null).ToList()`;
  `TilePrefs = (TilePrefs ?? new()).Where(kv => kv.Value is not null).ToDictionary(...)`; every profile
  `TilePref.X/Y` and the profile's `CoreMatrixX/Y` through the existing `SanitizePosition`.
- `if (settings.ActiveLayoutProfile is string a && !settings.LayoutProfiles.Any(p => p.Name == a)) settings.ActiveLayoutProfile = null;`
- `if (settings.ActiveLayoutProfile is null) settings.LayoutProfileModified = false;`
- The Game-mode layout picks are left as stored (assumed decision 11).

A pre-1.10 `settings.json` (no `LayoutProfiles`, `ActiveLayoutProfile`, …) loads with an empty list, null
active, false modified, null picks. A hand-edited `"DashboardLayoutMode": "Sideways"` inside a profile falls back
to `Auto` for that profile without discarding the file (the lenient converter is applied to the new property —
without it the global `JsonStringEnumConverter` would throw and `Load` would revert *every* setting to
defaults).

## Core (`Stats.Core`, testable, WPF-free)

### `GameModeSwitcher` — the transition signal (`src/Stats.Core/Fans/GameModeSwitcher.cs`)

- `public event Action<bool>? GamingChanged;` — raised **on the poll thread**, inside `Tick`, at most once per
  call, only when `IsGaming` actually transitioned in that call: `true` on enter, `false` on exit. Implementation:
  a local `bool? transition` set to `true`/`false` in the two existing transition branches (after `Apply(...)`
  returned and `IsGaming`/`_gamingSince` were latched — the existing lines are not reordered), then after the
  existing `UpdateStatus();` call: `if (transition is bool t) GamingChanged?.Invoke(t);`.
- Not raised from the `!GameModeEnabled` branch (disabling Game mode mid-game leaves both fans and layout as
  they are — today's fan behaviour).
- Because the state is latched before the event, a throwing subscriber (the poller already wraps `Tick` in
  try/catch) cannot cause a second fan apply on the next tick. `Apply(string?)` and its
  `ApplyProfile(prof, deferSave: true, resetFailures: false)` call stay byte-identical (rule 6).
- The event carries no profile name: the poll thread never reads `LayoutProfiles`; the UI thread resolves the
  pick (see `ApplyGameModeLayout`).

### `FansViewModel` — Game-mode layout picks (`src/Stats.Core/ViewModels/FansViewModel.cs`)

- New record next to `FanSourceOption`: `public sealed record LayoutProfileOption(string? Name, string Label)`
  with `ToString() => Label` (the same shape as `TrayMetricOption` in `SettingsViewModel.cs`).
- `public static readonly LayoutProfileOption NoLayoutProfile = new(null, "None (leave layout as is)");`
- `public ObservableCollection<LayoutProfileOption> LayoutProfileOptions { get; }` — `NoLayoutProfile` first,
  then one option per `settings.LayoutProfiles` entry in list order, label = name.
- `[ObservableProperty] LayoutProfileOption? _selectedGamingLayout`, `_selectedDesktopLayout`. Their
  `On…Changed(value)`: `if (_refreshingLayoutPicks || value is null) return;` then write `value.Name` to
  `_settings.GameModeGamingLayoutProfile` / `GameModeDesktopLayoutProfile` and `_saveSettings()`. A `null`
  can only come from WPF coercing a `SelectedItem` that is not in `ItemsSource` (a pick naming a deleted
  profile), never from the user (None is a real option object), so it is always ignored — no
  `IsComboBoxCoercion` dance needed. They do **not** raise `GameModeChanged` (nothing in the App depends on the
  layout picks; `GameMode_SettingsRoundTrip_AndStatus` keeps asserting 3 raises for the three fan settings).
- `private void SyncLayoutProfiles()` — called at the end of the constructor and from `Refresh()` (which
  `App.RunCoalescedRefresh` runs every coalesced tick while the Fans window is visible): reads
  `_settings.LayoutProfiles` names; if they differ from the current options (`SequenceEqual`), rebuilds
  `LayoutProfileOptions`; then, under `_refreshingLayoutPicks = true`, sets each `Selected…Layout` to the option
  whose `Name` equals the stored pick (`NoLayoutProfile` for null; `null` — blank ComboBox — for a pick naming a
  profile that no longer exists, leaving the stored pick untouched). Same guard pattern as
  `SetSelectedProfileQuietly`. Reading `_settings.LayoutProfiles` here is UI-thread only (the UI thread owns the
  settings graph; the poll thread never touches this list).
- `FansViewModel` still has no `DashboardViewModel` reference; profile creation/deletion is picked up through
  settings on the next `Refresh()`.

### `SettingsViewModel` — quiet mirror sync (`src/Stats.Core/ViewModels/SettingsViewModel.cs`)

- `private bool _syncing;` and `public void SyncShowCoreMatrix()` — sets `ShowCoreMatrix = _s.ShowCoreMatrix`
  with `_syncing = true` around it; `OnShowCoreMatrixChanged` gains `|| _syncing` on its existing `!_loaded`
  guard so the mirror update neither writes back, saves, nor raises `SettingsChange.CoreMatrix`. The
  `SettingsChange` enum is **not** extended (nothing new needs a composition-root reaction; the enum comment
  explains why dead members are not added).

### `DashboardViewModel` — new partial `src/Stats.Core/ViewModels/DashboardViewModel.Profiles.cs`

Same split as `DashboardViewModel.Layout.cs`. Everything below runs on the UI thread and mutates the settings
graph without a lock, exactly like every existing mutation site in `DashboardViewModel.cs` (serialization only
ever happens on the UI thread — `App.SaveSettings` — and the poll thread only takes `SyncRoot` for fan fields).

State (all read live from `_settings`, no duplicated collections):

- `public IReadOnlyList<string> LayoutProfileNames => _settings.LayoutProfiles.Select(p => p.Name).ToList();`
- `public string? ActiveLayoutProfileName => _settings.ActiveLayoutProfile;`
- `public bool IsLayoutModified => _settings.ActiveLayoutProfile is not null && _settings.LayoutProfileModified;`
- `public bool TryGetLayoutProfile(string name, out LayoutProfile? profile)` — ordinal `FirstOrDefault`.
- `public LayoutProfile SnapshotLayoutProfile(string name) => LayoutProfile.Capture(_settings, name);`
- `private void RaiseLayoutProfileState()` — `OnPropertyChanged` for `ActiveLayoutProfileName`,
  `IsLayoutModified`, `LayoutProfileNames`; `SaveActiveLayoutProfileCommand.NotifyCanExecuteChanged()`;
  `RevertLayoutProfileCommand.NotifyCanExecuteChanged()`.

Commands and methods (CommunityToolkit `[RelayCommand]`; a generated command on a non-nullable `string`
parameter already refuses `null` in `CanExecute`, which is what disables the menu items):

- `public bool ApplyLayoutProfile(string name)` — **the one apply path** (menu click, Revert, Game mode all
  go through it). Steps, in this order, and nothing else:
  1. `if (!TryGetLayoutProfile(name, out var profile)) return false;` (no save, no rebuild).
  2. `profile.Restore(_settings);`
  3. `_settings.ActiveLayoutProfile = profile.Name; _settings.LayoutProfileModified = false;`
  4. Re-sync the mirrors that are built once and never observe `AppSettings`:
     - `_layoutMode = _settings.DashboardLayoutMode;` (assign the backing field, as the constructor does —
       the `LayoutMode` setter's `OnLayoutModeChanged` would persist + rebuild + save a second time) then
       `OnPropertyChanged` for `LayoutMode`, `IsAutoLayout`, `IsGridLayout`.
     - `_suppressPickerEvents = true; try { foreach item in PickerItems: item.IsChecked = DashboardMetrics.Contains(id); item.IsOnOverlay = OverlayMetrics.Contains(id); } finally { _suppressPickerEvents = false; }`
       (the `SelectAllInGroup` pattern — otherwise each flip would rebuild + save).
     - `SettingsPanel?.SyncShowCoreMatrix();`
     - `_tilesSeededBelowUnmeasuredBlock.Clear();` (that list describes the arrangement being left).
  5. `RebuildSections();` — once. It re-creates every tile/section from the restored settings, re-raises
     `ShowFpsHint`/`IsEmpty`, recomputes the canvas extent, and — only when the profile's mode is Free/Grid and
     some dashboard tile has a null position — runs the seed pack, which saves once by itself
     (`_seedPackSavedLastRebuild`).
  6. `DashboardMetricsChanged?.Invoke(); OverlayMetricsChanged?.Invoke();` — the composition root already
     fans these out (`_peaksVm?.RebuildRows()`, `_overlayVm.Rebuild()`, `ApplyFrameTracing()` — the latter twice,
     harmless: `FrameRateReader.SetActive` returns early when the state is unchanged).
  7. `RaiseLayoutProfileState(); if (!_seedPackSavedLastRebuild) _saveSettings(); return true;`
  Net effect per owner decision 4: one settings mutation, one `RebuildSections`, one overlay rebuild, one save.
  No `Fan*` field is read or written anywhere on this path (rule 6; asserted by a test).
- `[RelayCommand] private void LoadLayoutProfile(string name) => ApplyLayoutProfile(name);` — the checkable
  menu items bind to `LoadLayoutProfileCommand`.
- `[RelayCommand] private void SaveLayoutProfile(string? name)` — `IsNullOrWhiteSpace` → return; trim;
  `Capture`; replace in place when a profile of that name exists (`FindIndex` + index assignment, keeps list
  order — same as `FanController.AddOrReplaceProfile`) else append; set active = name, modified = false;
  `RaiseLayoutProfileState()`; `_saveSettings()` once. Never rebuilds (the live layout did not change).
- `[RelayCommand(CanExecute = nameof(IsLayoutModified))] private void SaveActiveLayoutProfile()` —
  `SaveLayoutProfile(_settings.ActiveLayoutProfile)`.
- `[RelayCommand(CanExecute = nameof(IsLayoutModified))] private void RevertLayoutProfile()` —
  `ApplyLayoutProfile(_settings.ActiveLayoutProfile!)`.
- `[RelayCommand] private void DeleteLayoutProfile(string name)` — `RemoveAll(p => p.Name == name) == 0` →
  return (no save); if it was the active: active = null, modified = false (the live arrangement is **not**
  rebuilt or changed — it simply becomes the implicit unnamed layout); null either Game-mode layout pick equal
  to `name` (mirrors `FansViewModel.DeleteProfile`); `RaiseLayoutProfileState()`; `_saveSettings()` once.
- `public void RenameLayoutProfile(string oldName, string? newName)` — blank → return; trim; equal to
  `oldName` → return; a profile named `newName` exists → return (no silent merge); `oldName` not found → return;
  else set `profile.Name`, and if active or either Game-mode pick equals `oldName`, follow it;
  `RaiseLayoutProfileState()`; `_saveSettings()` once. Plain method (two arguments) called from the
  `DashboardWindow` code-behind after the dialog.
- `public bool MarkLayoutModified()` — `if (_settings.ActiveLayoutProfile is null || _settings.LayoutProfileModified) return false;`
  else set the flag, `RaiseLayoutProfileState()`, return `true`. **Never saves** — every caller below already
  saves right after; the `App` caller saves when it returns `true`.
- `public void ApplyGameModeLayout(bool gaming)` — UI thread only (the App marshals):
  `var name = gaming ? _settings.GameModeGamingLayoutProfile : _settings.GameModeDesktopLayoutProfile;`
  `if (string.IsNullOrWhiteSpace(name)) return;` (None) ·
  `if (_settings.ActiveLayoutProfile == name && !_settings.LayoutProfileModified) return;` (already live) ·
  `ApplyLayoutProfile(name);` (unknown name → no-op inside).

Modified tracking — `MarkLayoutModified()` is inserted **immediately before the existing `_saveSettings()`** at
exactly these user-driven sites and nowhere else:

| File | Site |
| --- | --- |
| `DashboardViewModel.cs` | `SelectAllInGroup`; both branches of `OnPickerItemChanged` (`IsChecked` and `IsOnOverlay`); `MoveTile`; `SetTileKind` and `SetTileSize` (call it in those two methods, **not** inside the shared `AfterPrefChange`, which `SetTileMax`/`RenameTile` also use); `OnSectionExpandedChanged` (inside `if (changed)`) |
| `DashboardViewModel.Layout.cs` | `OnLayoutModeChanged` (before the conditional save — it must run even when the seed pack saved); `ResetPositions`; `SetTilePosition`; `SetCoreMatrixPosition` |
| `App.xaml.cs` | `case SettingsChange.CoreMatrix:` (see App) |

Not marked (by design): `RenameTile`, `SetTileMax`, `SetTileThresholds`, `DismissFpsHint`, `PlaceUnpositioned`
(seed pack), `SetCoreMatrixSize` (the block's measured-height fix-up), `RebuildSections`, the constructor, and
`ApplyLayoutProfile`/`SaveLayoutProfile` themselves. With no active profile every site costs one null check.

## App (`Stats.App`)

### `App.xaml.cs`

- Right after `_dashboardVm` is constructed (`OnStartup`, currently lines 167-171; `_gameMode` already exists
  from line 165): `_gameMode.GamingChanged += gaming => Dispatcher.BeginInvoke(() => _dashboardVm?.ApplyGameModeLayout(gaming));`
  — the same marshal as `_poller.HealthChanged` (line 255). Nothing else on the poll-thread side changes: the
  two `SnapshotAvailable` subscriptions and the `GameModeSwitcher.Tick` → `FanController.Tick` order stay as
  they are.
- `OnSettingsChanged`, `case SettingsChange.CoreMatrix:` becomes
  `if (_dashboardVm?.MarkLayoutModified() == true) SaveSettings(); _dashboardVm?.RebuildSections(); break;`
  (the `SettingsViewModel.Raise` save already ran before `Changed` fired, so the flag needs its own save; it
  costs one extra tmp+move only when the flag actually flips).
- No other change: `SaveSettings`/`RequestSaveSettings`, `ApplyFrameTracing`, `SetupTray`, `ShowFans`,
  `RunCoalescedRefresh`, `OnExit` (`poller.Stop()` → `RestoreAll()` → dispose) are untouched.

### `DashboardWindow` (`src/Stats.App/Views/DashboardWindow.xaml` + `.xaml.cs`)

- `ViewButton_Click`: after the existing "Reset tile positions…" item append `new Separator()` and
  `BuildLayoutProfilesMenu(vm)`. Existing items are not touched.
- `private MenuItem BuildLayoutProfilesMenu(DashboardViewModel vm)` — a `MenuItem` whose `Items` form the
  submenu (a `MenuItem` with children opens as a submenu of the code-built `ContextMenu`). Everything is computed
  at open time, like `IsEnabled = vm.IsAutoLayout` today:
  - `Header`: `"Layout profiles"` when `vm.ActiveLayoutProfileName is null`; `$"Layout profile: {name}"`;
    `$"Layout profile: {name} (modified)"` when `vm.IsLayoutModified`.
  - One item per `vm.LayoutProfileNames` (list order): `IsCheckable = true`,
    `IsChecked = name == vm.ActiveLayoutProfileName`, `Command = vm.LoadLayoutProfileCommand`,
    `CommandParameter = name` (the `LayoutModeItem` shape, but bound through `Command` so the name is captured
    per item). When the list is empty: one disabled item `"No saved layouts yet"`.
  - `Separator`.
  - `"Save"` — `Command = vm.SaveActiveLayoutProfileCommand` (disabled unless modified).
  - `"Revert"` — `Command = vm.RevertLayoutProfileCommand` (disabled unless modified).
  - `"Save as…"` — `Click` → `InputDialog.Show(this, "Save layout profile", "Layout name:", vm.ActiveLayoutProfileName ?? "")`;
    null/whitespace → return; else `vm.SaveLayoutProfileCommand.Execute(result.Trim())` (the
    `FansWindow.SaveAs_Click` flow verbatim).
  - `"Rename…"` — `IsEnabled = vm.ActiveLayoutProfileName is not null`; `Click` →
    `InputDialog.Show(this, "Rename layout profile", "New name:", active)`; null/whitespace → return; else
    `vm.RenameLayoutProfile(active, result)`.
  - Delete — `Header = active is null ? "Delete" : $"Delete \"{active}\""`, `Command = vm.DeleteLayoutProfileCommand`,
    `CommandParameter = active` (null → the generated `CanExecute` disables it — the `FansWindow.ProfileMenu_Click`
    pattern). Targets the active profile only; to delete another one, check it first.
- `CommitFreeDrag` and `CommitCoreDrag`: after the threshold check and before the VM write, add
  `if (PresentationSource.FromVisual(container) is null) return;` — a container torn down by a profile apply that
  rebuilt the canvas mid-drag (Game mode firing while the mouse is captured) has nothing left to commit;
  `SetTilePosition` would otherwise find the *new* tile with the same id and write the stale drag position into
  the freshly applied profile.
- `DashboardWindow.xaml` line 113: `AutomationProperties.Name="View: layout mode, layout profiles, collapse or expand sections"`
  (the old "collapse or expand all sections" text was already stale after layout modes).

### `FansWindow` (`src/Stats.App/Views/FansWindow.xaml`)

In the Game-mode `StackPanel` (currently lines 128-145), directly after the existing Gaming/Desktop `WrapPanel`,
add a second `WrapPanel` with the same margins: `TextBlock "Gaming layout:"` + `ComboBox ItemsSource="{Binding LayoutProfileOptions}" SelectedItem="{Binding SelectedGamingLayout}" MinWidth="150"`,
`TextBlock "Desktop layout:"` + the Desktop ComboBox, then a dense secondary-text line
`"Layout profiles are saved from the dashboard's View menu and switch together with the fan profiles above."`
No code-behind change. `FansViewModel` exposes the options/picks; the window is otherwise untouched.

### `README.md`

- "Fan profiles & game mode" bullet: one sentence — Game mode can also switch a **Gaming layout** / **Desktop
  layout** (layout profiles saved from the dashboard's View menu); **None** leaves the layout alone.
- "Dashboard layout" bullet: a new **Layout profiles** sub-bullet — what a profile captures (selection, order,
  sizes/kinds, positions, collapsed groups, core matrix, layout mode, overlay selection; *not* tile names or
  gauge maxes), the View ▸ Layout profiles submenu (check to apply, Save / Revert / Save as… / Rename… /
  Delete), the "(modified)" marker, that switching never touches fans, and that a game-mode switch replaces
  unsaved edits to the profile being left.

## Preview harness (`tools/Stats.UiPreview`)

`PreviewComposition.Build` changes (mirroring `App.OnStartup` so the harness exercises the real fan-out):

- Hoist the inline `new GameModeSwitcher(fanController, settings)` into a local, expose it as
  `public required GameModeSwitcher GameMode { get; init; }`, and wire
  `gameMode.GamingChanged += gaming => dashboard.ApplyGameModeLayout(gaming);` — synchronous, no dispatcher (the
  substate ticks the switcher on the same thread).
- Wire `dashboard.OverlayMetricsChanged += overlay.Rebuild;` and `dashboard.DashboardMetricsChanged += peaks.RebuildRows;`
  right after both view models exist (App lines 188/214). No existing substate raises either event, so nothing
  already captured changes.

Substates (all pure VM/settings, applied in `ApplySubstate` before any window exists, so every capture is at
rest per the graph-effects constraint; registered in `SubstateCatalog.ByView`):

- Shared helper `SeedLayoutProfiles(c)`: (1) `c.Dashboard.SaveLayoutProfileCommand.Execute("Desk")` — the
  scenario's own Auto arrangement; (2) build a gaming arrangement through public paths only:
  `c.Dashboard.LayoutMode = DashboardLayoutMode.Grid`, `SelectAllInGroup(groupName, false)` for every picker
  group whose `Definition.Group` is not Cpu/Gpu/Game, then for each `PickerItems` entry
  `IsOnOverlay = id is FrameMetrics.FpsId or "gpu.temp.core"`; `SaveLayoutProfileCommand.Execute("Gaming")`;
  (3) `c.Dashboard.ApplyLayoutProfile("Desk")` — back on Desk, both profiles stored.
- `dashboard`: `layout-profile-desk` (seed only — Auto, every fixture tile, `Desk` active, not modified);
  `layout-profile-gaming` (seed + `ApplyLayoutProfile("Gaming")` — Grid canvas with only Cpu/Gpu/Game tiles);
  `layout-profile-modified` (gaming + `SetTilePosition(Tiles[0].Definition.Id, 600, 40)` — the flag is set, one
  tile visibly displaced); `layout-profile-game-mode` (seed; `Settings.GameModeEnabled = true`,
  `GameModeGamingLayoutProfile = "Gaming"`, `GameModeDesktopLayoutProfile = "Desk"`, fan picks left null; then
  `TickGameMode(c, fps: 120f, from: 0, to: 5)` — `c.GameMode.Tick(new SensorSnapshot({ [FrameMetrics.FpsId] = fps }, FixedTimeUtc + t s), FixedTimeUtc + t s)`
  the way `TickFan` drives the controller — so `GamingChanged(true)` fires and lands the dashboard on `Gaming`
  through the real signal path; `FanController.Tick` is never driven, so no `fan.*` command is recorded).
- `overlay`: `layout-profile-gaming` (same helper; the wired `Rebuild` shows the gaming overlay selection).
- `fans`: `game-mode-layouts` (seed through `c.Dashboard`, set the three Game-mode settings, `c.Fans.Refresh()` so
  `SyncLayoutProfiles` populates the new row with `Gaming`/`Desk` selected).
- `input-dialog`: `layout-save-as` (`CaptureHost.BuildInputDialog` takes the spec and, for this substate, sets
  `Title = "Save layout profile"`, prompt `"Layout name:"`, initial `"Gaming"` — the one dialog of this feature
  the harness *can* show).

`baseline.json` — append, all `"Method": "rtb"`, output prefix `artifacts/layout-profiles/`:

| Scenario | View | Substate | Themes | Size | Output |
| --- | --- | --- | --- | --- | --- |
| dense | dashboard | layout-profile-desk | Dark Amber, Light | 1180×720 | `v14-dashboard-layout-profile-desk-<theme>.png` |
| dense | dashboard | layout-profile-gaming | Dark Amber, Light | 1180×720 | `v14-dashboard-layout-profile-gaming-<theme>.png` |
| dense | dashboard | layout-profile-modified | Dark Amber | 1180×720 | `v14-dashboard-layout-profile-modified-dark-amber.png` |
| dense | dashboard | layout-profile-game-mode | Dark Amber | 1180×720 | `v14-dashboard-layout-profile-game-mode-dark-amber.png` |
| dense | overlay | layout-profile-gaming | Dark Amber | 400×200 | `v14-overlay-layout-profile-gaming-dark-amber.png` |
| fans | fans | game-mode-layouts | Dark Amber | 760×640 | `v14-fans-game-mode-layouts-dark-amber.png` |
| normal | input-dialog | layout-save-as | Dark Amber | 420×200 (ignored for dialogs) | `v14-input-dialog-layout-save-as-dark-amber.png` |

What the harness **can** show: the post-apply dashboard (Auto vs Grid, tile set, positions, core matrix), the
post-apply overlay tile set, the Fans window's new picker row with closed ComboBoxes showing their selected
text, the Save-as dialog's strings, and — through the harness tests — every VM/settings assertion (active name,
modified flag, save count, command log containing only `settings.save`).

What it **cannot** show (per `docs/ui-polish/PREVIEW_HARNESS.md` "a RenderTargetBitmap of the main visual does
not include every popup HWND", and `docs/layout-modes/EVIDENCE.md` recording that `--method screen` came back
blank in this environment): the View menu and its Layout profiles submenu (its header text, check marks,
enabled/disabled Save/Revert/Rename/Delete), the open ComboBox dropdown listing the layout options, the
Rename/Delete flows, real hysteresis timing and poll-thread marshalling, the overlay's `SizeToContent` resize at
a transition, the mid-drag switch, PresentMon, and persistence across a restart. All of these are owner-checklist
items.

## Non-goals

No import/export of layout profiles; no per-monitor or per-resolution profiles; no profiles for the Peaks or
Fans window layout; no change to how fan profiles work (`FanController`, `FanProfile`, `FansViewModel`'s fan
half and `GameModeSwitcher.Apply` are byte-identical); no overlay orientation/font scale/opacity/position in a
profile; no "Create default layouts"; no header badge, tray submenu, or confirmation dialogs; no layout-only
Game-mode switch; no status-text note for a missing layout pick; no new `SettingsChange` member; no new NuGet
packages (H.NotifyIcon.Wpf stays 2.3.2).

## Acceptance

- `dotnet build --nologo` 0 warnings; `dotnet test --nologo` green, 0 warnings (xUnit analyzers: constants in the
  `expected` slot).
- **Tests — `tests/Stats.Core.Tests/LayoutProfileTests.cs` (new, T1; pure `Capture`/`Restore`):**
  `Capture_DeepCopiesEveryArrangementField_AndOmitsTileNameAndMax` (mutating the snapshot never touches
  settings; `Name`/`Max` null in the copy); `Capture_IncludesTilePrefsOfTilesNotOnTheDashboard`;
  `Restore_WritesEveryArrangementField_InPlace_KeepingListInstances` (`ReferenceEquals` on the three lists
  before/after); `Restore_PreservesLiveTileNameAndMax_AndDropsArrangementOfIdsTheProfileLacks`;
  `Restore_DoesNotTouchFanFieldsLimitsThresholdsOverlayStyleOrActiveProfile`;
  `Capture_ThenRestore_RoundTrips_ArrangementFields`.
- **`SettingsServiceTests.cs` (T1):** `Load_MissingFile_LayoutProfilesEmpty_ActiveAndPicksNull_ModifiedFalse`;
  `SaveThenLoad_LayoutProfiles_RoundTrip_WithStringEnumAndPositions`; `Load_NullLayoutProfilesList_BecomesEmpty`;
  `Load_NullLayoutProfileElement_IsDropped`; `Load_NullLayoutProfileName_AndNullCollections_BecomeEmpty`;
  `Load_NullTilePrefInsideLayoutProfile_IsDropped`; `Load_SanitizesPositionsInsideLayoutProfiles` (NaN/negative/
  > 100 000 → null for tile and core-matrix positions); `Load_UnknownLayoutModeInsideProfile_FallsBackToAuto_WithoutWipingOtherSettings`;
  `Load_ActiveLayoutProfileNamingNoProfile_BecomesNull_AndModifiedFalse`;
  `Load_GameModeLayoutPicksNamingMissingProfiles_AreKept`; `Load_PreLayoutProfilesFile_LoadsWithDefaults` (a
  v1.9-shaped JSON without any of the five fields).
- **`GameModeSwitcherTests.cs` (T1):** `GamingChanged_RaisedTrueOnEnter_FalseOnExit_OncePerTransition`
  (`[true]` after the 5 s tick, `[true, false]` after 20 s inactive, no duplicates over 60 further gaming ticks);
  `GamingChanged_ObservesLatchedIsGaming_AndAppliedFanProfile` (inside the handler `IsGaming` is `true` and
  `ActiveFanProfile == "Gaming"`); `GamingChanged_NotRaised_WhileDisabled_NorWhenDisabledMidGame`;
  `GamingChanged_ThrowingSubscriber_DoesNotUnlatchOrReapplyNextTick` (handler throws once; `Tick` propagates;
  next tick leaves an edited `ManualPercent` alone). All existing switcher tests unchanged and green.
- **`FansViewModelTests.cs` (T1):** `LayoutProfileOptions_StartWithNone_ThenSettingsProfilesInOrder`;
  `SelectedGamingLayout_WritesThroughAndSavesOnce_NoneStoresNull`; `SelectedDesktopLayout_WritesThroughAndSavesOnce`;
  `SelectedLayout_NullFromComboBoxCoercion_IsIgnored_KeepsTheConfiguredName`;
  `Refresh_ResyncsLayoutOptionsAndPicks_FromSettings_WithoutWritingBack` (add a profile to settings behind the
  VM's back, `Refresh()`, options grow, save count unchanged); `GameMode_SettingsRoundTrip_AndStatus` still
  asserts exactly 3 `GameModeChanged` raises.
- **`tests/Stats.Core.Tests/LayoutProfileViewModelTests.cs` (new, T2; `Make` helper like
  `DashboardLayoutModeTests` with a save counter and a `Sections.CollectionChanged` Reset counter):**
  `SaveLayoutProfile_AddsName_SetsActive_ClearsModified_SavesOnce_NoRebuild`;
  `SaveLayoutProfile_ExistingName_ReplacesInPlace_KeepsListOrder`; `SaveLayoutProfile_BlankName_IsIgnored_NoSave`;
  `ApplyLayoutProfile_RestoresSelectionOrderModeCollapsedCoreMatrixAndPositions`;
  `ApplyLayoutProfile_RebuildsOnce_RaisesEachMetricsEventOnce_SavesOnce`;
  `ApplyLayoutProfile_FreeProfileWithAnUnplacedTile_SeedsIt_StillSavesOnce`;
  `ApplyLayoutProfile_ResyncsPickerItemsAndLayoutModeFlags_WithoutFiringTheirHandlers` (save count stays 1);
  `ApplyLayoutProfile_SyncsSettingsPanelShowCoreMatrix_WithoutRaisingChanged`;
  `ApplyLayoutProfile_PreservesTileNameAndMax`; `ApplyLayoutProfile_UnknownName_ReturnsFalse_NoSave_NoRebuild`;
  `ApplyLayoutProfile_NeverTouchesFanChannelsActiveFanProfileOrFanControlEnabled` (seeded fan state
  reference-equal and value-equal afterwards — rule 6);
  `ApplyLayoutProfile_LeavesTheStoredProfileOfTheLayoutBeingLeftIntact` (save `Desk`, edit, apply `Gaming`,
  the stored `Desk` still equals the original snapshot); `MarkLayoutModified_OnlyWhileAProfileIsActive_AndOnlyFlipsOnce`;
  `ArrangementEdits_MarkModified` (`[Theory]` over picker check/uncheck, overlay toggle, `SelectAllInGroup`,
  `MoveTile`, `SetTileKind`, `SetTileSize`, section collapse, `LayoutMode` change, `ResetPositionsCommand`,
  `SetTilePosition`, `SetCoreMatrixPosition`); `NonArrangementEdits_DoNotMarkModified` (`RenameTile`,
  `SetTileMax`, `SetTileThresholds`, `SetCoreMatrixSize`, the constructor's seed pack, `DismissFpsHint`);
  `RevertLayoutProfile_ReappliesActive_ClearsModified_CanExecuteFollowsModified`;
  `SaveActiveLayoutProfile_OverwritesActive_ClearsModified_CanExecuteFollowsModified`;
  `DeleteLayoutProfile_Active_FallsBackToUnnamed_LeavesLiveLayoutAlone_ClearsPicks_SavesOnce`;
  `DeleteLayoutProfile_NonActive_LeavesActiveAlone`; `DeleteLayoutProfile_Unknown_IsNoOp_NoSave`;
  `RenameLayoutProfile_UpdatesListActiveAndPicks_SavesOnce`; `RenameLayoutProfile_BlankSameOrCollidingName_IsNoOp`;
  `ApplyGameModeLayout_AppliesGamingThenDesktop_NonePickLeavesLayoutAlone`;
  `ApplyGameModeLayout_SkipsWhenAlreadyLiveAndUnmodified_ReappliesWhenModified`;
  `ApplyGameModeLayout_MissingProfile_IsNoOp_NoSave`; `LayoutProfileNames_FollowSettingsInListOrder`.
- **`SettingsViewModelTests.cs` (T2):** `SyncShowCoreMatrix_UpdatesMirror_WithoutSavingOrRaisingChanged`.
- **`tests/Stats.UiPreview.Tests/LayoutProfileSubstateTests.cs` (new, T4; built through
  `PreviewComposition.Build` without a window, like `DashboardLayoutSubstateTests`):**
  `SubstateCatalog_AcceptsLayoutProfileSubstates_ForTheirViews_AndRejectsThemElsewhere`;
  `LayoutProfileDesk_EndsOnDesk_AutoMode_EveryFixtureTile_NotModified`;
  `LayoutProfileGaming_EndsOnGaming_GridMode_OnlyCpuGpuGameTiles_OverlayIsFpsAndGpuTemp_NotModified`;
  `LayoutProfileModified_SetsTheFlag_AndSaveRevertCanExecute`;
  `LayoutProfileGameMode_LandsOnGamingViaTheSwitcher_ThenDeskAfterTwentySecondsInactive` (continue ticking
  `c.GameMode` with null fps to 26 s inside the test);
  `LayoutProfileGaming_OverlayView_RebuildsOverlayTiles` (`c.Overlay.Tiles` ids equal the gaming overlay set);
  `FansGameModeLayouts_SelectsBothPicks_AndOptionsListNoneFirst`;
  `LayoutProfileSubstates_RecordOnlySettingsSaves` (no `fan.*` entries — the switcher ran with null fan picks);
  `CompositionIsolationTests.CommandLog_…` keeps passing with its unchanged `AllowedCommandVerbs`.
- **Captures:** the seven `baseline.json` rows above, run through a scratch manifest with `--batch` and
  `--method rtb` (as `docs/layout-modes/EVIDENCE.md` did), every sidecar's `Warnings` empty; plus the existing
  `dense` Auto dashboard re-captured to show `layout-profile-desk` is pixel-identical to it. Recorded in
  `docs/layout-profiles/EVIDENCE.md` (per `docs/ui-polish/EVIDENCE_TEMPLATE.md`).
- **Owner checklist (cannot be proven from this shell):**
  1. View ▸ Layout profiles: **Save as…** creates and checks the name; the submenu header, check marks and
     Save/Revert/Rename/Delete enablement look right in every state (none / active / modified).
  2. Build a Free-mode `Gaming` and an Auto `Desk` layout with different tile sets, sizes, kinds, collapsed groups,
     core-matrix visibility and overlay selection; switch back and forth several times — everything returns
     exactly, positions included, and the profile being left is intact when you come back.
  3. Every arrangement edit shows "(modified)"; **Save** clears it; **Revert** restores; renaming a tile or setting
     a gauge max does *not* show modified and survives a switch; **Delete** of the active profile unchecks
     everything and leaves the layout in place.
  4. Fans ▸ Game mode: with the fan picks empty and `Gaming`/`Desk` picked as layouts, launch Stats from the
     Start-menu shortcut (PresentMon needs it) and a game: ~5 s after ≥ 10 fps the dashboard and overlay switch
     to `Gaming`, ~20 s after quitting back to `Desk`; the Fans window shows no channel change; the overlay
     resizes once and keeps click-through / move mode.
  5. Start a Free-mode drag and let a game-mode transition land mid-drag: no exception, the drop is discarded.
  6. Restart mid-game: the gaming layout loads and is checked; a v1.9.2 `settings.json` loads with nothing
     checked and no picks.
  7. Idle CPU unchanged (Task Manager, ~1 % or less) — nothing in this feature runs between ticks.
