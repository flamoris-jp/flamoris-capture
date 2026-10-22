# FLAMORIS Capture Progress

**Updated:** 2026-10-22  
**Project status:** initial one-window screenshot source implemented; Windows hands-on/release pending
**Repository:** [flamoris-jp/flamoris-capture](https://github.com/flamoris-jp/flamoris-capture)

This file separates **design readiness**, **source implementation**, **Windows hands-on acceptance**, and **release readiness**. A completed design checklist must never be reported as a tested feature.

## Snapshot

| Deliverable | Source | Automated verification | Windows hands-on | Release |
|---|---|---|---|---|
| Product scope, README and initial documentation | ✅ documented | Documentation review only | Not applicable | Not applicable |
| Screenshot capture DLL | Implemented: WGC, bounded PNG/JPEG/ROI | Full Windows-target cross-build passed; image-policy tests passed | Pending | Not released |
| Standalone EXE and target selection | Implemented: local selection, consent, preview, Stop | WPF cross-build passed | Pending | Not released |
| MCP grants and image results | Implemented: two typed queries, shared Core/Wpf 1.3.1 | Adapter/contracts and actual bridge Windows CI passed (24b035e) | Pending | Not released |
| Window / rectangle / monitor coverage | Window source only; rectangle/monitor not implemented | Source checks only | Pending | Not released |
| Short video, indexed observation, cleanup | Not started | Not started | Not started | Not available |
| Metadata-first video observation, frame selection, contact sheets | Basic design v0.1 documented; code not started | Documentation consistency review only | Not started | Not available |
| Interaction-event trace and synchronization | Not started | Not started | Not started | Not available |
| Windows packaging and published binaries | Self-contained win-x64 publish script and CI | Windows CI publish passed (24b035e) | Pending | No release/installer |

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
- **Windows CI evidence:** [PR run 39762395221](https://github.com/flamoris-jp/flamoris-capture/actions/runs/39762395221), source commit `24b035e`: build/publish succeeded, **24 passed / 0 skipped**, including the official MCP client against the published bridge with synthetic pixels. The self-contained win-x64 preview artifact was uploaded. A subsequent resize-dimension correction is locally rebuilt/tested and will be verified by the updated PR checks. No physical WGC test, real client image rendering, DPI/OS/driver acceptance or release is inferred from contract tests.
- See [ADR 0001](docs/decisions/0001-one-window-screenshot.md), [Setup](docs/SETUP.md), and the unexecuted [Windows acceptance checklist](docs/WINDOWS-ACCEPTANCE.md).

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
- [ ] Demonstrate a screenshot of a consented Window in a real Windows session with full metadata and no unrelated content.
- [ ] Verify window-close, permission revoke, target reuse, lock/secure content and oversized image paths.

### P2 · Expand targets and video

- [ ] Implement rectangle and selected-monitor capture, including negative virtual-monitor coordinates and mixed DPI.
- [ ] Add bounded recording start/status/stop/cancel, recording indicator and encode failure handling.
- [ ] Finalize video with a display-order frame index and recording-time/PTS mapping; use short-lived recording IDs without embedding video bytes in MCP.
- [ ] P2a: summary and selected frame by time/index, actual timestamp and explicit gaps.
- [ ] P2b: bounded sample selections, contact sheet, ROI and comparable-geometry diff with at most one image per result.
- [ ] Verify playable output, artifact SHA-256 and size, TTL, cleanup and dropped-frame reporting.

### P3 · Optional input trace and productization

- [ ] Evaluate application-scoped pointer/shortcut/window-state capture and sensitive-field redaction.
- [ ] Record timestamps relative to capture frames and quantify synchronization drift.
- [ ] Validate HowTo/debug report generation without claiming deterministic replay or guaranteed causal inference.
- [ ] Add CI, documented supported OS matrix, self-contained distribution and supply-chain/license review.
- [ ] Record independent Windows manual/physical DPI tests and link release evidence.

## Known risks / decisions still open

- WGC semantics and real capture of occluded/protected windows need proof (minimized/hidden are rejected by source policy); a screenshot permission does not imply remote or headless capture.
- MCP client image support and video transfer limits vary; metadata/bytes receipt does not prove the client can view the video.
- Recording event traces may expose sensitive user data; keyboard free text and cross-window global hooks are **not** accepted defaults.
- A rectangle/monitor includes whatever was visible in that region; stronger consent and preview are essential.
- File retention, disk quotas, video codec/packaging and signing strategy remain undecided.

## Next concrete deliverable

**Run the win-x64 preview on a physical interactive Windows desktop and complete [WINDOWS-ACCEPTANCE.md](docs/WINDOWS-ACCEPTANCE.md), including a real selected-window MCP image displayed by a client.** Source implementation is complete for this slice; P1 acceptance remains open. Measure GPU/CPU peak, DPI geometry, protected/minimized/occluded behavior and revoke/lock responsiveness before a release.

Details: [Setup](docs/SETUP.md) · [Architecture](docs/ARCHITECTURE.md) · [Privacy](docs/SECURITY-PRIVACY.md) · [Test plan](docs/TEST-PLAN.md).

Selective-observation contract: [基本設計 v0.1](docs/BASIC-DESIGN.md).


