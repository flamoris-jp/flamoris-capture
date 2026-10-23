# Setup & development

**Current slice:** 0.2.0 — one selected window, screenshots, recording/MP4, frame/contact-sheet queries and opt-in key/mouse events. Current contract: [ADR 0002](decisions/0002-simple-observation-recording.md).

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
./product/packaging/publish-windows.ps1
./artifacts/windows/FLAMORIS-Capture-win-x64/Flamoris.Capture.App.exe
```

`product/packaging/publish-windows.ps1` creates a self-contained app and bridge in one `artifacts/windows/FLAMORIS-Capture-win-x64` folder. Keep the folder intact. It does not install a service, publish a GitHub release, sign binaries or change any production machine.

For routine use from a Git checkout, close Capture before publishing and run:

```powershell
git switch main
git pull --ff-only
./product/packaging/publish-windows.ps1
./artifacts/windows/FLAMORIS-Capture-win-x64/Flamoris.Capture.App.exe
```

The existing `scripts/publish.ps1` remains the implementation and its legacy default output is `artifacts/win-x64`; the family entry point selects the standard package folder. Hub preferences/Windows credentials are per-user and survive either output location. Target consent and MCP sharing still start OFF after relaunch.

`dotnet run --project src/Flamoris.Capture.App` is sufficient for local preview. For local MCP from that development output, put the published `Flamoris.Capture.Bridge.exe` and its dependencies beside the development app, or use the published app bundle directly. The bridge is a thin entry point to Core's implementation, not a second MCP server.

Linux can cross-build with the repository's `EnableWindowsTargeting` and run the pure Core/MCP contract tests. WGC/WPF execution requires Windows. The transport test is explicitly skipped unless `FLAMORIS_CAPTURE_TEST_BRIDGE` is set; Windows CI sets it to the published bridge path and must pass that test.

```powershell
$env:FLAMORIS_CAPTURE_TEST_BRIDGE = (Resolve-Path artifacts/windows/FLAMORIS-Capture-win-x64/Flamoris.Capture.Bridge.exe).Path
dotnet test tests/Flamoris.Capture.Tests -c Release --no-build
```

## Local controls and MCP

Follow the [README usage](../README.md#usage). Target selection is local; **画面共有** is the sole screen-acquisition checkbox and **操作記録** is independent. The first enabled capability presents the common safety notice. If suppression is not selected, the notice appears on the first enable in each new app run. Settings can restore the notice even during that run.

All acquisition starts OFF, including when provider preferences remain saved. MCP only accepts Read only. Connect after enabling the intended capabilities; capability/target changes can revoke an old connection and require reconnection. Operation-only mode allows session/summary/event queries, while image tools reject access without screen sharing.

| Local tool | Input inside `input` | Result |
|---|---|---|
| `capture.session.get` | `{}` | Target/session permission metadata and limits |
| `capture.screenshot.get` | `{}`; optional `roi`, `maxEdge`, `format`, `requireExactSize` | One fresh PNG/JPEG image and metadata |
| `capture.recording.get` | `{}` | Current recording state, frame/event counts, bytes, stop reason |
| `capture.frame.get` | `{}`, `frameIndex` OR `timeMs` | Latest or selected recorded frame, one image |
| `capture.contactSheet.get` | optional `count` (1–9, default 6) | One image with cell bounds, frame indices and timestamps |
| `capture.events.get` | optional `afterSequence` (default 0), `limit` (1–500, default 100) | Bounded event page and dropped-event count |

No tool starts recording, changes the target, simulates input or accepts arbitrary file paths. Use the human UI to start/stop/save. All tools accept an optional nonempty `sessionId` for older clients; an explicitly supplied stale ID is refused. Core `guard` is still supported. **The normal client does not need either an ID lookup or an ID argument.**

Screenshot:

```json
{"input":{}}
```

A recorded moment (milliseconds from recording start):

```json
{"input":{"timeMs":1200}}
```

Contact sheet and events:

```json
{"input":{"count":6}}
```

```json
{"input":{"afterSequence":0,"limit":100}}
```

`frameIndex` is the acquired JPEG-frame index, zero-based; `timeMs` selects the last acquired frame at or before the requested time. Before-first/after-last/out-of-range selectors fail. Omit both for latest. This is not an imported MP4's decoder index. MP4 output is encoded from these frames, with the first acquired frame at MP4 time zero. The JSON records that first frame's offset from the shared event/recording origin; do not infer MP4 time from nominal FPS.

Screenshots remain at most 1 MiB / max edge 1920 (default 1280 JPEG); ROI uses physical source pixels. Recorded frames are max edge 1280 and at most 1 MiB each. Contact sheets are at most 1200×900 / 1 MiB. Unknown/duplicate/null properties and invalid selectors are rejected. Recording stalls may leave timestamp gaps; use the actual times. A changed output size stops recording with `geometry_changed`.

For Windows platform encoder/contact-sheet smoke tests (synthetic frames, no desktop capture):

```powershell
dotnet test tests/Flamoris.Capture.Windows.Tests -c Release --no-build
```

First-time Hub registration and upgrading an existing 3-tool catalog to 7 tools: [HUB-CONNECTION.md](HUB-CONNECTION.md). Hub tool names acquire the existing namespace (`capture.capture.frame.get`, for example).

## Embed the DLL

Reference `Flamoris.Capture.Core` and `Flamoris.Capture.Windows` from a Windows .NET 10 host. On the host's local target-selection UI, obtain a `WindowChoice` from `WindowTarget.List`, create a `WindowTarget.Select(choice)`, show its `DisplayName` and call `CaptureSession.SelectAndAllow(target)`. Construct the session with `new WindowCaptureBackend()`.

After human screen consent, call `session.SetSharing(true)`; then await `session.CaptureAsync(request, disclosure: false, cancellationToken)`. The result owns encoded bytes and metadata; it does not save a file. The embedding host owns consent UI, preview removal, idle `CheckTarget`, lock/disconnect/suspend handling and disposal, as demonstrated by the EXE. The capture DLL itself neither opens MCP nor owns a second user consent authority.

For MCP, reference `Flamoris.Capture.Mcp` and adapt the same session with `CaptureMcpHost`. Do not instantiate a second session/target for AI requests. Core authorization is in addition to product consent. Pass `ContactSheetRenderer` to `CaptureMcpHost` for sheet rendering. `session.Observation` owns the bounded recording and event timeline; `VideoExporter` saves a stopped recording locally.

## Key-recording smoke test

Enable **操作記録**, then click the selected application's timeline/canvas (outside a text-entry control) and press Left three times and Space three times. Read `capture.events.get`; expect physical `keyDown` / `keyUp` events for `Left` and `Space`. These keys may move the playhead or start playback, so use a test project. This case passed in Kachinco on 2026-10-24; see [Windows acceptance](WINDOWS-ACCEPTANCE.md).

Typing into a recognized text-entry field is not a valid test of command-key capture: password, Edit, TextPattern and writable ValuePattern controls suppress keyboard events. The app does not translate physical keys into Unicode/IME text. A custom control or automation-provider failure can affect the result; do not infer universal secret detection.

Read all event pages: pass the returned `nextSequence` as `afterSequence` until no further events remain. Sequence numbers need not restart at 1 for each recording; use the returned cursor rather than event counts. `droppedEvents: 0` reports no queue eviction, not proof that every physical device packet was captured.

## Troubleshooting

- Hub reports the Capture upstream offline: connect Capture through its MCP menu after enabling the intended capabilities; catalog registration alone does not connect the desktop app.
- Updated source but old UI: close Capture, switch to `main`, pull, republish, and launch the EXE in the new output folder. `git pull` alone does not update an existing EXE.
- Mouse events but no keys: confirm the selected target is foreground and focus its timeline/canvas rather than a recognized text-entry control; then inspect all event pages. Separate dialogs/popups with a different root are outside the selected-window trace.
- `target_lost`: reselect and authorize a live window; the app never follows a replacement window.
- `frame_unavailable`: restore a minimized/hidden window, let resizing settle, retry. Protected/secure/occluded behavior is not guaranteed; there is no broader capture fallback.
- `unsupported_capability`: required WGC capability/target kind is unavailable.
- `resource_limit`: shrink ROI/maxEdge or allow byte-budget scaling. Source >8,294,400 pixels or edge >8192 is rejected before GPU acquisition.
- `stale_session`, `unauthorized`, `forbidden`: obtain new consent and connection/context; do not reuse an old capability/session ID.
- `busy`: one acquisition is still running/cleaning up; no requests are queued.
- `timeout`: a shot exceeded the 5-second deadline. Native driver/encoder cleanup may outlive result cancellation; Stop still revokes immediately.

See [Windows acceptance](WINDOWS-ACCEPTANCE.md) for the basic real-Windows workflow confirmed on 2026-10-24 and the remaining platform/lifecycle/export checks. A confirmed workflow does not imply a stable release.
