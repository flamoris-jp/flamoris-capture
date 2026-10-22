# FLAMORIS Capture

**See what happened. Understand how it happened.** 🎬

A Windows screen-observation toolkit for people and AI: a reusable capture DLL, an independent desktop EXE, and an MCP adapter over the same permissioned session. FLAMORIS applications and unrelated Windows applications can be observed without modifying them.

**Status: initial one-window screenshot implementation (0.1.0 development preview).** Source, automated contracts and build verification are separate from physical Windows acceptance and release. No stable release or installer is claimed. See [PROGRESS.md](PROGRESS.md).

## What works in this implementation

- A human-visible WPF EXE lists windows locally, binds one Windows Graphics Capture item, and asks for capture consent.
- Local one-shot preview works independently of MCP; sharing is a separate opt-in. Both permissions start OFF.
- MCP exposes only `capture.session.get` and `capture.screenshot.get`, using **Flamoris.Mcp.Core / Wpf 1.3.1** for authorization, shared connection UI, manual/Hub/tunnel connections, revocation and standard single-image results.
- Each screenshot is PNG/JPEG, at most 1 MiB, with bounded ROI/downscale, actual dimensions, hash, source geometry/DPI and frame time metadata. No arbitrary target/path selector is exposed to MCP.
- Stop/revoke, target loss/change, lock/disconnect, suspend and shutdown invalidate the old session and grants. No ongoing recorder or capture cache exists.

**Not implemented:** rectangle/monitor capture, video recording, indexed frames/contact sheets/comparison, interaction-event trace, image saving/export UI, installer/signing. Only the selected window is eligible; the backend never falls back to the desktop. Actual minimized, protected, occluded, mixed-DPI and GPU behavior has a separate [hands-on acceptance gate](docs/WINDOWS-ACCEPTANCE.md).

## Build and run

Windows interactive desktop, .NET 10 SDK, D3D11/WGC-capable graphics. Windows API floor: build 19041; win-x64 preview target. This is not a validated OS/driver support matrix.

```powershell
dotnet restore Flamoris.Capture.slnx
dotnet build Flamoris.Capture.slnx -c Release --no-restore
dotnet test tests/Flamoris.Capture.Tests -c Release --no-build
./scripts/publish.ps1
./artifacts/win-x64/Flamoris.Capture.App.exe
```

The publish script places the app, reusable DLLs, Core-based bridge and self-contained .NET runtime together. Keep the entire folder; the EXE alone is not the bundle. Development can also use `dotnet run --project src/Flamoris.Capture.App`; manual/tunnel MCP needs the published bridge alongside the app. Restore uses public nuget.org, without a GitHub token.

See [Setup](docs/SETUP.md) for the DLL contract, connection instructions and request examples, and [ADR 0001](docs/decisions/0001-one-window-screenshot.md) for implementation limits and decisions.

## Usage

1. Refresh the local window list and choose one window.
2. Click **このウィンドウの撮影を許可**, review the current target name, and confirm.
3. Click **静止画を取得** for local preview. This does not share pixels.
4. To share, check **この対象の画像をMCPで共有する**, then use **MCP / AI → 接続** and choose **Read only**. Connection settings are provided by the shared desktop MCP subsystem.
5. The MCP client obtains `documentToken` from `mcp.context` and uses it as `sessionId`. Each screenshot request acquires one fresh frame and ends acquisition.
6. Uncheck sharing to revoke MCP while retaining local capture consent, or click **すべて停止・許可解除** to clear everything. Switching targets requires new consent and sharing authorization.

The window contains whatever the target application displays; browser tabs, names and documents can be sensitive. Titles remain local UI only. No free text, keystrokes, clipboard, input injection or all-system hooks are recorded.

## Assemblies and ownership

| Component | Responsibility |
|---|---|
| `Flamoris.Capture.Core` | Authoritative session, consent/epoch, busy/cancel and pure image policies; no UI/MCP dependency |
| `Flamoris.Capture.Windows` | Reusable WGC/D3D11 capture DLL, target identity, bounded image encoding |
| `Flamoris.Capture.App` | Standalone EXE, human target/permission controls, local preview and OS lifecycle |
| `Flamoris.Capture.Mcp` | Two typed query adapters over the same session; no second target authority |
| `Flamoris.Capture.Bridge` | Executable entry point to packaged MCP Core's stdio bridge |
| MCP Core / Wpf | Transport, same-user capability, RequestGuard, disclosure checks, connection UI/providers |

Capture observes visible results; Cutwork/2D/Kachinco retain their own documents, edits and internal-state MCP. Infrastructure [Observer](https://github.com/flamoris-jp/flamoris-observer) is a different responsibility. Hub is an optional connection provider, not a mandatory capture server.

## Later: selective video observation

The [basic design](docs/BASIC-DESIGN.md) remains the roadmap for **summary → contact sheet → selected frame / ROI → optional comparison/events**. Human playback and AI observation will use the same bounded recording asset; default AI observation will not upload the entire video. These video tools are not registered in this implementation.

## Development

Read [AGENTS.md](AGENTS.md), [Setup](docs/SETUP.md), [Architecture](docs/ARCHITECTURE.md), [Privacy](docs/SECURITY-PRIVACY.md) and [Test plan](docs/TEST-PLAN.md). CI builds/publishes Windows and runs an official MCP-client round trip using synthetic pixels. It does not certify real-display capture. Do not commit real user captures, tokens or machine-specific settings.

FLAMORIS: [organization map](https://github.com/flamoris-jp/.github) · [Desktop ecosystem](https://github.com/flamoris-jp/flamoris-commons/blob/main/docs/desktop-ecosystem.md) · [MCP Core](https://github.com/flamoris-jp/flamoris-mcp-core).

## License and support

Source is [Apache-2.0](LICENSE). Windows SDK/platform APIs and NuGet dependencies retain their own terms; no model, media or third-party capture backend is vendored. See ADR 0001 for dependencies. FLAMORIS is provided as-is, without guaranteed individual support.
