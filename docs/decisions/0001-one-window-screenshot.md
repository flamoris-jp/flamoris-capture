# ADR 0001: One-window screenshot MVP

> The startup, MCP grant and sharing-consent clauses below are superseded by [ADR 0004](0004-application-mcp-lifetime.md). Current Core/Wpf/bridge is 1.4.0. Historical versions and acceptance evidence below describe the original decision, not the current connection procedure.

Accepted for source implementation, 2026-10-22. Windows hands-on acceptance and release remain separate gates.

Superseded in part by [ADR 0002](0002-simple-observation-recording.md): current screen gate, optional IDs, recording and interaction controls. This file records the earlier screenshot-only decision.

## Decision

- .NET 10, WPF EXE, Windows Graphics Capture (WGC), D3D11 hardware device, BGRA8, one frame-pool buffer. API floor: Windows build 19041 (Windows 10 2004), win-x64 preview packaging. This is an API minimum, not a validated Windows/GPU support matrix.
- `Flamoris.Capture.Core` owns the capture session, consent, epochs and pure image policy. `Flamoris.Capture.Windows.dll` owns immutable WGC items, acquisition and encoding. `Flamoris.Capture.Mcp` adapts the same session to **NuGet Flamoris.Mcp.Core 1.3.1**. The EXE uses **Flamoris.Mcp.Wpf 1.3.1** for connection UI, Hub/manual/tunnel providers and indicators.
- `Flamoris.Capture.Bridge` is a small executable entry point to the package's `StdioBridge.RunAsync`; no protocol, pipe authentication or MCP server is reimplemented. The shared WPF subsystem starts Core's local endpoint or outbound Hub provider.
- Human selection creates one WGC item, followed by confirmation of the item's current display name. After confirmation that item is never recreated from HWND. Its Closed event, original PID/process start time, validity checks and host-generated target/session IDs prevent automatic substitution. Window titles and HWND never enter MCP results or diagnostics. The selection list is local UI only.
- Capture and sharing start OFF. Local capture authorization does not enable MCP. Sharing opt-in precedes the shared MCP connection confirmation; only Read only is accepted. Restart requires new target/capture/sharing consent even if provider preferences exist. No automatic endpoint is opened on application startup.
- The host's single lock serializes permission changes, session replacement and final disclosure checks. Core uses RuntimeId plus sessionId as DocumentToken; Core revision is the host's permission epoch. Revoke, target loss/replacement, Stop, lock, logoff, console/remote disconnect, suspend and shutdown invalidate the old workspace/grant. Sharing-only revoke preserves local capture consent, cancels active acquisition and rotates sessionId. No cache exists.
- One shot per request, one admission slot, no waiting queue. Source: <=8,294,400 pixels and each edge <=8192 before pool creation. One image: <=1 MiB, max edge <=1920, default 1280 JPEG80. ROI is a half-open physical-pixel rectangle and is rejected if out of bounds or overflowed. Output never upsizes.
- JPEG tries 80/65/50 then 75% dimensions at most three times. PNG changes dimensions only. `requireExactSize` preserves the initial maxEdge-derived size (JPEG may change quality). If it cannot fit, return resource_limit. Both the product and Core validate output size; Core validates the base64 wire budget (default 4 MiB).
- CPU pixels are cropped/resampled within ROI with bounded nearest-neighbour sampling; this intentionally favors simple deterministic scope over interpolation quality. Each output reports actual size, ROI, scale, encoding, SHA-256, source geometry/DPI, WGC QPC ticks, UTC observed at frame receipt and unknown synchronization error (`null`). The geometry origin is DWM extended-frame bounds and requires physical DPI validation. The WGC timestamp is not claimed to be synchronized with external app events.
- Size/position/DPI changes during a shot return frame_unavailable; retry after the window settles. Frame ContentSize refreshes the next request's pool dimensions; the immutable item is not recreated and its initial Size is not assumed to track resize. Minimized/hidden windows are rejected. Occluded, protected, secure and HDR cases require hands-on evidence; black pixels do not prove protected content. No GDI, desktop or other-window fallback. Cursor capture is disabled; separate popups/menus are not guaranteed.
- Resources are request-owned and disposed; pixels remain only in in-flight results and the human-visible local preview. No automatic disk/network storage, arbitrary import/path tools, videos, traces or recorder. A 5-second product/Core deadline cancels results and checks between acquisition/encoding stages. Native GPU copy/encoder calls may outlive cancellation; admission stays occupied until cleanup finishes. A stalled driver is not claimed forcibly interruptible. UI Stop synchronously revokes first and never waits for the worker.

## Memory envelope

Source caps apply before GPU pool creation. Pool buffer and acquired SoftwareBitmap are at most about 32 MiB each; CPU source is about 32 MiB; max output BGRA is <15 MiB; encoder scratch and its compressed stream are bounded indirectly by these image dimensions. This is **not** a measured RSS/GPU peak or a process-wide hard byte quota. There is no persistent capture surface, waiting queue or image cache. Measure actual GPU/CPU peak and release behavior in the hands-on gate before making capacity claims. Failed/oversized encoding never yields a truncated image.

## Verification boundary

Pure helpers/session/adapter tests use synthetic pixels. The official MCP client test exercises the published bridge and same-user endpoint in Windows CI using synthetic pixels. It does not exercise WGC or prove a physical client rendered a real window. Use [WINDOWS-ACCEPTANCE.md](../WINDOWS-ACCEPTANCE.md) for actual display acceptance. Region, monitor, video and event tools are not registered.

## Dependencies / licensing

Production packages: Flamoris.Mcp.Core/Wpf 1.3.1 (Apache-2.0), transitive Flamoris.Logging 1.0.0 and ModelContextProtocol.Core 2.2.0; Windows SDK reference/runtime projection through the Windows target framework. No model, media, third-party capture implementation or codec binary is vendored. Windows platform imaging encoders are used for PNG/JPEG; video codec choice is deferred.

## Primary references

- [WGC capture](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)
- [IGraphicsCaptureItemInterop::CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
- [Free-threaded frame pool](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)
- [WGC SystemRelativeTime](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.systemrelativetime)
- [MCP Core](https://github.com/flamoris-jp/flamoris-mcp-core)
