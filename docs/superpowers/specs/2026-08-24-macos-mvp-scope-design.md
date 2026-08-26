# Stats macOS MVP scope — design

**Date:** 2026-08-24  
**Status:** approved in chat

## Objective

Define the first macOS build scope for Stats with clear in/out boundaries so implementation can proceed without feature drift.

## In Scope (MVP)

### UI/features
- Dashboard
- Overlay
- Tray
- Settings
- Thresholds
- History

### Metrics (read-only first)
- CPU load
- Memory
- Disk
- Network

### Optional metrics
- Temperatures where safe/reliable access is available on macOS

## Out of Scope (MVP)

- Fan control
- FPS / frame-time capture
- Any write-path to hardware devices
- Full feature parity with Windows release

## Guardrails

- macOS MVP is read-only telemetry first.
- If a metric source is unavailable, the app must degrade gracefully (no crash, clear unavailable state in UI).
- Temperature sensors are optional and must only ship if source reliability is acceptable on supported Macs.

## Acceptance Criteria

1. The app ships macOS Dashboard, Overlay, Tray, Settings, Thresholds, and History.
2. The app displays read-only CPU load, memory, disk, and network metrics.
3. Temperature metrics are included only when validated as safe/reliable; otherwise hidden/marked unavailable.
4. No fan control or FPS functionality is present in this milestone.

