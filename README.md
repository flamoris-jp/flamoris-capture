# FLAMORIS Capture

**See what happened. Understand how it happened.** 🎬

A Windows screen-observation toolkit for people and AI: a reusable capture DLL, an independent desktop EXE, and an MCP adapter over the same permissioned session. FLAMORIS applications and unrelated Windows applications can be observed without modifying them.

**Status: 0.2.0 development preview source.** One approved window, direct MCP screenshots, bounded recording, selective frame observation and opt-in interaction recording are implemented. The basic real-Windows workflow was confirmed on 2026-10-24: MCP screenshots, recording, selective frames/contact sheets and mouse/key events. Broader platform/lifecycle acceptance remains separate; no stable release/installer is claimed. See [PROGRESS.md](PROGRESS.md).

## What works

- Compact toolbar and large preview; the human chooses one window. **画面共有** gates all live pixel acquisition, recording and MCP images. Both checkboxes start OFF at restart.
- No session ID lookup: `capture.screenshot.get` and `capture.session.get` accept `{ "input": {} }`. Optional explicit session IDs remain compatible and stale IDs are rejected.
- **録画開始 / 録画停止** retain a bounded JPEG frame sequence (nominal 10fps, max edge 1280, 15 minutes / 9,000 frames / 512 MiB of frame files). The EXE plays/scrubs it and saves silent H.264 MP4 using Windows' encoder after acquisition stops. Source stalls can reduce actual FPS; timestamps preserve gaps. Cursor pixels are excluded.
- AI obtains a summary, a selected frame by index/time, a single contact sheet of up to nine frames, or a page of operation events. The whole video is never sent through MCP.
- **操作記録** independently records physical keys (including Blender single-letter/numpad keys), target-local mouse buttons/movement/wheel. Only the selected foreground root window qualifies. Yellow **操作記録中** and red **録画中** badges reflect actual acquisition state.
- A shared first-use notice has only OK and an initially unchecked **次回からこのメッセージを表示しない**. **設定 → 安全確認メッセージを表示する** restores it. No acquisition begins before OK.
- Core/Wpf 1.3.1 still own MCP transport, authorization, providers, connection UI and single-image results. No second session/target authority is introduced.

**Limitations:** one window only; no monitor/rectangle capture, audio, cursor overlay, imported-video player, input replay, comparison, installer or signing. Input recording suppresses keys in UI Automation-recognized password/editable/text controls; custom controls may not report their nature, and physical key sequences can reveal typed information. The notice and explicit stop remain essential. The tested workflow and remaining OS/GPU/Blender cases are recorded in [Windows acceptance](docs/WINDOWS-ACCEPTANCE.md).

## Build and run

Windows interactive desktop, .NET 10 SDK, D3D11/WGC-capable graphics. Windows API floor: build 19041; win-x64 preview target. This is not a validated OS/driver support matrix.

```powershell
dotnet restore Flamoris.Capture.slnx
dotnet build Flamoris.Capture.slnx -c Release --no-restore
dotnet test tests/Flamoris.Capture.Tests -c Release --no-build
./product/packaging/publish-windows.ps1
./artifacts/windows/FLAMORIS-Capture-win-x64/Flamoris.Capture.App.exe
```

For routine updates, close Capture, switch to `main` with `git switch main`, run `git pull --ff-only`, then repeat the publish and EXE commands above. Pulling source alone does not rebuild the EXE; launch the newly published bundle. The desktop-family entry point is `product/packaging/publish-windows.ps1`. The legacy `scripts/publish.ps1` entry point still defaults to `artifacts/win-x64`.

The publish script places the app, reusable DLLs, Core-based bridge and self-contained .NET runtime together. Keep the entire folder; the EXE alone is not the bundle. Development can also use `dotnet run --project src/Flamoris.Capture.App`; manual/tunnel MCP needs the published bridge alongside the app. Restore uses public nuget.org, without a GitHub token.

See [Setup](docs/SETUP.md) for the DLL contract and request examples, [Hub connection](docs/HUB-CONNECTION.md) for first-time registration, and [ADR 0001](docs/decisions/0001-one-window-screenshot.md) for implementation limits and decisions.

## Usage

1. Choose a window and click **対象にする**. This binds a target without acquiring pixels.
2. Enable **画面共有**, acknowledge the notice, then connect **MCP / AI → 接続 → 読み取り専用**. The preview updates only while enabled. AI can now request the currently shared window without knowing an internal ID.
3. Optionally enable **操作記録**. It works with screen sharing OFF too; connect MCP after enabling the desired capabilities. Recognized text-entry controls are excluded. Other foreground applications are excluded.
4. For video, enable screen sharing and click **録画開始**. **録画停止** finishes frame acquisition; **録画を見る**, the slider and **再生** inspect it. **MP4を保存** exports a silent video; **操作履歴を保存** exports synchronized JSON. A new recording replaces the previous temporary recording and starts a fresh frame/event timeline.
5. Screen sharing OFF immediately cancels live pixel acquisition/recording, clears the live preview and revokes the old MCP grant. Operation recording has its own OFF switch. **すべて停止** also clears the target and stops both. Restart/target loss/OS lock never resumes acquisition automatically.

Stopped footage remains locally saveable until replaced or the app exits. Previously revoked footage cannot become MCP-readable merely by enabling sharing again. Temporary frame files are deleted on normal replacement/exit; a crash may leave files in the current user's temporary `FLAMORIS-Capture` directory. MP4/JSON files deliberately saved by the human remain under their control.

See [Setup](docs/SETUP.md), [current decision](docs/decisions/0002-simple-observation-recording.md), [Hub catalog upgrade](docs/HUB-CONNECTION.md) and [Windows acceptance](docs/WINDOWS-ACCEPTANCE.md).

## Assemblies and ownership

| Component | Responsibility |
|---|---|
| `Flamoris.Capture.Core` | Authoritative session, consent/epoch, busy/cancel and pure image policies; no UI/MCP dependency |
| `Flamoris.Capture.Windows` | Reusable WGC/D3D11 capture DLL, target identity, bounded image encoding |
| `Flamoris.Capture.App` | Standalone EXE, human target/permission controls, local preview and OS lifecycle |
| `Flamoris.Capture.Mcp` | Six typed query adapters over the same session; no second target authority |
| `Flamoris.Capture.Bridge` | Executable entry point to packaged MCP Core's stdio bridge |
| MCP Core / Wpf | Transport, same-user capability, RequestGuard, disclosure checks, connection UI/providers |

Capture observes visible results; Cutwork/2D/Kachinco retain their own documents, edits and internal-state MCP. Infrastructure [Observer](https://github.com/flamoris-jp/flamoris-observer) is a different responsibility. Hub is an optional connection provider, not a mandatory capture server.

## Selective video observation

The current contract is [ADR 0002](docs/decisions/0002-simple-observation-recording.md). The earlier [basic design](docs/BASIC-DESIGN.md) is a broader roadmap, including imported assets, advanced selection, comparison and retention policy. It is not the current wire API.

## Development

Read [AGENTS.md](AGENTS.md), [Setup](docs/SETUP.md), [Architecture](docs/ARCHITECTURE.md), [Privacy](docs/SECURITY-PRIVACY.md) and [Test plan](docs/TEST-PLAN.md). CI builds/publishes Windows and runs an official MCP-client round trip using synthetic pixels. It does not certify real-display capture. Do not commit real user captures, tokens or machine-specific settings.

FLAMORIS: [organization map](https://github.com/flamoris-jp/.github) · [Desktop ecosystem](https://github.com/flamoris-jp/flamoris-commons/blob/main/docs/desktop-ecosystem.md) · [MCP Core](https://github.com/flamoris-jp/flamoris-mcp-core).

## License and support

Source is [Apache-2.0](LICENSE). Windows SDK/platform APIs and NuGet dependencies retain their own terms; no model, media or third-party capture backend is vendored. See ADR 0001 for dependencies. FLAMORIS is provided as-is, without guaranteed individual support.

## Embedded editor observation

`Flamoris.Capture.Wpf` supplies a visible status indicator, connection-bound window observation and
two screenshot tools for an editor's existing MCP connection. Cutwork and 2D can
reference it without launching the standalone EXE. See
[ADR 0003](docs/decisions/0003-embedded-editor-observation.md).
