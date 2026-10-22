# ADR 0002: Human-selected current window, bounded recording and physical operation trace

2026-10-23. Development preview source, superseding the ID requirement and screenshot-only UI in ADR 0001. Physical Windows acceptance is separate.

## User workflow and authority

The human chooses one window locally, enables **画面共有** and/or **操作記録**, then attaches a Read only MCP connection. The toolbar contains target selection, checks, recording/save and connection/settings controls; the remaining area is a large preview. No redundant observation heading. The operation badge is yellow at the preview's upper right; recording has a separate red badge. Badges use actual workspace state, including automatic stop on loss/error/limit.

The first enable of either capability per app run shows **操作記録の確認**, with the approved warning, initially unchecked **次回からこのメッセージを表示しない**, and OK only. Closing without OK leaves the checkbox OFF. Only explicit suppression persists; target/acquisition permissions never persist. Settings restores display. The warning is not permission to target other applications.

`CaptureSession` is the sole target/consent/epoch authority; `ObservationWorkspace` is owned by that session and uses its lock. MCP is an adapter, not an acquisition controller. No MCP recording start/stop, target selector, arbitrary path, shell, input injection, video transfer or new transport is added. All six product tools are read-only.

## Screen gate and optional IDs

`capture.session.get` and `capture.screenshot.get` accept empty input objects. `sessionId` is optional compatibility input; supplied stale IDs are rejected. Internally, a request pins the current session before acquisition and checks that same session at delivery. A target/share change cannot retarget an in-flight request. Core's RuntimeId/DocumentToken and RequestGuard remain internal protections.

Screen sharing OFF blocks every new live pixel request (including local preview), cancels the recorder and in-flight capture, clears the live preview and invalidates the old MCP grant/session. Operation recording remains independently controllable. Operation-only MCP allows metadata and event queries but blocks image queries. When neither capability is ON, no observation is available. Target replacement, Stop, target destruction, lock/disconnect/suspend and exit stop both; unlocking/restarting never resumes them.

Stopped footage remains human-saveable locally. Sharing revoke clears its MCP authority; merely turning sharing back ON cannot authorize that old recording. A new recording is required for new MCP-readable frames. Operations from an old target are cleared. Output already received by a client cannot be recalled.

## Recording and time

The first implementation stores acquired JPEG frames in a unique directory under the current user's temporary `FLAMORIS-Capture` directory. Nominal sampling is 10fps; max edge 1280, same screenshot resource envelope and at most 1 MiB per frame. Limits: 15 minutes from recording start, 9,000 acquired frames, 512 MiB of frame-file bytes. The first reached limit stops acquisition and reports `resource_limit`. Metadata/events, file-system overhead, export files and codec scratch are additional; 512 MiB is not a process-wide or total-disk quota.

A single acquisition lane remains authoritative. Concurrent screenshots return busy if needed; the recorder skips busy/frame-unavailable/timeout attempts and keeps the actual gaps. It does not fabricate missing frames. Acquisition is one bounded WGC shot per sampled frame (correctness-first, not yet an optimized persistent frame pool). Output size changes stop with `geometry_changed`; source frames continue reporting their actual geometry. No fallback to desktop, no audio and no cursor pixels.

Starting a recording replaces the previous temporary frames and resets the event timeline, preserving the operation-ON checkbox. Frame WGC QPC ticks and event-receipt QPC ticks share the same monotonic origin. They are measured at different pipeline points; synchronization error is unknown, not zero. UTC is informational. Event timestamps indicate host receipt, not hardware interrupt time. MP4 starts at the first acquired frame (which can be later than recording origin); JSON includes `mp4TimelineOriginMs`. Source indices refer to acquired JPEG frames, not decoder-generated MP4 frame counts.

Normal stop leaves frames available to authorized read-only observation and the local preview. Playback selects by actual times; the slider selects by zero-based acquired-frame index. Local MP4 export runs after acquisition stops, decodes one bounded JPEG at a time and feeds BGRA8 samples to Windows MediaStreamSource/MediaTranscoder H.264. Odd dimensions are padded to even dimensions. Samples preserve acquired time intervals; the last frame gets a nominal 100ms duration. A temporary output beside the destination is atomically moved into place only after successful encoding; encoding failure does not replace the existing destination. The GUI blocks replacement of the recording during export and cancels/awaits export on shutdown. The complete recording is not loaded into memory.

Normal replacement/exit deletes temporary frames; a process crash can leave frame files in the user's temporary directory. Crash recovery/TTL sweeping are not implemented. Saved MP4/JSON files are deliberate human output and are retained. Do not claim secure erasure.

## Selective observation tools

- `capture.recording.get`: state, counters, stop reason, internal recording identity and declared frame-selection semantics.
- `capture.frame.get`: optional **either** `frameIndex` or `timeMs`; neither means latest. Time selects the last acquired frame at/before the requested time. Before-first/after-last/out-of-range requests fail. Returns one image and its frame/source metadata.
- `capture.contactSheet.get`: `count` 1–9, default 6; evenly spans current acquired frames, one JPEG up to 1200×900 / 1 MiB. Metadata identifies each image cell, exact frame index/time and pixel bounds. Single-cell requests select latest. Permission and recording identity are checked after rendering; replacement/revoke suppresses the result.
- `capture.events.get`: monotonic `afterSequence`, limit 1–500, default 100; bounded event pages and dropped-event count. No image/video or arbitrary payload.

Image selectors cannot select another window. Unknown, duplicate and explicitly null fields are rejected; optional fields must be omitted. A static Hub catalog must be upgraded as well as the app; the public prefix convention remains compatible. Live and stopped current recordings can be inspected, provided the same sharing scope is valid.

## Operation recording

The visible checkbox registers Raw Input only while ON; OFF unregisters and the workspace stops accepting events synchronously. Before reading a packet, the adapter verifies that the approved window is the foreground root window. Pointer events additionally require the cursor to be over that same root. Other windows, including dialogs/popups with a different root, are excluded. Mouse coordinates are physical pixels relative to the current DWM extended-frame bounds; pointer moves are throttled to about 30Hz. Buttons, wheel/horizontal-wheel, physical key down/up, left/right modifiers, single letters and numpad keys (including NumLock-OFF scan codes) are represented.

The implementation does not translate keystrokes into Unicode/IME text or read clipboard content. UI Automation-recognized password, Edit, TextPattern and writable ValuePattern controls suppress keyboard events; disappearance/provider errors suppress them too. Custom application controls can expose incomplete UI Automation information, so this is **not a guarantee that all typed or secret information is detected**. Physical sequences can reveal text and passwords. The explicit notice/target restriction/visible indicator/immediate stop remain required. Blender and custom canvas controls need hands-on acceptance.

Events use a 20,000-entry bounded queue with monotonic sequence numbers and explicit eviction count. A new recording clears the timeline. Event-only recording is allowed, bounded by queue capacity rather than a video-duration timer. Turning operations OFF stops future collection; retained events remain readable only while an observation capability is currently enabled for that target. All-target Stop clears events.

## Verification boundary

Pure Core/MCP tests cover empty inputs, stale IDs, checkbox gates, recording stop/selectors, events, limits and revoke during asynchronous contact rendering. Windows CI additionally encodes/decodes synthetic JPEG/MP4 and renders a contact sheet. Neither is evidence of physical WGC, custom UI Automation, real input routing, performance or final client image rendering. Record those in WINDOWS-ACCEPTANCE.md.

## Primary references

- [Microsoft screen capture to video](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture-video)
- [MediaStreamSample.CreateFromBuffer](https://learn.microsoft.com/en-us/uwp/api/windows.media.core.mediastreamsample.createfrombuffer)
- [Raw Input registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerrawinputdevices)
- [WM_INPUT](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-input)
- [UI Automation IsPassword](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.ispasswordproperty)
