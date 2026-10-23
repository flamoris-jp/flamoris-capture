# FLAMORIS Capture Progress

**Updated:** 2026-10-24
**Project status:** 0.2.0 current-window observation, bounded recording and operation trace source; basic Windows/MCP workflow accepted; broader platform/lifecycle acceptance and release pending
**Repository:** [flamoris-jp/flamoris-capture](https://github.com/flamoris-jp/flamoris-capture)

This file separates **design readiness**, **source implementation**, **Windows hands-on acceptance**, and **release readiness**. A completed design checklist must never be reported as a tested feature.

## Snapshot

| Deliverable | Source | Automated verification | Windows hands-on | Release |
|---|---|---|---|---|
| Product scope, README and initial documentation | ✅ documented | Documentation review only | Not applicable | Not applicable |
| Screenshot capture DLL | Implemented: WGC, bounded PNG/JPEG/ROI | Full Windows-target cross-build passed; image-policy tests passed | User screenshot confirms Cutwork one-shot preview; remaining cases pending | Not released |
| Standalone EXE and target selection | Implemented: local selection, consent, preview, Stop | WPF cross-build passed | User screenshot confirms target selection/local preview; remaining cases pending | Not released |
| MCP grants and image results | Implemented: six typed queries, optional IDs, shared Core/Wpf 1.3.1 | Adapter/contracts and actual bridge Windows CI passed (24b035e) | Real Hub-to-client screenshot displayed without session ID (2026-10-24); remaining grant/revoke cases pending | Not released |
| Window / rectangle / monitor coverage | Window source only; rectangle/monitor not implemented | Source checks only | Selected-window capture confirmed; rectangle/monitor unimplemented | Not released |
| Short video, indexed observation, cleanup | Bounded 10fps JPEG sequence, local playback/scrub, silent H.264 MP4 export, normal-exit cleanup | Cross-build/pure recording tests; Windows synthetic H.264 encode/decode passed (8cb0f44) | Real start/stop and frame retention confirmed; local MP4/JSON export, playback and cleanup not reverified | Development source |
| Metadata-first video observation, frame selection, contact sheets | Summary, latest/index/time frame and 1–9 cell sheet | Contract/revoke tests; Windows platform sheet passed (8cb0f44) | Real summary, six-cell sheet and time-selected images displayed (2026-10-24) | Development source |
| Interaction-event trace and synchronization | Target-foreground Raw Input, physical keys/numpad/mouse, opt-in notice/badge, QPC JSON | Pure opt-in/page/queue tests | Mouse drag and Left/Space down/up confirmed; Blender/password/custom control cases pending | Development source |
| Windows packaging and published binaries | Self-contained win-x64 publish script and CI | Windows CI publish passed (24b035e) | User rebuilt/launched updated checkout; installer/signing not accepted | No release/installer |

## 2026-10-24 — Basic real-Windows / Hub observation acceptance

The user and AI completed the basic workflow using the running Windows Capture app and a selected Kachinco window. Evidence is live MCP image display and sanitized response counters from this acceptance session, not synthetic CI alone. Exact app commit, Windows build and GPU/driver versions were not collected, so this does not define a platform support matrix.

- After the user rebuilt/launched the updated app, upgraded the Hub catalog and connected Capture, `capture.screenshot.get` with `input: {}` returned a decodable image visibly matching the selected Kachinco window. No session-ID discovery was needed.
- The human started/stopped recording in Capture. One stopped sample reported **87 frames over 8,665.9187 ms**. The AI displayed a six-cell contact sheet and retrieved the frame at/before a requested 5,000 ms (actual **4,949.2339 ms**). This confirms selective image observation, not whole-video transfer.
- A mouse sample reported **172 events**, **0 dropped events**. Left-button down at **12,530.6356 ms** and up at **14,535.581 ms** corresponded to before/after frames showing the Kachinco playhead moving from about 0.07 s to 1.5 s. This supports the observed drag sequence; it is not a measured synchronization-error bound or causal proof.
- A subsequent sample reported **130 events**, **0 dropped events**, including **three Left-arrow and three Space presses, each with keyDown and keyUp**. Mouse and keyboard operation retrieval are confirmed for this selected-app case.
- An earlier text-entry trial contained no keyboard events. Source inspection confirms suppression for recognized editable/text/password controls; the focused control was not instrumented, so that trial is not proof of password detection or universal text suppression.
- **Basic observation acceptance is complete:** current-window screenshot → human-controlled recording → selective frames/contact sheet → mouse/key event retrieval. Acquisition start/stop/save remain human UI actions; MCP exposes read queries.
- Not exercised here: local MP4/JSON save and playback, Blender single-letter/numpad/NumLock behavior, wheel/modifier coverage, independent operation-only access, permission revoke/OS lifecycle, cleanup/resource caps, DPI/OS/GPU matrix and release/signing. Prior synthetic encoder tests remain separate evidence.

See [Windows acceptance](docs/WINDOWS-ACCEPTANCE.md) for the remaining checks. No raw user captures, input text, credentials, session IDs or private topology are committed.

## 2026-10-23 — Current-window workflow, recording and operations

- User-approved toolbar UI: direct current-window observation without required session IDs, screen/operation checkboxes, yellow operation/red recording badges, common first-enable notice and persisted opt-out/settings restore.
- Screen OFF gates local and MCP pixel acquisition and cancels recording; operation recording remains independently available. Target/OS/Stop revocation stops both. Recorded assets lose MCP authority on revoke and stay locally saveable; re-enabling operations cannot reauthorize old footage.
- Bounded acquired-frame recording (10fps nominal, max edge 1280, 15 minutes / 9,000 frames / 512 MiB frame files), local time-based playback/slider, silent platform H.264 MP4 export and explicit operation/frame JSON save. Single-frame acquisition is reused; source stalls reduce FPS and remain timestamp gaps.
- Six product queries plus Core context (seven Hub descriptors). Summary, frame index/time/latest, one contact sheet and paginated events; no remote start/stop, input replay, arbitrary files or whole-video transfer.
- Physical key/numpad and pointer events are filtered to the selected foreground root. Known UI Automation password/editable/text fields suppress keys. Custom-control detection is best effort; no universal secret filtering is claimed.
- Existing-catalog upgrade preserves credentials, runtime.env and Docker container identity; only the Hub catalog is backed up/replaced and its current container restarted, with health verification and catalog rollback. No live server operation was performed by this change.
- Verification before Windows CI: whole solution cross-build including new Windows tests, zero warnings/errors; 29 pure C# cases passed and one Windows bridge test explicitly skipped on Linux; 17 mocked host-helper tests passed. Windows CI adds actual published-bridge and synthetic JPEG/contact-sheet/H.264 encode-decode checks; results recorded separately below when complete.
- Windows CI [39806522127](https://github.com/flamoris-jp/flamoris-capture/actions/runs/39806522127) at `8cb0f44`: build/publish succeeded with zero warnings/errors; **29 Core/MCP/actual-bridge cases passed with no skips**, plus **one Windows synthetic JPEG/contact-sheet/H.264 MP4 encode-decode case passed**. The self-contained preview artifact is available on that run. The subsequent operation-only MCP regression case also passes locally; it changes tests only.
- At this implementation checkpoint, real-display/input/client-image acceptance was pending. The basic Kachinco workflow was subsequently confirmed on 2026-10-24 above; Blender, DPI, OS lifecycle/performance and other untested cases remain pending.
- Current contract: [ADR 0002](docs/decisions/0002-simple-observation-recording.md). Earlier entries below are historical evidence for the initial screenshot slice, not current feature limits.

## 2026-10-22 — Repository initialization

- Repository created from FLAMORIS template (license, generic AGENTS and root policy scaffolding).
- Initial product documentation requested: project overview, setup, architecture, privacy and test plans.
- Cross-repository reference: [private FLAMORIS Idea Inbox #9](https://github.com/flamoris-net/flamoris-issue/issues/9).
- Driving hands-on observation: Cutwork Part separation looked correct in normal composite but hiding the part exposed holes; screen-level verification complements domain-level MCP.
- **No capture code, recorder, Windows executable, tests, CI pipeline, package or real hardware verification has been implemented as part of this documentation task.**

## 2026-10-22 — First screenshot implementation

- Implemented `Flamoris.Capture.Core`, reusable `Flamoris.Capture.Windows.dll`, WPF `Flamoris.Capture.App.exe`, same-session `Flamoris.Capture.Mcp`, and a thin executable entry point to packaged Core's stdio bridge.
- API floor Windows build 19041 / .NET 10; D3D11 hardware + WGC immutable selected-window item, no broader fallback. Consent/share start OFF. Invalidation on target/share/Stop/OS lifecycle, closed DTOs, source/ROI/image bounds, QPC/DPI/geometry/hash metadata and one admission slot are implemented.
- Fixed public NuGet Core/Wpf **1.3.1**; reused the shared connection menu/providers, capabilities, RequestGuard and single-image transport. No package code or DLLs vendored.
- Local evidence: .NET SDK **10.0.402 on Linux**, full solution restore/cross-build (including WPF + WGC), **0 warnings / 0 errors**; **23 contract/policy/adapter test cases passed**, **1 actual-bridge integration explicitly skipped** because a published Windows bridge is not executable here.
- **Windows CI evidence:** [PR run 39762395221](https://github.com/flamoris-jp/flamoris-capture/actions/runs/39762395221), source commit `24b035e`: build/publish succeeded, **24 passed / 0 skipped**, including the official MCP client against the published bridge with synthetic pixels. The self-contained win-x64 preview artifact was uploaded. The resize-dimension correction is also verified by [updated Windows run 39762778823](https://github.com/flamoris-jp/flamoris-capture/actions/runs/39762778823) at source commit `9ebe73a`: build, publish and bridge tests succeeded. Shutdown UI callbacks are guarded against displaying errors after the host closes. No physical WGC test, real client image rendering, DPI/OS/driver acceptance or release is inferred from contract tests.
- See [ADR 0001](docs/decisions/0001-one-window-screenshot.md), [Setup](docs/SETUP.md), and the unexecuted [Windows acceptance checklist](docs/WINDOWS-ACCEPTANCE.md).

## 2026-10-22 — First real-Windows preview and Hub registration preparation

- The user supplied a screenshot showing the running Capture EXE displaying a one-window Cutwork still. The visible Capture overlay is absent from its captured Cutwork preview. This confirms that single observed local preview case; OS build, DPI/driver, lock/revoke, resizing and unrelated-app cases remain unverified.
- Generated `config/hub-capture.example.json` from the actual Core 1.3.1 wire descriptors. A producer-side test pins all three schemas. Video and target-selection tools are not advertised.
- Added an explicit human-run PowerShell/SSH registration helper, dedicated locally generated token, preservation of current Hub Docker settings, authenticated metadata-only health verification and automatic error rollback. This is a prepared operation; **live registration/desktop MCP image acceptance remain pending execution**.
- The helper's mocked tests verify environment/security preservation, conflict refusal, success, failure rollback and idempotence; CI parses the PowerShell launcher and runs these tests without production access.
- Usage and the existing local-prefix/Hub-namespace mapping: [Hub connection](docs/HUB-CONNECTION.md).

## 2026-10-22 — Hub connection troubleshooting

- Human-run Hub registration reported Capture with three tools, preserved the image, and added its dedicated credential. Physical desktop MCP image acceptance is still pending.
- The user's loaded Nginx configuration showed exact routes for three existing desktop products and no `/desktop/capture` route. Hub catalog registration alone did not publish this endpoint; the original setup instructions omitted this separate proxy step.
- Added an explicit, separate Nginx route repair that copies the working Cutwork WebSocket block in the same server, backs up privately, checks syntax, reloads and restores on failure. Five mocked route tests (14 total host-helper tests) passed locally; the user reported successful production Nginx syntax test/reload and a separate authenticated WSS handshake (`SERVER_WSS_AUTH_OK`). Actual Capture connection remains pending. No app rebuild or token rotation is required for this route change.

## 2026-10-22 — Read-only connection diagnosis and local preview fix

- The user's stored Capture preferences showed `Method: Hub`, `Permission: Edit`, the intended Capture endpoint, and an existing Windows credential entry. Capture's app attachment rejects Edit before starting the Hub provider; this explains the observed attempt without a desktop registration. The user must choose Read only in the connection confirmation. WSS server-side authentication success does not prove the separately stored Windows secret matches.
- Added an explicit product message for unsupported Edit permission rather than only the shared generic connection failure.
- Fixed sharing ON/OFF clearing the local preview. Sharing changes continue to revoke old MCP grants and cancel stale requests, while valid local capture consent keeps the still visible. Target selection clears the previous target explicitly; Stop, target loss and OS lock still clear the preview.
- Local full-solution cross-build passed with zero warnings/errors; 24 C# tests passed and the actual Windows-bridge test was explicitly skipped on Linux. Windows CI and the real checkbox/connection acceptance of this fix are tracked separately.

## 2026-10-22 — Desktop-family publish workflow

- Added `product/packaging/publish-windows.ps1` so Git checkout usage matches the desktop family: pull, publish, launch the portable EXE.
- Output is `artifacts/windows/FLAMORIS-Capture-win-x64/Flamoris.Capture.App.exe`, with the bridge, DLLs and self-contained runtime in the same folder. The original script remains the single publish implementation and keeps its legacy default output for compatibility.
- Required app/bridge/WGC/MCP/WPF/runtime output files are checked after publish. Windows CI now executes the family entry point and tests the bridge from the resulting package folder.
- The user reported a green Capture connection after choosing Read only. This confirms their observed indicator; real MCP image display and revoke/OS/DPI acceptance remain separate gates.

## Roadmap and gates

### 2026-10-22 — Selective video observation design

- Added [基本設計 v0.1](docs/BASIC-DESIGN.md): summary → contact sheet → selected frame/ROI → comparison/events.
- Specified time and 0-based frame selectors, actual timestamps, gaps, geometry changes, bounded selection and single-image MCP results.
- Defined proposed limits, recording finalization, stop vs revoke, asset TTL, derived-cache invalidation and staged acceptance.
- Checked the current MCP Core image contract (one PNG/JPEG, at most 1 MiB); no new transport or multi-image result is assumed.
- This is a **documentation baseline**, not an implemented API, validated performance budget, completed P0 or Windows acceptance. OS/backend, codec, memory and package-version decisions remain open.

### P0 · Define the first screenshot contract

- [ ] Decide supported Windows versions and whether Windows Graphics Capture meets actual constraints, including headless/minimized/locked scenarios.
- [x] Define capture session owner, one target ID, per-target consent, separate MCP disclosure grant and revoke behavior.
- [x] Confirm packaging boundaries: reusable DLL, GUI EXE, and MCP adapter with no second transport/security implementation.
- [x] Choose an MVP rendering backend; record unsupported cases rather than silently falling back to another screen.
- [x] Define still-image max bytes, ROI/downscale, timestamp/DPI metadata, cancellation and error vocabulary.
- [x] Accept an ADR for security, sharing and resource budgets.

### P1 · Implement and verify one-window still-image path

- [x] Author code + deterministic tests for target identity, sizing, ROI, bounds and session lifetime.
- [x] Create a minimal human-visible EXE with target selection, capture indicator and stop/revoke.
- [x] Provide a **bounded one-shot** MCP screenshot using the existing FLAMORIS MCP infrastructure.
- [x] Demonstrate a selected-window screenshot with image metadata displayed by a real MCP client (Kachinco, 2026-10-24); broader isolation/target-reuse cases remain pending.
- [ ] Verify window-close, permission revoke, target reuse, lock/secure content and oversized image paths.

### P2 · Expand targets and video

- [ ] Implement rectangle and selected-monitor capture, including negative virtual-monitor coordinates and mixed DPI.
- [x] Implement bounded local recording and status/stop handling; real start/stop and frame retention confirmed 2026-10-24. Cancellation/indicator/failure cases still require full physical acceptance.
- [ ] Finalize video with a display-order frame index and recording-time/PTS mapping; use short-lived recording IDs without embedding video bytes in MCP.
- [x] Implement summary and selected recorded frames by time/index with actual timestamps. Summary and time selection confirmed on real footage 2026-10-24; explicit index/gap edge cases remain automated-only.
- [ ] P2b: bounded sample selections, contact sheet, ROI and comparable-geometry diff with at most one image per result.
- [ ] Verify playable output, artifact SHA-256 and size, TTL, cleanup and dropped-frame reporting.

### P3 · Optional input trace and productization

- [x] Implement selected-foreground physical key/mouse trace and recognized text-control suppression. Mouse and Left/Space key down/up confirmed 2026-10-24; broader input/control/window-state cases remain pending.
- [ ] Record timestamps relative to capture frames and quantify synchronization drift.
- [ ] Validate HowTo/debug report generation without claiming deterministic replay or guaranteed causal inference.
- [ ] Add CI, documented supported OS matrix, self-contained distribution and supply-chain/license review.
- [ ] Record independent Windows manual/physical DPI tests and link release evidence.

## Known risks / decisions still open

- WGC semantics and real capture of occluded/protected windows need proof (minimized/hidden are rejected by source policy); a screenshot permission does not imply remote or headless capture.
- MCP client image support and video transfer limits vary; metadata/bytes receipt does not prove the client can view the video.
- Recording event traces may expose sensitive user data; keyboard free text and cross-window global hooks are **not** accepted defaults.
- A rectangle/monitor includes whatever was visible in that region; stronger consent and preview are essential.
- Current temporary-frame bounds, normal-exit cleanup and silent Windows H.264 export are defined in ADR 0002. Measured resource behavior, broader retention/expiry and signing remain open.

## Next concrete deliverable

**The basic selected-window observation workflow is accepted (2026-10-24).** Complete the remaining [Windows acceptance](docs/WINDOWS-ACCEPTANCE.md) cases before a release: local MP4/JSON save/playback, Blender inputs, revoke/lock behavior, DPI/platform coverage and measured resource/cleanup behavior. This documentation update does not change the development-preview release status.

Details: [Setup](docs/SETUP.md) · [Architecture](docs/ARCHITECTURE.md) · [Privacy](docs/SECURITY-PRIVACY.md) · [Test plan](docs/TEST-PLAN.md).

Selective-observation contract: [基本設計 v0.1](docs/BASIC-DESIGN.md).



## 2026-10-24 Embedded WPF observation candidate

- Shared owner-window consent/lifecycle control and screenshot-only MCP surface.
- Common privacy notice reused by standalone and embedded hosts.
- Editor boundary composition/consent/revocation contract tests added.
- Real Windows embedding acceptance remains pending.

### Embedded connection policy update (2026-10-24)

- User accepted MCP connection as authorization to observe that editor window.
- No separate embedded sharing checkbox, notice or saved sharing preference.
- A connection lease starts sharing and releases it on detach/failure; stale leases
  cannot stop newer connections. The status bar reports sharing state.
- Standalone Capture retains its original explicit consent controls.
