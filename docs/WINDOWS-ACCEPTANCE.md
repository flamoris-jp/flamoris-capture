# Physical Windows acceptance

**Basic workflow confirmed on 2026-10-24; broader acceptance remains pending.** The user operated Capture on Windows and the AI received real Hub images and operation events from a selected Kachinco window. This is a scoped workflow acceptance, not a completed OS/GPU/lifecycle matrix. CI results remain separate. Never attach personal desktop captures/tokens to public CI or Issues.

## 2026-10-24 — Observed results

| Workflow | Observed evidence | Result / boundary |
|---|---|---|
| Current-window MCP screenshot | `input: {}` returned a displayed JPEG matching the selected Kachinco window | Confirmed without session-ID lookup; full isolation/revoke tests not run |
| Human start/stop and retained recording | Stopped recording: 87 frames, 8,665.9187 ms | Confirmed; nominal 10fps is not a measured sustained-FPS guarantee |
| Contact sheet and time selection | Six-cell sheet displayed; request at 5,000 ms returned actual 4,949.2339 ms frame | Confirmed through real Hub/client; video was not transferred |
| Mouse events and corresponding frames | 172 events, droppedEvents=0; left down 12,530.6356 ms, up 14,535.581 ms; before/after playhead positions changed | Drag observation confirmed; synchronization drift not measured |
| Physical keyboard events | 130-event sample, droppedEvents=0; Left ×3 and Space ×3, each down/up | Confirmed for focused Kachinco timeline; Blender/numpad/modifiers still pending |
| Text-entry trial | Earlier trace had no key events; source suppresses recognized editable/text/password controls | Consistent with suppression, but focused control was not instrumented; no password-detection acceptance |
| Local MP4/JSON save, local playback/scrub | Not exercised in this session | Pending; synthetic CI encode/decode evidence is separate |

The basic user-requested observation test is complete. Exact app commit, Windows build, GPU/driver and full display configuration were not collected. Counts and timestamps above are sanitized session evidence; raw images, input text, identifiers and private host details are intentionally omitted.

## 2026-10-24 — Embedded catalog follow-up

The existing Hub catalogs were refreshed to Cutwork 11 / 2D 181 tools, and the external client discovered both embedded Capture tools. Follow-up screenshot attempts returned `frame_unavailable` for connected Cutwork and upstream-offline for 2D; no image was displayed and the unavailable-frame cause was not established. Catalog/discovery is accepted, while physical image/revoke acceptance stays pending. See [progress](../PROGRESS.md) and the focused [stable-scope review #6](https://github.com/flamoris-jp/flamoris-capture/issues/6).

## Environment to record

Date, exact app commit, Windows build, SDK/runtime, GPU/driver, monitor resolutions/origins and 100/150/200% DPI. Use a synthetic test window and at least one ordinary non-FLAMORIS application. Capture APIs start at build 19041; no OS/GPU matrix has yet been accepted.

## Required cases

| Check | Expected | Evidence |
|---|---|---|
| Cold start/restart | Target, capture, sharing OFF; no acquisition or endpoint | Pending |
| Select/confirm one window | Current item name confirmed, no other-window pixels; local preview works without MCP | Pending |
| Two windows with same title, close/recreate rapidly | Bound WGC item never changes; stale session/grant rejected | Pending |
| Toggle screen sharing ON/OFF | OFF cancels live acquisition/recording, clears preview and revokes old image access; operation recording remains independent | Pending physical OFF/revoke case |
| Connect with Edit / Read only | Edit shows the product's Read-only guidance without opening Hub transport; Read only reaches a real Hub registration | Pending |
| Local preview → share → real MCP client | One decodable/displayed PNG/JPEG <=1 MiB with geometry/hash metadata | Basic Hub image display confirmed 2026-10-24; independent byte/hash/ROI validation pending |
| Sharing revoke during capture/encoding/final disclosure | No result from old grant; old footage cannot be reauthorized merely by enabling sharing again | Pending |
| Target change, Stop, close, shutdown | Revoke first; no acquisition continues, no stale preview/grant | Pending |
| Lock/unlock, logoff, local/remote disconnect, suspend/resume | Old session cancelled, preview cleared; no automatic resumption | Pending |
| Resize/move/DPI transition during shot | frame_unavailable or correctly matched stable geometry; no wrong crop | Pending |
| Negative monitor origin, DPI 100/150/200% | Correct physical ROI, dimensions and source origin/DPI | Pending |
| Minimized/hidden/occluded/protected/secure windows | Minimized/hidden rejected; other unsupported cases truthful, no fallback; black image is not classified as proof of protection | Pending |
| PNG/JPEG noisy synthetic scene | Encoding attempts, quality/dimensions and exact-size rejection match limits | Pending |
| Source/output/ROI limits, overflow and unknown fields | Rejected; source budget before pool allocation; no truncated bytes | Pending |
| 100 sequential shots + cancel/revoke under load | Resources released, one admission slot, UI Stop remains responsive; no hidden queue | Pending |

Measure first-frame latency, ordinary and cancel/revoke completion latency, CPU working set/GPU memory peak at the source cap, post-shot memory plateau and errors. Native GPU copy/encoding is not forcibly abortable; record any driver stalls. Inspect diagnostic output for titles, tokens, paths and pixels (none should be present).

Keep **source complete**, **automated build/contracts/transport**, **physical display/MCP rendering acceptance**, and **release** as separate statuses in [PROGRESS](../PROGRESS.md). Region/monitor/video/event support must not be inferred from this gate.

## 0.2.0 acceptance additions — remaining full-case checks

- Select target with both checks OFF: no preview/capture/recording/input registration. Enable first capability: notice, no acquisition before OK; close via X leaves OFF. Verify skip defaults OFF, per-run first enable, persisted suppression and settings restore.
- Large preview/toolbar layout at 100/150/200% DPI and narrow window; yellow operation badge/red recording badge remain readable and reflect real worker state.
- MCP `input: {}` returns the selected window without ID discovery. Explicit old ID still fails. New Hub catalog has seven tools; old catalog is updated without replacing connector credentials.
- Record moving synthetic window for 5 seconds; stop, scrub/play, save MP4/JSON and decode MP4. Compare actual acquired timestamps/counts and event timeline. Verify silent output, cursor exclusion, odd dimensions, cancellation/failure preserving existing destination.
- Screen OFF during native acquisition/recording: no later image disclosure, no new frame files, live preview clears. Operation checkbox may remain ON independently. Operations OFF: no new events. All-target Stop/target loss/OS lock stops both and never resumes on unlock.
- Enable operations only, connect Read only: session/events work; screenshot/frame/contact sheet fail. Disable both: no observation available.
- In Blender: numpad with NumLock ON/OFF; G, X, numeric command, Enter; key up/down, left/right modifiers, mouse drag, wheel. Verify appropriate target-relative coordinates and event/frame timing; do not enter private text.
- Focus another app: no keyboard/mouse packets retained. Pointer over another/overlapping window: no mouse event. Test separate target dialogs/popups and document exclusion. Test known password/editable controls and a custom canvas; UI Automation exclusion is best effort, not universal protection.
- Source-size change, minimized target, capture busy/timeout and disk/resource limits: bounded behavior and explicit stop reason; no desktop fallback. Measure actual FPS/CPU/GPU/disk before promising realtime performance.
- New recording replaces prior temporary asset and resets event timeline; in-flight old contact sheet cannot disclose. Normal exit removes temporary frame directories; saved MP4/JSON remain. Crash leftovers are documented.
