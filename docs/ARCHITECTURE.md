# Architecture / 設計境界

**Status:** proposed design, not implemented. Updated 2026-10-22.

## Product contract

FLAMORIS Capture will be a **general-purpose Windows observation product**, able to run independently from FLAMORIS application code. It consists of a reusable capture library, a local Windows EXE for user-facing permissions and preview, and an optional MCP adapter.

```text
User: select target, authorize capture, optionally authorize sharing
    |
    v
Capture.exe (single capture-session authority)
    | permissions / target token / revoke / lifecycle
    +---- UI: window picker / rectangle / monitor / preview / status
    |
    +---- MCP adapter [optional] -- Flamoris.Mcp.Core -- authorized clients
    |
    v
Capture DLL
    +-- Windows capture backend
    +-- ROI / scale / image encode
    +-- short video encode and bounded artifact buffer
    +-- typed event trace interface (later)
```

Capture **does not** become an editing authority for Cutwork, 2D, Kachinco or other applications. Domain-specific MCP queries remain the best source for masks, documents and timeline state; screen observation tells the AI what actually appeared on a user's display.

## Initial capture modes

| Scope | Consent/identity | Observation boundary |
|---|---|---|
| `window` | User selects a specific live window; bind to actual target instance | Selected application window, not arbitrary next HWND |
| `region` | User drags a rectangle on a selected display | Pixels within the shown selection; nearby windows can appear |
| `monitor` | User selects a specific monitor | Entire selected display; broadest sensitivity |

For `region`, compare WGC display capture followed by validated cropping with other platform-native approaches. Window-only capture may exclude other windows/overlays, depending on backend; **do not claim all UI/popups/minimized content are capturable**. For multi-monitor and mixed-DPI scenarios, explicitly define virtual screen origin and conversion between physical pixels, DIPs and encoded output.

Windows Graphics Capture is the first backend to evaluate, not an unconditional guarantee. Platform permission/selection UI, target size changes, occlusion, minimized windows, protected media, secure desktops and encoder availability require real tests.

## Suggested assembly responsibilities (names provisional)

- `Flamoris.Capture.Core`: capture target descriptors, session state, authorization checks, timestamps, image/video result descriptors, hard resource budget interfaces. No WPF or MCP dependency.
- `Flamoris.Capture.Windows`: OS capture acquisition, Windows interop, frame lifetimes, video encode; no direct public network/HTTP or file browser.
- `Flamoris.Capture.App`: independently runnable EXE, explicit user target picker and sharing controls, visible recording indicator, immediate stop, permission revoke.
- `Flamoris.Capture.Mcp`: typed tool commands, authorization and metadata/image/asset results; reuse [Flamoris.Mcp.Core](https://github.com/flamoris-jp/flamoris-mcp-core). Must not create a second capture session.
- Tests: deterministic pure helpers + controlled Windows interactive testing; no reliance solely on headless CI screenshots.

Prefer fewer assemblies if actual implementation evidence favors simplicity. Sharing a DLL across FLAMORIS and third-party applications does not require those third-party apps to run MCP.

## Resource and result design

**Still image:** bounded PNG/JPEG with actual width/height, timestamp (monotonic capture relative clock + wall timestamp where needed), target identity, ROI origin, DPI/coordinate mapping, scaling and encoding parameters. Prefer standard MCP image content via the existing `HostImageTool<T>` when supported; the current MCP Core image contract limits individual payloads to 1 MiB. Larger pictures must be safely downsized/cropped or rejected, never silently truncated.

**Video:** bounded `start/status/stop/cancel` recording session, conservative max duration/FPS/dimensions/bytes to be set by benchmarks, failure and cancellation status, short-lived asset handle, chunked read/checksum or reviewed equivalent. Avoid a single huge base64 MCP JSON result. MP4/H.264 is a candidate, not an implemented codec choice. Do not infer that returning a video artifact means the client can inspect its content.

**Interaction events (later):** pointer operations, mouse wheel, explicitly approved command shortcuts, window focus/resize and opt-in application semantic events timestamped on a common monotonic clock with recorded frames. Recording events != safely replaying input. Never log freeform typed text.

**Bundle (later):** `manifest.json`, video, `events.jsonl` and optional still frames. Manifest records target/scope, coordinate conventions, FPS/timestamps, dropped frames/gaps, hashes and versions; it does not contain credentials or sensitive window titles by default. This is an interoperability proposal, not a committed schema.

## Lifecycle & ownership

1. Capturing and AI disclosure begin OFF.
2. Local user selects specific target and grants capture capability.
3. Separate explicit sharing authorization allows an approved client to retrieve capture data.
4. Every request and every outgoing result rechecks target identity, grant, current capture session, cancellation and size limits.
5. Stop, revoke, app exit, target destruction/replacement and relevant OS session changes end active capture and invalidate outstanding handles.
6. A target is **never** silently replaced by another window or expanded to a monitor.
7. UI and MCP operate on the same captured state and lifecycle; no parallel hidden recorder.

## Source of truth

- Product-specific capture/trace schema: **this repository**.
- Transport, permission primitives and MCP result encoding: **Flamoris.Mcp.Core**, not copied into Capture.
- Application model state: **that application's own editor/MCP**.
- FLAMORIS MCP Hub: optional external router, not a required Windows capture daemon.
- FLAMORIS Observer: infrastructure/server observation; separate responsibility.

See [Privacy and security](SECURITY-PRIVACY.md) before implementing any part of this design.
