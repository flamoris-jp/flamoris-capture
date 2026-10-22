# FLAMORIS Capture

**See what happened. Understand how it happened.** 🎬

FLAMORIS Capture is a **planned Windows screen-observation toolkit for people and AI**. It will combine a reusable capture DLL, an independent desktop EXE, and an MCP adapter, so FLAMORIS tools and unrelated Windows applications can be observed without modifying each application.

**Current status: planning / documentation only (2026-10-22).** The repository was initialized from the FLAMORIS template. **There is no working capture DLL, EXE, MCP endpoint, installer, or release yet.** The interfaces, OS support, and dependencies below are design targets and require implementation and Windows validation.

## 🧭 What it is / 何者か

Windows用の画面キャプチャ・AI観測ツール。人間が明示的に許可した画面を静止画または短い動画として取得し、将来は許可された操作イベントと同期して「何をしたらどうなったか」を調べられるようにする。

It is a standalone Windows observation product and a reusable library, **not** a FLAMORIS-only screen recorder.

### What it owns / 主な責任範囲

- **Capture engine (DLL):** bounded screenshots, short video recordings, capture target identity, frame timestamps, cropping and encoding.
- **Standalone Windows app (EXE):** choose a window, rectangle, or monitor; preview, record/stop, manage explicit grants, and observe status.
- **MCP capture adapter:** expose approved observation requests and return bounded images and recording metadata, reusing [Flamoris.Mcp.Core](https://github.com/flamoris-jp/flamoris-mcp-core). Raw video export is a later, separate contract.
- **Optional interaction trace (later phase):** application-scoped mouse actions, approved shortcuts, window-resize and application events synchronized with video; raw text/keylogging is not a goal.

### What it does not own / 持たない責任

- It does **not** edit Cutwork/2D/Kachinco documents, perform Undo/Redo, control another app, replay input, or infer application state from a screenshot with guaranteed accuracy.
- It does **not** replace each application's domain-specific MCP queries, [FLAMORIS Observer](https://github.com/flamoris-jp/flamoris-observer) for infrastructure, or MCP Core's transport/capability authority.
- It does **not** provide covert recording, unbounded surveillance, unrestricted desktop access, or arbitrary filesystem/network MCP tools.

## Planned capture modes

| Mode | Human selection | Still image | Short video |
|---|---|---|---|
| **Window** | Select one explicitly authorized window | Planned | Planned |
| **Rectangle** | Drag a Snipping Tool-like screen region | Planned | Planned |
| **Monitor** | Select an explicitly authorized display | Planned | Planned |

Exact behavior for minimized/occluded windows, protected content, multiple monitors, mixed DPI, window resizing, and recording duration must be tested; availability is not assumed.

**Why this exists:** while testing Cutwork, a part looked acceptable in a normal composite but hiding it exposed a hole. Real screenshots revealed what metadata and part previews alone did not. Capture is intended to provide that *visible result* while application-specific MCP provides the *internal state*.

## Conceptual architecture

```text
                    People                       Authorized MCP clients
                       |                                  |
             FLAMORIS Capture.exe                Typed MCP capture tools
                       |                                  |
                       +----------- Host / grants --------+
                                         |
                             Capture DLL / Windows backend
                                |                 |
                           Still image        Short video
                                |                 |
                           PNG / JPEG         bounded asset
                                         |
                  Optional application-scoped interaction trace
```

One recording session and its permissions are controlled by the Capture host. The DLL does not create a second application editing authority. MCP is a bounded interface to the same capture session.

See [Architecture](docs/ARCHITECTURE.md), [Privacy & security](docs/SECURITY-PRIVACY.md), and [Test plan](docs/TEST-PLAN.md).

## AI video observation / 必要なコマだけ観測

**Proposed basic design, not implemented:** keep recordings in Capture and let AI request only the images it needs. The default flow is **metadata summary → contact sheet → selected frame / ROI → optional comparison and event correlation**. Do not automatically attach a whole video or every frame.

指定時刻と0始まりのコマ番号の両方を扱い、要求時刻と実取得時刻、欠落、サイズ変更を明示する。人間は動画を再生し、AIは必要なコマだけを確認する。

Read [基本設計 v0.1](docs/BASIC-DESIGN.md) for the proposed MCP contracts, time/index rules, single-image result budget, recording/asset lifecycle, and acceptance criteria. No tool listed there is callable yet.

## Getting started

**There is not yet a runnable binary.** For developers and reviewers, start with [Setup & development](docs/SETUP.md). It explains prerequisites, how to obtain the repository, what is not available yet, and the proposed first implementation slice.

When code is introduced, its actual solution/project paths and verified `dotnet restore/build/test` commands will be added to Setup. Do not treat illustrative architecture names as existing projects.

## Roadmap / 開発順序

1. **Foundation:** define stable capture session/target/grant boundaries, one DLL, a minimal EXE, and one approved Windows screenshot via MCP.
2. **Target modes:** window, rectangle, monitor; DPI/multi-display normalization and preview.
3. **Video observation:** bounded start/status/stop/cancel, MP4 candidate, summary, indexed frame retrieval, contact sheets and comparison; timeout and cleanup. Raw video transfer is deferred to a separate bounded export design.
4. **Interaction trace:** opt-in application-scoped operations with synchronized timestamps; redact text and sensitive inputs.
5. **Productization:** packaging, CI, real Windows hands-on acceptance, diagnostics, and HowTo/test-report consumers.

These are **plans**, not shipped features. [PROGRESS.md](PROGRESS.md) tracks source work versus validation and release readiness.

## Working with FLAMORIS

| Integration | Boundary |
|---|---|
| [Cutwork](https://github.com/flamoris-jp/flamoris-cutwork) | Observe real UI and compare with Part/Mask/Repair MCP data. |
| [FLAMORIS 2D](https://github.com/flamoris-jp/flamoris-2D) | Observe animated previews while 2D owns rig/motion state. |
| [Kachinco](https://github.com/flamoris-jp/flamoris-kachinco) | Observe GUI/how-to interactions while Kachinco owns the timeline. |
| [MCP Core](https://github.com/flamoris-jp/flamoris-mcp-core) | Reuse authorization, revocation, bounded MCP image results and protocol infrastructure. |
| [MCP Hub](https://github.com/flamoris-jp/flamoris-mcp-hub) | Optional external orchestration; Capture must also work without the Hub. |

FLAMORIS overview: [organization map](https://github.com/flamoris-jp/.github) · [Desktop ecosystem](https://github.com/flamoris-jp/flamoris-commons/blob/main/docs/desktop-ecosystem.md).

## Safety by design

Capture starts **off**. The user must select the target and separately authorize any MCP disclosure; a recording indicator and immediate stop/revoke must be available. Rectangle/monitor capture is broader than single-window capture and needs distinct authorization. No covert recording, secure-desktop bypass, password/keylogging, or automatic handoff of unrelated windows. Recorded data is sensitive and must have strict size, TTL and deletion controls.

See [Privacy & security](docs/SECURITY-PRIVACY.md). AI-accessible capture is **read access to screen pixels**, not merely harmless metadata.

## Development / contributions

Read [AGENTS.md](AGENTS.md), [docs/SETUP.md](docs/SETUP.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and [PROGRESS.md](PROGRESS.md). Prefer small reviewed PRs with deterministic tests and Windows real-display evidence. **Do not add machine-specific paths, credentials, raw captured screens or test recordings to source control.**

Concept/design request: [FLAMORIS Issue Inbox #9](https://github.com/flamoris-net/flamoris-issue/issues/9) (the Inbox is private; link may require access). Implementation-specific Issues belong here after scope approval.

## GitHub metadata

Suggested topics: `flamoris`, `screen-capture`, `windows`, `mcp`, `desktop`, `dotnet`, `observability`. Set the organization `development_status` to **planned** while no runnable product exists. This README does not change repository metadata automatically.

## License and support

Source code is licensed under [Apache License 2.0](LICENSE), unless noted otherwise. External codecs, Windows APIs, dependencies and media have their own licensing constraints and require review before distribution.

FLAMORIS software is provided as-is, with no guaranteed support. You can use, adapt and distribute Apache-2.0-licensed code, including commercially, under its terms. AI-assisted self-support through repository documentation, Issues, code, and tests is welcome.

> **FLAMORIS Capture is a planned product; capabilities listed above are design intentions until a release and test evidence say otherwise.**

