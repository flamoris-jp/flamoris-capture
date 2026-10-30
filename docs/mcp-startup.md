# MCP startup and independent content consent

Capture can connect before selecting a target, with screen sharing and operation
recording OFF. `mcp.context` reports application identity and `hasDocument: false`;
Capture has no editor document. `capture.session.get` returns safe status without
content consent. The screenshot/recording/event tools do not require a document;
they enforce their own target/session and content permissions.

New connection preferences enable local read-only Manual auto-connect. Hub/tunnel
reuse the configured method/credentials when **起動時に自動接続** is ON. Saved OFF
remains OFF. Connection/reconnection never selects a target or enables acquisition.
Explicit MCP Stop and app shutdown revoke the grant.

Screen sharing and operation recording have separate controls. Turning either OFF
blocks its new, pending and retained content while preserving MCP. Re-enabling does
not revive old recording authority. Target replacement/loss, Stop-all, lock and
suspend stop acquisition; no content resumes automatically.

For embedded Cutwork/2D, `BeginConnection()` only enables the visible sharing
checkbox; the user must enable it and confirm the notice. Document replacement
stops sharing without disconnecting MCP. See [ADR 0004](decisions/0004-application-mcp-lifetime.md)
and [Setup](SETUP.md) for the current integration API.

Acceptance: launch without a target and both controls OFF; connect/reconnect and
verify context/status/discovery, content denial, independent screen/event consent,
withdrawal during capture and stored-content reads, target replacement/stale IDs,
explicit MCP Stop and shutdown. The user reported all updated desktop apps built
and launched on 2026-10-30; full physical WGC/input/revoke/DPI cases remain tracked
in [Windows acceptance](WINDOWS-ACCEPTANCE.md). Synthetic CI is separate evidence.

Requires published Core/Wpf/bridge 1.4.0. Cutwork/2D pin the coordinated Capture
submodule; no local Core checkout or custom NuGet feed is required.
