# Portable still-image processing (Capture #14)

`Flamoris.Capture.Imaging` targets .NET 10 on Windows/Linux. It accepts bounded
encoded PNG/JPEG bytes and existing `ScreenshotRequest`; it has no screen,
window, filesystem, MCP, GPU or permission authority. It reuses Core's
`ImagePolicy.Region`, `Size`, `Attempts` and nearest-neighbour `CropScale`.
The latter adds an optional `preserveAlpha=false` argument: existing callers keep
opaque output and existing Windows acquisition/encoding is unchanged.

`Process` identifies headers before decoding and rejects >32 MiB encoded input,
>8,294,400 source pixels, edges >8192, animation, other formats and non-top-left
EXIF orientation. Decode must complete successfully. ROI is in original pixels;
maxEdge is 1..1920 and never enlarges. Existing bounded attempt policy applies:
JPEG quality 80/65/50 and up to three 3/4 reductions, or PNG at each size.
`requireExactSize` prevents reductions but still allows JPEG quality selection.
Failure to fit 1 MiB returns `response_too_large`; it never silently changes format.

Color is converted to sRGB, 8-bit BGRA. PNG preserves straight alpha, including
partially transparent pixels. JPEG composites onto white explicitly. Output is a
new encoding without source EXIF or ancillary metadata; no HDR/wide-gamut promise.
Metadata records source size, ROI, output size, MIME, actual byte count, SHA-256,
quality, alpha and color handling. Cancellation is checked during stream reads,
resampling and before/after bounded native decode/encode. Native codec calls are
not interruptible mid-call; callers needing a hard process deadline use the CLI.

## CLI for Python or other callers

Build/publish `src/Flamoris.Capture.Imaging.Cli` (self-contained linux-x64 or
win-x64). No runtime install is needed for self-contained output. Its local-only
stdin JSON is <=16 KiB, exactly one request terminated by EOF:

```json
{"inputPath":"input.png","outputPath":"new-preview.png","options":{"format":"png","maxEdge":1280}}
```

`inputPath` and `outputPath` are trusted local caller arguments, never an MCP
schema. Output uses CreateNew and refuses overwrite. Exit 0 emits
`{"metadata":{...}}`; exit 1 emits `{"error":{"code":"..."}}` without private
paths/exception text. It does not return base64 or implement MCP. The caller owns
cleanup of any partial output on cancellation/I/O failure. Deadline: 30 seconds.

The direct .NET CG adapter consumes the library without a CLI round-trip and uses
published `Flamoris.Mcp.Core` 1.4.0 `HostImageTool` / `McpImage`. Capture owns image
processing, Mcp.Core owns bounded image transport/revocation, CG owns its manifest
and immutable output references. No transport implementation is copied here.

## Dependencies and distribution

SkiaSharp 3.119.4 and SkiaSharp.NativeAssets.Linux.NoDependencies 3.119.4 are pinned.
SkiaSharp is MIT; bundled Skia is BSD-style with third-party notices. See
[upstream licensing](https://github.com/mono/SkiaSharp/tree/v3.119.4) and retain
package/native third-party notices when redistributing a published directory.
The Linux no-fontconfig native asset avoids desktop/font dependencies; it still
uses the supported platform C runtime (Ubuntu 24.04 x64 acceptance). Windows uses
SkiaSharp's native Windows assets. No font/model/media dependency is introduced.

Run `dotnet test tests/Flamoris.Capture.Imaging.Tests -c Release` on both OSes.
The dedicated CI matrix checks real decoded pixels, alpha/matte, ROI, exact size,
compression cap, metadata hashes, malformed input, bounds and cancellation.
The existing Windows build/tests remain required regression checks. Physical
Windows screen-capture acceptance is distinct from portable synthetic tests.
