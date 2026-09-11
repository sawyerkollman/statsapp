# Windows visual preview harness — implementation contract

This document specifies a harness for Codex to build in T1. No preview executable is included in the preparation package.

## Purpose and architecture

Render the actual Stats WPF views with deterministic simulated data before and after UI changes. Source inspection alone cannot establish typography, resource resolution, clipping, focus, popups, or live themes.

Prefer a separate development executable at `tools/Stats.UiPreview`, targeting the existing .NET 8 Windows WPF stack, with its own `asInvoker` startup. Reference reusable real views/resources and Core types as the architecture permits. Do not construct `Stats.App.App`, execute its production composition root, load its startup URI, or launch its requireAdministrator executable to get a preview. Keep production elevation and installer behavior unchanged.

Inspect resource dependencies first: Theme, Controls, TileTemplates, and application-level styles must resolve in correct order. If sharing currently App-only resources requires extraction to a dictionary, the parent owns that limited change. Avoid copying an entire second UI that can diverge from production. Using fake view models is acceptable where view contracts permit, but prefer real view models backed by fake services for meaningful command checks. Report what is real and what is mocked.

## Isolation requirements

- Create a separate temporary settings/log/output root per run; never read/write the user's live settings by default. Compatibility fixtures are copies.
- Fake sensor-reader/poller input. No LibreHardwareMonitor device access, PawnIO dependency, ETW/PresentMon launch, startup task changes, update download/install, autostart registration, or tray singleton from the production app.
- Fake fan channels/controllers/services. All commands record intended effects to a fixture log; they cannot reach a hardware service. Even Enable, Identify, and All to Auto remain simulated.
- Record external side-effect attempts; fail the run if a production side-effect dependency is resolved. Add a meaningful test proving the preview composition cannot resolve real hardware/startup/update services through its supported paths.
- Mark the harness window and exported evidence manifest as simulated. The real production UI need not acquire preview-only labels.
- Freeze fixture time, random seed, culture, and sample step for repeatable exports. Realtime preview playback can exist separately, but captured state must be deterministic.
- Close windows and dispose preview services/timers after export. A fixture run must not leave a background monitoring process.

## Scenarios

Fixture names below are the proposed stable interface. Values are illustrative, never readings of the user's PC. Store explicit definitions, units, timestamps, warn/crit rules, histories, and states; do not depend on device discovery. Use a fixed reference date/time and seeded data. Validate fixtures against the actual model constraints.

| Scenario | Content and edge cases |
| --- | --- |
| `normal` | CPU 62 °C / 24% / 4.85 GHz / 72 W; GPU 56 °C / 42% / 135 W; memory 18.4 of 32 GiB; storage 12%; network receive 12.4 MB/s; game 144 FPS / 92 1% low / 6.9 ms |
| `dense` | 40 mixed tiles, all S/M/L and kinds, 16-core matrix, multiple disks/adapters, long hardware and sensor names |
| `thresholds` | Regular warning/critical CPU or GPU temperatures; FPS 45 and 25 with existing 60/30 inverted thresholds; 1% lows on their own 30/15 scale; transitions and glyphs |
| `missing` | No current sample, sample-history gaps, unavailable sensor, read-failure/degraded status, no foreground game reason |
| `empty` | No selected dashboard metrics, picker no results, no Peaks rows, empty Alerts, no available fan channels |
| `fans` | Simulated Auto, Manual, Curve, pump channel, modified profile, game-mode state, master off/on, conflict and recovery notices |
| `settings` | Every category populated; invalid threshold/limit/hotkey, restart required, startup checking/error, update checking/progress/error |

Expose substate selection or separate fixture variants where a single screenshot cannot show mutually exclusive states. All charts use semantically valid inputs; absent data stays absent. Include zero and high numeric values, 100%, negative temperature if supported, and long unit/throughput formatting to test value width.

## Proposed command interface

T1 should implement and document this interface or record an equivalent explicit command mapping in the evidence report:

`dotnet run --project tools/Stats.UiPreview -- --scenario normal --view dashboard --theme "Dark Amber" --width 1180 --height 720 --ui-scale 1.0 --output artifacts/ui-polish/before/normal-dashboard.png`

Additional views: `picker`, `settings`, `fans`, `peaks`, `alerts`, `details`, `overlay`, `threshold-dialog`. Additional options should select relevant substate and fixed fixture timestamp. This command is a requirement for future implementation, not a currently runnable repo command.

Width/height are logical WPF units. Actual Windows DPI and app UI scale are separate test axes. Do not fake a DPI pass by just multiplying image dimensions; record actual environment DPI or the validated per-monitor/rendering method used.

## Capture behavior

Create the real view on an STA thread, load shared resources, bind fixtures, show/measure/arrange, and wait for a settled dispatcher/render cycle before capture. Flag binding/resource exceptions. Use an actual visible-window capture when validating native title bars, dropdowns, context menus, or tooltips; a RenderTargetBitmap of the main visual does not include every popup HWND. RenderTargetBitmap can supplement deterministic content-layout checks when correctly sized and DPI-aware.

Export PNG plus a JSON sidecar with: source commit; dirty-diff identity if any; harness version; scenario/substate; view; theme/accent; logical dimensions; physical image dimensions; DPI; UI scale; culture; fixed time/seed; exact launch command; screenshot method; warnings; simulated services. Never include real personal sensor logs accidentally.

## Validation scope

Preview proves visual layout and fixture-backed view behavior. It does not prove physical fan safety, ETW permission, hardware discovery, real monitoring overhead, real tray integration, or GPU-specific behavior. Existing automated tests and focused Windows runtime checks address those separately. If no Windows environment is available, implement what can be built/reviewed, preserve the runnable instructions, and label screenshot gates pending. Browser/HTML approximations are not WPF acceptance evidence.
