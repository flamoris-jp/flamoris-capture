# Privacy & Security / プライバシーと安全設計

**Status:** proposed mandatory requirements, not implemented. Updated 2026-10-22.

Screen capture is sensitive: a harmless-looking screenshot can include names, chat, notifications, passwords, documents and other applications. Video and interaction traces increase the sensitivity. FLAMORIS Capture should **fail closed** and make capture and disclosure visible to the user.

## Consent model

**Separate capabilities:**

1. **Local capture consent**: the user selects `window`, `region`, or `monitor`, reviews its scope and explicitly begins a bounded capture session.
2. **External/MCP disclosure consent**: user separately enables a grant permitting an authorized client to receive the captured pixels/assets. Local saving/preview should not silently permit remote retrieval.
3. **Optional interaction-event consent**: user explicitly enables event recording; capturing screen pixels alone does not authorize a keyboard or mouse event stream.

All OFF by default, including at restart. A visible status indicator (and recording-specific indicator) must remain apparent; one immediate user-facing Stop/Revoke action must terminate or invalidate active work. A local interactive user should always be able to deny/revoke without the AI's permission.

The local user must choose target using a host-controlled UI. External MCP must not silently change to another window, select a new monitor or expand the crop to the entire desktop.

## Least privilege

- Bind target to more than a reusable raw window handle; verify process/window lifetime and capture-session token on request and result delivery.
- Prefer same-user local-only connection and an explicit short-lived capability. Reuse FLAMORIS MCP Core instead of rolling a new authentication scheme.
- Revalidate during long operations, not just at start; suppress output on revoke, target swap, timeout or session expiration.
- Cap resolution, ROI, recording duration, FPS, total bytes, temporary storage, simultaneous sessions and pending requests. Apply limits before allocating buffers or starting encoders.
- No arbitrary path/URL/network/file-browser/shell/input-injection MCP tools in the core capture surface.
- Fail with explicit `denied`, `unsupported`, `target_lost`, `busy`, `stale_session` or `resource_limit` categories (names to be finalized) rather than falling back to broader capture.

## Window / rectangle / monitor difference

`window` captures one user-selected application target (actual ability to include menus/dialogs is backend-dependent). `region` can capture any pixels visibly intersecting the rectangle, including other programs. `monitor` may include *everything* on one display and must present a stronger scope warning.

The capture selection boundary is not equivalent to the document boundary. A screenshot of a browser can include private tabs or notifications not represented in an app's internal model. Explicitly state the potential difference in the GUI.

Do not try to bypass Windows protected surfaces, UAC secure desktop, DRM, session lock, or operating-system privacy protections. Minimized/occluded/hidden results must be represented accurately, not fabricated.

## Interaction tracing: data minimization

Do **not** create a keylogger. Default event schema should retain target-local pointer actions, wheel deltas, approved *semantic shortcuts* (e.g. Undo) and geometry/focus changes only when necessary. Never record password entry, IME text, clipboard contents, raw typed characters, screen text from hidden/other windows or all-system keyboard hooks by default.

Treat each event record as potentially sensitive. Hide/mask events from protected fields and allow an explicit stop of event recording without terminating an already authorized screenshot session. A future policy for selectively capturing typed text would require separate design/review and is **not** part of this plan.

## Assets, retention, logs

The [basic design](BASIC-DESIGN.md) distinguishes normal recording stop from revocation. Normal stop finalizes an asset for already authorized observation until TTL; it does not silently widen sharing. Revoke invalidates grants/IDs and in-flight disclosure, including cached frames, contact sheets, diffs, event pages and metadata. A separate local capture grant may remain active when only MCP sharing is revoked. Target loss/replacement and OS lock invalidate the observation workspace; recovered local recordings require human review and a new grant before sharing.

All derived images and event metadata inherit the parent recording's scope and maximum lifetime. Validate authorization before decoding and again immediately before delivery. Cached pixels must not bypass revoke/expiry. Returned pixels already received by a client cannot be recalled.

- Release acquisition buffers on normal stop and preserve only the bounded finalized recording until TTL. Clean up cancelled/failed recordings and expired temporary files; revoke clears MCP derivatives/handles while deliberately retained local recordings stay under the human's control.
- Persist only when the human deliberately selects an output destination; no automatic cloud upload or hidden long-term store.
- Initial MCP observation returns selected images, not video chunks. If future raw-video export is added, verify length, offset and SHA-256 under a separately reviewed bounded transfer contract.
- Diagnostics may include counters, error codes and coarse performance timings, but **not** raw images, window titles, typed content, secret paths, tokens or video.
- CI and Issues must not publish private user captures. Use synthetic windows/test patterns where possible.
- Encrypt/sign only when appropriate and documented; do not market ephemeral local capture as securely encrypted end-to-end unless actually verified.

## Review gates

Before calling any MVP feature ready, demonstrate refusal after revoke, target reuse and replacement rejection, cross-user/unauthorized-client refusal, safe stop under capture load, OS-protected content behavior, and no data leaking to logs or unintended output channels.

Read [Test plan](TEST-PLAN.md) for acceptance. This document is a design constraint, **not evidence that a secure implementation already exists**.

