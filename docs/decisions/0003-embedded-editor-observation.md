# ADR 0003: Embedded editor observation

Accepted for implementation, 2026-10-24.

Cutwork and 2D need to inspect their actual WPF window, including panels, selection
and overlays, while retaining their existing document/command MCP authority.

`Flamoris.Capture.Wpf.EmbeddedWindowCapture` owns one CaptureSession over WGC and
binds only its owner HWND after local consent. It exposes only `capture.session.get`
and `capture.screenshot.get`, appended to the editor's existing MCP boundary.
There is no separate provider, input recorder, polling screenshot loop, or editor
document/history. MCP permission alone never enables screen sharing. Both read-only
and edit grants can read a screenshot after local screen consent.

The visible sharing checkbox starts OFF each run. A common first-use notice must
be acknowledged; its suppression preference is per host application. Sharing OFF,
window loss, lock/disconnect, suspend, and disposal immediately revoke Capture's
session and cancel acquisitions. The host also invalidates its MCP grant; reconnect
after changing sharing. Final Capture checks prevent an in-flight result from
escaping after consent revocation. Editor document replacement remains an editor
boundary and must invalidate the editor MCP grant.

Only two screenshot tools are offered in embedded mode. Recording, keyboard/mouse
hooks, arbitrary window selection and desktop capture remain standalone features.
Capture does not start or control editing. Modal windows with different HWNDs are
outside the owner-window image. Minimized/hidden windows fail without a fallback.

Until Capture packages are published, consumers pin this public source repository
as a git submodule and reference the Wpf project. Packaging initializes the pinned
submodule; CI checks it out recursively. No moving branch download or DLL copy is
used. All MCP Core/Wpf/bridge references must agree on 1.3.1 for image results.

Acceptance: cross-build all consumers; test consent/cancellation/image contracts;
on Windows, enable sharing locally, reconnect existing MCP, capture each editor's
visible panels/canvas, disable during a pending request, lock/unlock, minimize,
replace a document, and close/reopen. Sharing must never resume automatically.
