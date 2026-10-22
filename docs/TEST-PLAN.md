# Verification & Acceptance Plan

**Status:** executable pure contract/session/image-policy/adapter tests and an optional official-client bridge integration now exist in `tests/Flamoris.Capture.Tests`. Windows CI enables the latter using synthetic pixels. Physical WGC/display acceptance has not been executed; use [WINDOWS-ACCEPTANCE](WINDOWS-ACCEPTANCE.md). CI/contract results are recorded separately in [PROGRESS](../PROGRESS.md). Future video/region/trace cases remain plans.

## Test layers

| Layer | Purpose | Evidence |
|---|---|---|
| Unit (pure .NET) | ROI/DPI mapping, bounds, session identity, timestamps, quota accounting, encoding metadata | Deterministic fixtures and automated tests |
| Windows integration | OS capture target picker, frames, video encoder, revoked grants, process/window lifecycle | Windows CI where feasible |
| Physical Windows hands-on | Actual app/cursor/menus, dual monitor, DPI 100/150/200%, nontrivial GPU windows, real encoder | Recorded OS/build/display/driver matrix and reviewer notes |
| MCP external client | Real single-image/content blocks, guarded authorization, metadata-first frame retrieval, no unexpected disclosure | Actual client round trip, size/hash checks |
| Product UX | Human permission and stop semantics, recording indicators, sensible errors | Reviewer observations, not synthetic screenshots alone |

## M1: Window screenshot (first useful slice)

- [ ] Application starts with capture and MCP disclosure OFF.
- [ ] User selects exactly one Window and explicitly permits capture.
- [ ] One still PNG/JPEG matches the selected target with correct dimensions and ROI mapping; data returned to client is actually decodable/displayable.
- [ ] Exact target identity and frame timing metadata survive through MCP; bounded image contract respects current MCP Core limits.
- [ ] Revoke/close/window replacement/lock/sleep cancel active work and invalidate stale handles; screenshot stop ends acquisition without a hidden recorder.
- [ ] Invalid/oversized output and unsupported protected content fail safely.
- [ ] User can operate EXE independently of any FLAMORIS editor; test on Cutwork and a non-FLAMORIS app.

## M2: Region/monitor and short video

- [ ] User-selected rectangle on chosen display crops precisely (including DPI conversions); only selected region can be returned.
- [ ] User-selected monitor captures expected physical display, including a multi-monitor negative-origin case.
- [ ] Separate consent is needed when widening scope from a window to a region/monitor.
- [ ] Bounded 5-second sample video can start/stop, plays back, reports size/FPS/dropped frames, and passes SHA-256 checks.
- [ ] Window resize/segments preserve geometry; target close, encoder failure, cancel, storage cap and expiry follow the basic-design cleanup rules. Normal stop finalizes a bounded recording for authorized observation.
- [ ] A client with image support can inspect selected frames without video-input support. The product does not claim that video bytes were viewed by AI or offer an unimplemented raw-video transfer tool.

## M3: Interaction trace (optional)

- [ ] Pointer click, drag, wheel, window resize and allowlisted application shortcut events share a monotonic time base with capture frames.
- [ ] Timestamps quantify drift and gaps; lost events are flagged, not silently patched.
- [ ] Password typing, clipboard and freeform keystrokes are absent from event output. Unselected applications remain outside observation scope.
- [ ] A sample trace supports a HowTo timeline and a Cutwork display regression report, without claiming deterministic full replay.

## M2a/M2b: Selective video observation

Contract and proposed numeric budgets: [基本設計 v0.1](BASIC-DESIGN.md). These are future acceptance cases, not tests that have run.

- [ ] A synthetic 30-second video with known frame markers supports metadata-only summary, interval selection `[0,30000)` at 3000ms (10 items), one contact sheet, and a selected detailed frame via a real MCP client. Summary/sample contain no image/video bytes.
- [ ] CFR/VFR and long-GOP fixtures prove 0-based **presentation-order** frame indexing, seek/decode selection, requested vs actual timestamp, before/after/nearest rules and earlier-frame tie-breaking. Do not derive the index from nominal FPS.
- [ ] First/last frame, out-of-range/negative/overflow/both selectors, tolerance failures, repeated selectors, dropped/duplicated frames, equal timestamps and explicit capture gaps behave as specified.
- [ ] Resize/segment transitions preserve recording time and geometry. ROI bounds are rechecked; cross-geometry comparison returns incomparable_geometry. A failed item appears as a marked placeholder in a partial contact sheet.
- [ ] At most 12 selected items and one image result; encoded image <=1 MiB, full wire response within Core/client limits, metadata <=64 KiB. Oversized requests fail before expensive allocation. PNG remains lossless, actual dimensions/quality are reported, exact-size requests are not silently shrunk.
- [ ] Decode deadline, busy, corrupt/truncated recording, unavailable codec and finalization failure produce truthful errors. Only ready recordings allow frame queries; failed data never becomes a playable/verified asset.
- [ ] Normal stop allows authorized post-record observation; cancel/discard prevents it. Sharing revoke, target replacement, OS lock and TTL block **all** reads, including cached images, selections, events and metadata. Exercise revoke during decode and immediately before return.
- [ ] Storage caps include video/index/events/finalization files; GPU surfaces and peak process memory are measured before selecting a supported operating envelope. Event overflow reports partial/gaps, and no unbounded queue or cache remains.
- [ ] Event selectors preserve before/at/after requests, report boundary misses, and quantify clock uncertainty; not_recorded differs from zero recorded events.
- [ ] Same-geometry RGB comparison reports configured threshold and metrics; cursor/animation/compression changes are not labelled anomalies. Change candidates, if implemented later, are labelled heuristic and include algorithm parameters.
- [ ] Complete the Cutwork synthetic-image investigation in the basic design, record image count/total bytes/latency and state exactly which intervals remain unobserved.

## Resource/performance gates

Start with the conservative proposed limits in the basic design and validate them by measurement; do not report them as demonstrated capacity. Evaluate increasing capture resolution and FPS, different GPU/drivers, app foreground/background, protected/secure windows, resize during record, repeated starts/stops, resource cleanup, transport byte limits and memory growth.

Track at minimum: latency to first frame, dropped frame rate, CPU/GPU/memory use, peak temporary storage, revoke-to-stop latency, cleanup success and image/video decode success. Record actual machine/OS/software versions privately where appropriate; keep public test evidence free from personal content.

## Test readiness truth

`unit-pass` does not imply `Windows-display-pass`. `Window capture pass` does not imply `region/monitor pass`. `recording bytes delivered` does not imply `AI viewed/analyzed video`. `user action + visual change` is helpful diagnostic evidence, not a causal proof.

Update [PROGRESS.md](../PROGRESS.md) only with verified evidence. Follow [Privacy & security](SECURITY-PRIVACY.md) in all test fixtures and logs.


