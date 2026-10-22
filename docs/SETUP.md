# Setup & Development / セットアップ

## Current state (2026-10-22)

This is a **documentation/bootstrap repository**. It does not yet contain a .NET solution, `src/` tree, runnable `Capture.exe`, NuGet DLL, installer, packaged MCP bridge, or executable tests. Do **not** expect a build or startup command to work today.

The purpose of this guide is to make the first implementation reproducible without inventing build paths or claiming a verified Windows install.

## 1. Intended development environment

- **Windows desktop:** Windows 10/11 is a design target. Exact minimum build, WGC API availability, and supported graphics/display configurations **must be confirmed** before the first beta; locked session/secure desktop support is **not promised**.
- **SDK:** .NET 10 SDK is the proposed baseline to align with existing FLAMORIS Windows projects. Verify SDK/runtime/Windows SDK requirements when source projects are added.
- **Git:** Git and optionally GitHub CLI (`gh`) for authenticated repository access. This repository is currently public.
- **Display:** A real interactive logged-in Windows session, target application window, GPU/graphics driver and (for video) an encoder path will be needed for end-to-end tests.
- **External dependencies:** FLAMORIS MCP Core is an intended reusable dependency, not yet a configured PackageReference; pin a reviewed released version before implementation.

## 2. Obtain the repository

With GitHub CLI already signed in:

```powershell
gh auth status
gh repo clone flamoris-jp/flamoris-capture
cd flamoris-capture
git status
```

Or clone the public repository via your usual Git tooling. Authentication problems should be resolved through `gh auth login` or the connected GitHub integration; do not use GitHub account passwords over HTTPS.

## 3. Read before implementing

1. [AGENTS.md](../AGENTS.md): repository rules.
2. [README.md](../README.md): product role and what is **not implemented**.
3. [ARCHITECTURE.md](ARCHITECTURE.md): proposed DLL/EXE/MCP separation and ownership.
4. [SECURITY-PRIVACY.md](SECURITY-PRIVACY.md): explicit consent, capture scope, safe event recording and retention.
5. [TEST-PLAN.md](TEST-PLAN.md): phase gates and Windows acceptance matrix.
6. [PROGRESS.md](../PROGRESS.md): evidenced status.

Initial requirements were captured in [private Issue Inbox #9](https://github.com/flamoris-net/flamoris-issue/issues/9); request access if necessary.

## 4. Proposed implementation layout (NOT created yet)

```text
src/
  Flamoris.Capture.Core/          # UI/MCP-neutral contracts and bounds
  Flamoris.Capture.Windows/       # Windows capture/encoder backend
  Flamoris.Capture.App/           # Standalone Windows GUI host
  Flamoris.Capture.Mcp/           # Typed MCP tools over App capture session
tests/
  Flamoris.Capture.Tests/
docs/
  SETUP.md ARCHITECTURE.md SECURITY-PRIVACY.md TEST-PLAN.md
PROGRESS.md
```

Choose actual assembly names and solution structure in a reviewed first PR. Do not create an abstract framework or duplicate `Flamoris.Mcp.Core` grant/transport plumbing.

## 5. Commands after the first solution exists

**These are illustrative templates, NOT verified instructions for the current checkout:**

```powershell
dotnet --info
# After adding and documenting the actual .slnx/.sln:
# dotnet restore <actual-solution-path>
# dotnet build <actual-solution-path> -c Release
# dotnet test <actual-solution-path> -c Release
# dotnet run --project <actual-app-project>
```

A first real PR must replace placeholders with tested commands, identify the Windows SDK/Windows App SDK references actually required, and document dependencies (including encoder licensing) and clean-machine prerequisites. Linux headless tests cannot prove GUI capture on real Windows.

## 6. Minimum hands-on test after first implementation

1. Start the EXE in an interactive Windows session; verify capturing is **off** by default.
2. Select a specific test window in the EXE and authorize it.
3. Capture **one** still PNG or JPEG locally, verify content, size, target identity and DPI metadata.
4. Explicitly enable MCP disclosure and use an approved MCP client to request that **same target**. Verify the client actually receives/render an image.
5. Revoke access; verify further reads fail. Close/reopen window and verify stale handles cannot capture another target.
6. Test negative coordinates/mixed DPI and window resize. Record the actual Windows build, monitor layout and graphics driver in test evidence; do not commit private screen content.
7. Only after the still path is sound, test rectangle/monitor modes, video encoding and trace recording.

## 7. Security & data handling

- Never record passwords, clipboard content, IME composition, freeform keystrokes or unrelated windows by default.
- Do not put private recordings, logs containing window titles, session tokens, local file paths or user data in commits, CI artifacts or issue attachments.
- Capturing a monitor or selected rectangle may include third-party windows and notifications. Show scope and secure separate permissions for capture and AI disclosure.
- Use bounded buffers, cancellation, expiration and secure cleanup policies; see [Privacy](SECURITY-PRIVACY.md).

## 8. PR workflow

Create a short-lived feature branch, update the closest tests/docs, submit a focused PR, and note **source complete vs tested on Windows vs released** separately. Read [PROGRESS.md](../PROGRESS.md) for gates. No new backend should silently enable persistent surveillance or arbitrary file access.
