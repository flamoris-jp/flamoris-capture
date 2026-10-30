# ADR 0004: Application MCP lifetime and independent content consent

Accepted and implemented, 2026-10-30. Supersedes the connection/consent and startup
lifetime clauses in ADRs 0001–0003; their image, recording and platform boundaries
remain applicable. Tracking: [Commons #8](https://github.com/flamoris-jp/flamoris-commons/issues/8).

MCP Core/Wpf 1.4.0 supports an opt-in application-lifetime grant. Capture connects
before target selection with screen sharing and operation recording OFF. Safe
`mcp.context` / `capture.session.get` and discovery remain available. Content tools
independently enforce Capture's target/session and screen/operation consent epochs,
including during work and final disclosure. Withdrawing consent blocks new,
pending and retained content without terminating the transport. Re-enabling consent
does not revive old footage or IDs. Explicit MCP Stop and shutdown revoke transport.

New desktop preferences auto-connect locally in read-only Manual mode; saved OFF
remains an opt-out. Hub/tunnel reuse configured provider settings and credentials.
Connecting/reconnecting never selects a capture target or enables acquisition.

Embedded `EmbeddedWindowCapture(owner)` exposes a separate visible sharing checkbox
and confirmation notice. `BeginConnection()` only enables that control. Detach,
document replacement, target loss, lock and suspend stop sharing. Ordinary document
changes and content withdrawal preserve the editor connection; the host revokes it
on runtime loss or shutdown. Stale connection leases cannot stop newer attachments.
Only owner-window screenshot/status tools are embedded; no recording or input hooks
are added. Screenshots remain subject to the existing image/platform limits.

Cutwork/2D pin Capture source as a submodule and use matching Core/Wpf/bridge 1.4.0.
Automated tests cover startup/status, content epochs, final disclosure and lease
isolation. The user reported rebuilding and launching all updated desktop apps on
2026-10-30. This is build/startup evidence; physical WGC, input, DPI and full
withdrawal/platform checks remain separately recorded in [Windows acceptance](../WINDOWS-ACCEPTANCE.md).
