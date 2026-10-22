using System.Runtime.InteropServices.WindowsRuntime;
using Flamoris.Capture.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Flamoris.Capture.Windows;

public sealed class ContactSheetRenderer : IContactSheetRenderer
{
    public async Task<ContactSheetResult> RenderAsync(SheetInput[] frames, CancellationToken token)
    {
        if (frames.Length is < 1 or > 9) throw new CaptureFault("invalid_request");
        int columns = Math.Min(3, frames.Length), rows = (frames.Length + columns - 1) / columns;
        const int cellWidth = 400, cellHeight = 300;
        int width = columns * cellWidth, height = rows * cellHeight;
        var canvas = new byte[width * height * 4];
        var cells = new List<SheetCell>();
        for (int i = 0; i < frames.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(frames[i].Bytes.AsBuffer()); stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            int sourceWidth = bitmap.PixelWidth, sourceHeight = bitmap.PixelHeight;
            ImagePolicy.ValidateSource(sourceWidth, sourceHeight);
            var source = new byte[sourceWidth * sourceHeight * 4]; bitmap.CopyToBuffer(source.AsBuffer());
            double scale = Math.Min(1, Math.Min((double)cellWidth / sourceWidth, (double)cellHeight / sourceHeight));
            int w = Math.Max(1, (int)(sourceWidth * scale)), h = Math.Max(1, (int)(sourceHeight * scale));
            var pixels = ImagePolicy.CropScale(source, sourceWidth, sourceHeight, new(0, 0, sourceWidth, sourceHeight), w, h, token);
            int x = i % columns * cellWidth + (cellWidth - w) / 2, y = i / columns * cellHeight + (cellHeight - h) / 2;
            for (int row = 0; row < h; row++) System.Buffer.BlockCopy(pixels, row * w * 4, canvas, ((y + row) * width + x) * 4, w * 4);
            cells.Add(new(frames[i].Frame.Index, frames[i].Frame.TimeMs, new(x, y, w, h)));
        }
        foreach (int quality in new[] { 80, 65, 50 })
        {
            var encoded = await WindowCaptureBackend.Encode(canvas, width, height, "jpeg", quality, token);
            if (encoded is not null) return new(encoded, "image/jpeg", width, height, cells.ToArray());
        }
        throw new CaptureFault("resource_limit");
    }
}
