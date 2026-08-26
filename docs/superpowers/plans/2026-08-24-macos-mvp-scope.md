# macOS MVP scope — implementation plan

**Spec:** `docs/superpowers/specs/2026-08-24-macos-mvp-scope-design.md`

## Milestones

- [ ] **Milestone 1: Platform baseline**
  - Establish macOS app shell with Dashboard, Overlay, Tray, Settings surfaces.
  - Wire settings persistence and baseline theme/token plumbing.

- [ ] **Milestone 2: Read-only metric pipeline**
  - Implement read-only metric collection for CPU load, memory, disk, and network.
  - Expose metric capabilities so unavailable sources can be represented cleanly in UI.

- [ ] **Milestone 3: Thresholds and history**
  - Add threshold evaluation and visual severity states for in-scope metrics.
  - Add history buffering/retention controls and chart bindings for in-scope metrics.

- [ ] **Milestone 4: Temperature probe (optional)**
  - Evaluate macOS-safe/reliable temperature sources.
  - Enable temperatures only if reliability criteria are met; otherwise leave disabled with explicit unavailable handling.

- [ ] **Milestone 5: MVP hardening**
  - Validate no write-path hardware control is exposed.
  - Verify fan control and FPS are absent from all UI and code paths for this milestone.
  - Ship notes/documentation for MVP behavior and known limitations.

## Explicit Non-goals for this plan

- Fan control
- FPS / frame-time capture
- Full Windows parity

