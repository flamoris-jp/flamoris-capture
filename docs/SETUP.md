# Setup & development

**Current slice:** one selected window, still image, reusable DLL + WPF EXE + MCP. Video/region/monitor/events are future work. See [ADR 0001](decisions/0001-one-window-screenshot.md).

## Prerequisites

- .NET 10 SDK; `global.json` accepts the latest installed 10.0 feature band.
- An interactive Windows desktop with WGC and a D3D11 hardware device for real capture. API floor is Windows build 19041; win-x64 is the preview distribution target. Real OS/GPU support remains subject to acceptance.
- Ordinary current-user execution; no administrator permission, service, GPU daemon, secret environment setup or private package feed is required.
- Public NuGet restore: Flamoris.Mcp.Core and Flamoris.Mcp.Wpf **1.3.1**, plus their transitive logging/official MCP SDK dependencies. No DLL vendoring.

For an authenticated checkout:

```powershell
gh auth status
gh repo clone flamoris-jp/flamoris-capture
cd flamoris-capture
```

If required, run `gh auth login`; use `gh auth setup-git` for later authenticated Git operations. Do not use a GitHub account password for Git.

## Build / test / preview bundle

```powershell
dotnet restore Flamoris.Capture.slnx
dotnet build Flamoris.Capture.slnx -c Release --no-restore
dotnet test tests/Flamoris.Capture.Tests -c Release --no-build
./scripts/publish.ps1
./artifacts/win-x64/Flamoris.Capture.App.exe
```

`publish.ps1` creates a self-contained app and bridge in one `artifacts/win-x64` folder. Keep the folder intact. It does not install a service, publish a GitHub release, sign binaries or change any production machine.

`dotnet run --project src/Flamoris.Capture.App` is sufficient for local preview. For local MCP from that development output, put the published `Flamoris.Capture.Bridge.exe` and its dependencies beside the development app, or use the published app bundle directly. The bridge is a thin entry point to Core's implementation, not a second MCP server.

Linux can cross-build with the repository's `EnableWindowsTargeting` and run the pure Core/MCP contract tests. WGC/WPF execution requires Windows. The transport test is explicitly skipped unless `FLAMORIS_CAPTURE_TEST_BRIDGE` is set; Windows CI sets it to the published bridge path and must pass that test.

```powershell
$env:FLAMORIS_CAPTURE_TEST_BRIDGE = (Resolve-Path artifacts/win-x64/Flamoris.Capture.Bridge.exe).Path
dotnet test tests/Flamoris.Capture.Tests -c Release --no-build
```

## Local capture and sharing

1. Refresh windows, choose one, click **このウィンドウの撮影を許可**, and confirm the bound target's current display name. No frame is acquired before consent.
2. Click **静止画を取得** to preview locally. Each shot stops acquisition after one frame. There is no background recorder.
3. Check **この対象の画像をMCPで共有する** separately. Then use **MCP / AI → 接続** and choose **Read only**; this product rejects Edit connections.
4. The shared **MCP / AI → 設定** dialog supports Manual, OpenAI tunnel-client and Hub provider settings. Auto-connect cannot bypass capture/sharing consent, which reset at restart. Provider preferences are current-user local settings, not source-controlled deployment defaults. Credentials use the shared credential handling; do not put transient capability JSON in Issues or Git.
5. For Manual, copy the transient client connection configuration from the shared settings dialog into the MCP client. The capability is scoped to this running session and is invalid after revoke/target change/exit. For Hub, configure a reviewed catalog with `mcp.context` plus the two tools below; no Hub/server catalog is modified by this repository's build.
6. Uncheck sharing to revoke MCP. **すべて停止・許可解除** also clears the target and local preview. Lock, suspend/disconnect, target destruction and shutdown stop the session; unlocking never resumes consent automatically.

No window enumeration or target changes are exposed to AI. Window titles appear only in the local picker/confirmation. Broader monitor or rectangle capture is not available.

First-time Hub registration and the optional human-run SSH helper: [HUB-CONNECTION.md](HUB-CONNECTION.md).

## MCP tools

Use `mcp.context` first. Its `documentToken` is the Capture `sessionId`; its `runtimeId` and `documentToken` can also be supplied as the Core `guard`. Example Core wire arguments:

```json
{"input":{"sessionId":"<documentToken>"}}
```

Use this with `capture.session.get` to retrieve metadata, consent state, supported queries and effective limits. It contains no image or window list.

```json
{
  "input": {
    "sessionId": "<documentToken>",
    "format": "png",
    "maxEdge": 1280,
    "roi": {"x":0,"y":0,"width":640,"height":480},
    "requireExactSize": false
  },
  "guard": {"runtimeId":"<runtimeId>","documentToken":"<documentToken>"}
}
```

Use this with `capture.screenshot.get`. ROI is optional and expressed in source physical pixels; omit it for the whole selected window. Requests reject unknown/duplicate properties, missing required fields, invalid bounds and oversized dimensions. `format` defaults to `jpeg`, maxEdge to 1280. Hard output edge is 1920; encoded bytes <=1 MiB. `requireExactSize` prevents additional byte-budget downscaling after the initial maxEdge resize; it does not upsize. Response includes exactly one standard PNG/JPEG MCP image and metadata (`sha256`, dimensions, ROI/scales, actual quality, source geometry/DPI, QPC ticks, UTC and permission epoch). Bytes are not duplicated in structured metadata.

Tool names may acquire an external namespace when routed through a Hub. The tools own no file/network/process/shell authority. Video tools in BASIC-DESIGN remain unimplemented.

## Embed the DLL

Reference `Flamoris.Capture.Core` and `Flamoris.Capture.Windows` from a Windows .NET 10 host. On the host's local target-selection UI, obtain a `WindowChoice` from `WindowTarget.List`, create a `WindowTarget.Select(choice)`, confirm its `DisplayName` and call `CaptureSession.SelectAndAllow(target)`. Construct the session with `new WindowCaptureBackend()`.

Use `session.Info.SessionId` in `ScreenshotRequest`, then await `session.CaptureAsync(request, disclosure: false, cancellationToken)`. The result owns encoded bytes and metadata; it does not save a file. The embedding host owns consent UI, preview removal, idle `CheckTarget`, lock/disconnect/suspend handling and disposal, as demonstrated by the EXE. The capture DLL itself neither opens MCP nor owns a second user consent authority.

For MCP, reference `Flamoris.Capture.Mcp` and adapt the same session with `CaptureMcpHost`. Do not instantiate a second session/target for AI requests. Core authorization is in addition to `session.SetSharing(true)`, not a replacement.

## Troubleshooting

- `target_lost`: reselect and authorize a live window; the app never follows a replacement window.
- `frame_unavailable`: restore a minimized/hidden window, let resizing settle, retry. Protected/secure/occluded behavior is not guaranteed; there is no broader capture fallback.
- `unsupported_capability`: required WGC capability/target kind is unavailable.
- `resource_limit`: shrink ROI/maxEdge or allow byte-budget scaling. Source >8,294,400 pixels or edge >8192 is rejected before GPU acquisition.
- `stale_session`, `unauthorized`, `forbidden`: obtain new consent and connection/context; do not reuse an old capability/session ID.
- `busy`: one acquisition is still running/cleaning up; no requests are queued.
- `timeout`: a shot exceeded the 5-second deadline. Native driver/encoder cleanup may outlive result cancellation; Stop still revokes immediately.

See [Windows acceptance](WINDOWS-ACCEPTANCE.md) before calling the preview tested on physical Windows or released.
