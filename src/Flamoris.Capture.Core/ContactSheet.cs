namespace Flamoris.Capture.Core;

public sealed record SheetInput(RecordedFrame Frame, byte[] Bytes);
public sealed record SheetCell(int FrameIndex, double TimeMs, PixelRect Bounds);
public sealed record ContactSheetResult(byte[] Bytes, string MimeType, int Width, int Height, SheetCell[] Cells);
public interface IContactSheetRenderer
{
    Task<ContactSheetResult> RenderAsync(SheetInput[] frames, CancellationToken token);
}
