# Verification & Acceptance Plan

**Status:** plan only. No executable tests or live Windows acceptance exist in this repository as of 2026-10-22.

## Test layers

| Layer | Purpose | Evidence |
|---|---|---|
| Unit (pure .NET) | ROI/DPI mapping, bounds, session identity, timestamps, quota accounting, encoding metadata | Deterministic fixtures and automated tests |
| Windows integration | OS capture target picker, frames, video encoder, revoked grants, process/window lifecycle | Windows CI where feasible |
| Physical Windows hands-on | Actual app/cursor/menus, dual monitor, DPI 100/150/200%, nontrivial GPU windows, real encoder | Recorded OS/build/display/driver matrix and reviewer notes |
| MCP external client | Real image/content blocks, guarded authorization, bounded video chunk delivery, no unexpected disclosure | Actual client round trip, size/hash checks |
| Product UX | Human permission and stop semantics, recording indicators, sensible errors | Reviewer observations, not synthetic screenshots alone |

## M1: Window screenshot (first useful slice)

- [ ] Application starts with capture and MCP disclosure OFF.
- [ ] User selects exactly one Window and explicitly permits capture.
- [ ] One still PNG/JPEG matches the selected target with correct dimensions and ROI mapping; data returned to client is actually decodable/displayable.
- [ ] Exact target identity and frame timing metadata survive through MCP; bounded image contract respects current MCP Core limits.
- [ ] Revoke/stop/close/window replacement/lock/sleep/cancel invalidate active work and stale handles.
- [ ] Invalid/oversized output and unsupported protected content fail safely.
- [ ] User can operate EXE independently of any FLAMORIS editor; test on Cutwork and a non-FLAMORIS app.

## M2: Region/monitor and short video

- [ ] User-selected rectangle on chosen display crops precisely (including DPI conversions); only selected region can be returned.
- [ ] User-selected monitor captures expected physical display, including a multi-monitor negative-origin case.
- [ ] Separate consent is needed when widening scope from a window to a region/monitor.
- [ ] Bounded 5-second sample video can start/stop, plays back, reports size/FPS/dropped frames, and passes SHA-256 checks.
- [ ] Window resize, target close, encoder failure, stop/cancel, storage cap and expiry clean up session/output.
- [ ] Client lacking video input support receives a truthful unsupported/retrieval indication, not an AI-viewability claim.

## M3: Interaction trace (optional)

- [ ] Pointer click, drag, wheel, window resize and allowlisted application shortcut events share a monotonic time base with capture frames.
- [ ] Timestamps quantify drift and gaps; lost events are flagged, not silently patched.
- [ ] Password typing, clipboard and freeform keystrokes are absent from event output. Unselected applications remain outside observation scope.
- [ ] A sample trace supports a HowTo timeline and a Cutwork display regression report, without claiming deterministic full replay.

## Resource/performance gates

Specify concrete limits only after measurement. Evaluate increasing capture resolution and FPS, different GPU/drivers, app foreground/background, protected/secure windows, resize during record, repeated starts/stops, resource cleanup, transport byte limits and memory growth.

Track at minimum: latency to first frame, dropped frame rate, CPU/GPU/memory use, peak temporary storage, revoke-to-stop latency, cleanup success and image/video decode success. Record actual machine/OS/software versions privately where appropriate; keep public test evidence free from personal content.

## Test readiness truth

`unit-pass` does not imply `Windows-display-pass`. `Window capture pass` does not imply `region/monitor pass`. `recording bytes delivered` does not imply `AI viewed/analyzed video`. `user action + visual change` is helpful diagnostic evidence, not a causal proof.

Update [PROGRESS.md](../PROGRESS.md) only with verified evidence. Follow [Privacy & security](SECURITY-PRIVACY.md) in all test fixtures and logs.
