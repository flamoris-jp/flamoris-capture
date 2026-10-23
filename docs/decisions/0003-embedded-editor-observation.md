# ADR 0003: Embedded editor observation

Accepted for implementation, 2026-10-24.

Cutwork and 2D need to inspect their actual WPF window, including panels, selection
and overlays, while retaining their existing document/command MCP authority.

`Flamoris.Capture.Wpf.EmbeddedWindowCapture` owns one CaptureSession over WGC and
binds only its owner HWND when the host enables its MCP connection. It exposes only `capture.session.get`
and `capture.screenshot.get`, appended to the editor's existing MCP boundary.
There is no separate provider, input recorder, polling screenshot loop, or editor
document/history. Per the user's accepted 2026-10-24 workflow, enabling the editor's
MCP connection includes observation of that editor window. Both read-only and edit
grants can read screenshots; there is no second sharing checkbox or notice.

The host acquires `BeginConnection()` when attaching MCP and disposes its lease
on detach or connection failure. No screen consent is persisted independently.
The visible status badge reports **MCP画面共有中** while attached. A stale lease
cannot stop a newer attachment. Binding/normal detach must not invalidate the
connection being attached or trigger an automatic reconnect loop.

Window loss, OS lock/disconnect, suspend and disposal revoke the current Capture
session and cancel acquisitions. The host invalidates its MCP grant; subsequent
connections follow the existing MCP provider/reconnect settings. Final Capture
checks prevent an in-flight result from escaping after revocation. Editor document
replacement remains an editor boundary and invalidates the editor MCP grant.

The standalone Capture EXE keeps its own target selection, checkboxes and safety
notice. This simpler connection-based policy applies only to embedded editor
observation, where the target is fixed to the editor's own window.

Only two screenshot tools are offered in embedded mode. Recording, keyboard/mouse
hooks, arbitrary window selection and desktop capture remain standalone features.
Capture does not start or control editing. Modal windows with different HWNDs are
outside the owner-window image. Minimized/hidden windows fail without a fallback.

Until Capture packages are published, consumers pin this public source repository
as a git submodule and reference the Wpf project. Packaging initializes the pinned
submodule; CI checks it out recursively. No moving branch download or DLL copy is
used. All MCP Core/Wpf/bridge references must agree on 1.3.1 for image results.

Acceptance: cross-build all consumers; test consent/cancellation/image contracts;
on Windows, connect existing MCP and immediately capture each editor's visible
panels/canvas without additional sharing controls. Disconnect during a pending
request, lock/unlock, minimize, replace a document and close/reopen. Screenshots
must require an active editor grant. Synthetic WPF tests cover attachment leases,
normal detach without reconnect loops and stale-lease rejection.
