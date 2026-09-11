# Stats UI polish specification

## 1. Intent and boundaries

Deliver a cohesive, compact Windows instrument panel: clear measurements, quiet surfaces, consistent spacing, and obvious interaction states. Preserve the customizable monitoring workflow. This is a source-informed proposal; no reference screenshots were available. T1 captures the existing runtime UI before visual changes and gives the orchestrator evidence to refine dimensions.

Include the dashboard, picker/settings, tiles, fan window, Peaks/Alerts, details, and consistency touches on the overlay/dialogs. Do not add telemetry products, sensors, dashboard presets, theme names, account features, a sidebar navigation system, or a framework migration. No new continuously animated decoration. Preserve all existing controls and commands unless this document explicitly relocates them.

## 2. Shared visual contract

Dimensions below are WPF device-independent units before application UI scaling. Use named resources, consistent resource ownership, layout rounding, and pixel snapping for suitable borders. Do not apply scale twice.

| Element | Starting target |
| --- | --- |
| Typeface | Segoe UI; numeric values use tabular figures where supported |
| Window/page title | 20, Semibold |
| Group/section heading | 14, Semibold |
| Body, buttons, settings labels | 13 |
| Tile label, supporting text | 12; 11 minimum for dense metadata |
| Main medium/large tile value | 28, Semibold; 24 minimum for long readings |
| Compact tile value | 22, Semibold |
| Unit next to value | 13–14, visually secondary |
| Spacing scale | 4 / 8 / 12 / 16 / 24 |
| Main content inset | 16 |
| Tile gap | 12 total between neighboring tiles |
| Tile padding | 12; compact may use 10 |
| Section separation | 24; header-to-content 8 |
| Corner radius | Tiles/panels 8; buttons/inputs 4 |
| Controls | 32 minimum height; dense secondary rows may use 28 |
| Borders | 1-unit quiet border when needed for interaction or separation |

Do not scale every reading down to accommodate an oversized gauge. Numbers are the primary content; gauges are supporting content. Keep text usable at the existing 0.9 UI scale and at supported Windows DPI settings. Use stable numeric widths; do not animate digits or rebuild full tile controls each poll.

### Palette and states

Retain the existing Dark Amber, Blue, Green, Purple, Light, and custom accent behavior and stored names. Dark starting surfaces may remain Window `#1B1B1C`, Tile `#252528`, Flyout `#2B2B2F`, Control `#303035`. The improvement is hierarchy and consistency, not mandatory recoloring.

- Use primary text for normal readings; quiet text for labels and min/max.
- Use the accent for selected controls, focus, and chart traces. Group headings should generally use primary text, with restrained accent details if helpful.
- Keep warn/critical severity glyphs alongside color. Dark Amber inherently resembles warning amber; therefore warning states also need a glyph and explicit status/tooltip, not just a hue change. Do not promise arbitrary custom accents can be made semantically distinct by color alone.
- Keep existing light-theme semantic colors and the overlay's pinned dark-safe colors.
- Set project acceptance targets of 4.5:1 contrast for small essential text and 3:1 for essential control boundaries/focus indicators. Measure final resolved colors; do not assume palette names prove contrast. User-selected low-contrast accents should not make labels unreadable.
- Hover: subtle surface/border change. Pressed: stronger local fill. Focus: visible outline, independent of hover. Selected/toggled: persistent fill/indicator and accessible state. Disabled: recognizable without hiding the label.
- Reserve room for tile options so the hover button never covers the label, period label, value, or severity icon. Show it on keyboard focus too. Keep keyboard context-menu access.
- Use a consistent 16-unit vector icon set, with 20-unit icons only where needed. Prefer local XAML geometries in one resource dictionary; no emoji or downloaded icon-font dependency. Keep accessible names on icon-only controls.

### Tile sizing

Preserve serialized S/M/L choices. Starting rendered dimensions: S 160×80, M 224×144, L 460×192. L spans two M columns plus their 12-unit gap. These are target sizes, not a migration of user preferences. The orchestrator can adjust after the T1/T3 captures; record the reason, and maintain consistent column relationships. Fit values, units, severity, and menu affordances without clipping. Labels may ellipsize with the full name available through tooltip and accessibility.

## 3. Dashboard shell

Keep the native title bar and window behavior. Default size remains 1180×720. Establish and verify a usable minimum, starting at 860×600; do not silently increase the minimum to conceal layout defects.

Header layout: Stats at left; Overlay, Fans, Peaks as one action group; Metrics and Settings as a second group; a compact View menu for Collapse all / Expand all. Keep visible labels at standard widths. At the tested narrow width, wrap into a deliberate second toolbar row rather than truncate actions. Overlay must communicate its on/off state through the existing source of truth.

Keep the current vertical hardware groups and per-group reorder semantics. A section header contains its chevron, group name, count, and any existing health status. A collapsed group still exposes important status. Use consistent content alignment and section gaps. Preserve user's group expansion state and metric order.

Keep required recovery/degraded/read-failure messages prominent and actionable. Style notices consistently with icon, short summary, and available action; additional explanation can expand. Group informational prompts and update information so they do not overwhelm the header. Do not hide distinct actionable faults or delete existing update progress/retry/What's new behavior.

If no dashboard metrics are selected, show a clear empty state and an Open Metrics action. No fake measurements or reassuring health claim when telemetry is missing.

## 4. Metric tiles

Shared content order: label and reserved menu area; value with separated unit and severity; visualization where present; one quiet min/max footer. The displayed unit and precision must come from the current formatter/unit model; do not split a formatted string on arbitrary spaces. Reuse or minimally extend the existing view model with meaningful formatter tests if needed.

| Kind | Layout |
| --- | --- |
| Compact | Label plus one value row; no chart/footer; full name accessible |
| Sparkline | Label, value, chart filling remaining height, min/max; period label in reserved metadata area |
| Gauge | Main value outside the arc at standard reading size; compact arc beside/below it; optional limit detail |
| Bar | Standard reading, aligned horizontal track, concise scale/limit labeling, footer |
| Value | Standard label and prominent reading, optional limit and footer; shared baseline conventions |

Omit empty optional content from layout instead of leaving blank rows. Preserve configured gauge maximum, thresholds, rename/remove, sizes, Details, double-click, context menu, keyboard access, and same-group drag reorder/insertion indicator. Do not recalculate thresholds or change chart scales solely for decoration.

Missing value: an em dash and available short status; never 0 or a stale value styled as current. Keep history gaps. Normal/warn/crit behavior must remain correct for inverted FPS and regular temperature thresholds. Core-matrix cells keep meaningful load, clock, and temperature information; do not inflate every cell to full tile size. Existing graphs retain accurate samples rather than invented smoothing.

## 5. Metrics and Settings

Keep Metrics and Settings in the current right flyout for this pass. Start at 480 units wide, capped by available content width so controls remain usable at narrow windows and high UI scale. Provide a clear title and close affordance. Escape closes the flyout unless a child popup/editor appropriately consumes it; return focus to the invoking control. Do not block keyboard access with a visual-only overlay.

Metrics: add a visible search label/placeholder and clear action; preserve search semantics. Keep Sensor / Now / Dashboard / Overlay columns, alignment, group All/None semantics, and live values. Include a helpful no-results state with Clear search. Do not accidentally change All/None to affect a different selection scope.

Settings: retain a single existing SettingsViewModel instance and live application of settings. Replace the single long list with a category selector and a scrollable category body. Category selector may be a compact dropdown or wrapping tabs; default to a dropdown if tabs crowd the flyout. No new save/cancel transaction model.

| Category | Existing settings to retain |
| --- | --- |
| Appearance | Theme, accent, UI scale, core matrix |
| Monitoring | Polling, history window, thresholds, limits, tray metric |
| Alerts | Enabled behavior, hold time, chime, all current alert options |
| Overlay | Layout, opacity, font scale, click-through, hotkey, reset position |
| System | Hardware read toggle/restart, startup, automatic updates, diagnostics, About/manual update |

Before extraction, inventory every binding and command in the existing flyout and map it to a category; audit that inventory afterward. Keep inline validation next to the relevant field. Show clear Warn / Critical labels and Lower is worse directions. Preserve invalid-input handling, pending edits, restart-required state, and background check errors. Avoid resetting values or view models when navigating categories.

## 6. Fans

Retain the separate Fans window and existing bindings. Header: title plus explicit control state; master enable toggle and All to Auto remain visible and distinct. All to Auto retains its actual current semantics. Do not recolor an armed state as a generic success message.

Profile row: labeled selector, visible active profile, Modified state, Save as and Reload when relevant; move Delete and Create defaults into a profile menu. Keep profile deletion handling and reload behavior. Game-mode options form a separate section with short labels, Gaming and Desktop selectors, and current switching status.

Each fan card: editable name; aligned RPM and duty reading; a segmented Auto / Manual / Curve control built on correct mutually exclusive behavior; Identify as a secondary explicit action. At narrow widths, move controls to the next row. Auto hides unnecessary editors, Manual exposes slider/value/target, Curve exposes source selection and curve. Keep live target marker, source summary, range/pump floor visibility, and disabled states. Rename remains discoverable and keyboard accessible.

Retain safety banner collapse behavior, recovery notices, and competing-software warnings. Simplify copy without losing the fact that changes affect hardware and Auto/off/exit restores device control. No UI action fires an Identify pulse or changes enable state simply by entering a view or selecting a preview scenario.

## 7. Secondary surfaces

Peaks/Alerts: consistent toolbar and typography, stable numeric alignment, column headers aligned with rows, readable metric names, and clear empty states. At minimum width use sensible column minimums plus horizontal scrolling if necessary; never give Metric the leftover few pixels after fixed numeric columns. Retain TSV copy, session reset, min/max time tooltips, alert order and ongoing state.

Details: current/min/avg/max summary and chart labels follow shared typography. Preserve crosshair, units, time axis, threshold guides, and gaps. Dialogs use consistent padding, focus, validation, and primary/secondary action treatment. Overlay gets matching numeric clarity while preserving fixed dark contrast, opacity, click-through, move mode, hotkey, and independent sizing. Do not add an opaque dashboard-style card stack to the overlay.

## 8. Acceptance and change control

`VALIDATION.md` defines the required evidence. Preserve data density: record baseline/after fully visible medium tile counts at 1180×720 and explain reductions over 20%; do not sacrifice legibility to pass the count. Source dimensions are starting points. Any material departure from this visual contract needs an orchestrator decision recorded with before/after evidence, not a new user clarification for ordinary layout choices.
