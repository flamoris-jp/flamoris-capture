# FLAMORIS Capture Progress

**Updated:** 2026-10-22  
**Project status:** `planned` (documentation/bootstrap, no runnable product)  
**Repository:** [flamoris-jp/flamoris-capture](https://github.com/flamoris-jp/flamoris-capture)

This file separates **design readiness**, **source implementation**, **Windows hands-on acceptance**, and **release readiness**. A completed design checklist must never be reported as a tested feature.

## Snapshot

| Deliverable | Source | Automated verification | Windows hands-on | Release |
|---|---|---|---|---|
| Product scope, README and initial documentation | ✅ documented | Documentation review only | Not applicable | Not applicable |
| Screenshot capture DLL | Not started | Not started | Not started | Not available |
| Standalone EXE and target selection | Not started | Not started | Not started | Not available |
| MCP grants and image results | Not started | Not started | Not started | Not available |
| Window / rectangle / monitor coverage | Not started | Not started | Not started | Not available |
| Short video, bounded transfer, cleanup | Not started | Not started | Not started | Not available |
| Interaction-event trace and synchronization | Not started | Not started | Not started | Not available |
| Windows packaging and published binaries | Not started | Not started | Not started | Not available |

## 2026-10-22 — Repository initialization

- Repository created from FLAMORIS template (license, generic AGENTS and root policy scaffolding).
- Initial product documentation requested: project overview, setup, architecture, privacy and test plans.
- Cross-repository reference: [private FLAMORIS Idea Inbox #9](https://github.com/flamoris-net/flamoris-issue/issues/9).
- Driving hands-on observation: Cutwork Part separation looked correct in normal composite but hiding the part exposed holes; screen-level verification complements domain-level MCP.
- **No capture code, recorder, Windows executable, tests, CI pipeline, package or real hardware verification has been implemented as part of this documentation task.**

## Roadmap and gates

### P0 · Define the first screenshot contract

- [ ] Decide supported Windows versions and whether Windows Graphics Capture meets actual constraints, including headless/minimized/locked scenarios.
- [ ] Define capture session owner, one target ID, per-target consent, separate MCP disclosure grant and revoke behavior.
- [ ] Confirm packaging boundaries: reusable DLL, GUI EXE, and MCP adapter with no second transport/security implementation.
- [ ] Choose an MVP rendering backend; record unsupported cases rather than silently falling back to another screen.
- [ ] Define still-image max bytes, ROI/downscale, timestamp/DPI metadata, cancellation and error vocabulary.
- [ ] Accept an ADR for security, sharing and resource budgets.

### P1 · Implement and verify one-window still-image path

- [ ] Author code + deterministic tests for target identity, sizing, ROI, bounds and session lifetime.
- [ ] Create a minimal human-visible EXE with target selection, capture indicator and stop/revoke.
- [ ] Provide a **bounded one-shot** MCP screenshot using the existing FLAMORIS MCP infrastructure.
- [ ] Demonstrate a screenshot of a consented Window in a real Windows session with full metadata and no unrelated content.
- [ ] Verify window-close, permission revoke, target reuse, lock/secure content and oversized image paths.

### P2 · Expand targets and video

- [ ] Implement rectangle and selected-monitor capture, including negative virtual-monitor coordinates and mixed DPI.
- [ ] Add bounded recording start/status/stop/cancel, recording indicator and encode failure handling.
- [ ] Use short-lived asset handles for videos rather than embedding a large file in one MCP response.
- [ ] Verify playable output, artifact SHA-256 and size, TTL, cleanup and dropped-frame reporting.

### P3 · Optional input trace and productization

- [ ] Evaluate application-scoped pointer/shortcut/window-state capture and sensitive-field redaction.
- [ ] Record timestamps relative to capture frames and quantify synchronization drift.
- [ ] Validate HowTo/debug report generation without claiming deterministic replay or guaranteed causal inference.
- [ ] Add CI, documented supported OS matrix, self-contained distribution and supply-chain/license review.
- [ ] Record independent Windows manual/physical DPI tests and link release evidence.

## Known risks / decisions still open

- WGC semantics and actual capture of minimized/occluded/protected windows need proof; a screenshot permission does not imply remote or headless capture.
- MCP client image support and video transfer limits vary; metadata/bytes receipt does not prove the client can view the video.
- Recording event traces may expose sensitive user data; keyboard free text and cross-window global hooks are **not** accepted defaults.
- A rectangle/monitor includes whatever was visible in that region; stronger consent and preview are essential.
- File retention, disk quotas, video codec/packaging and signing strategy remain undecided.

## Next concrete deliverable

**A small, permissioned real-Windows screenshot PoC using one DLL + one GUI EXE + one MCP image query**. Update this file from actual CI and physical test evidence after implementation, not from planned design alone.

Details: [Setup](docs/SETUP.md) · [Architecture](docs/ARCHITECTURE.md) · [Privacy](docs/SECURITY-PRIVACY.md) · [Test plan](docs/TEST-PLAN.md).
