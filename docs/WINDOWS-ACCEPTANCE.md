# One-window screenshot: physical Windows acceptance

**Not yet executed.** CI contract/transport results do not certify these cases. Record sanitized evidence here or link a reviewed follow-up; never attach personal desktop captures/tokens to public CI or Issues.

## Environment to record

Date, exact app commit, Windows build, SDK/runtime, GPU/driver, monitor resolutions/origins and 100/150/200% DPI. Use a synthetic test window and at least one ordinary non-FLAMORIS application. Capture APIs start at build 19041; no OS/GPU matrix has yet been accepted.

## Required cases

| Check | Expected | Evidence |
|---|---|---|
| Cold start/restart | Target, capture, sharing OFF; no acquisition or endpoint | Pending |
| Select/confirm one window | Current item name confirmed, no other-window pixels; local preview works without MCP | Pending |
| Two windows with same title, close/recreate rapidly | Bound WGC item never changes; stale session/grant rejected | Pending |
| Local preview → share → official MCP client | Exactly one decodable/displayed PNG/JPEG <=1 MiB; hash and geometry match | Pending |
| Sharing revoke during capture/encoding/final disclosure | No result from old grant; local consent remains, new sessionId | Pending |
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
