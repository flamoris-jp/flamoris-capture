using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Flamoris.Capture.Core;
using Flamoris.Capture.Windows;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;
using Xunit;

namespace Flamoris.Capture.Windows.Tests;

public sealed class VideoTests
{
    private sealed class Target : ICaptureTarget
    {
        public bool IsAlive => true;
        public event Action? Lost { add { } remove { } }
        public void Dispose() { }
    }
    private sealed class Backend(byte[] bytes, int width, int height) : ICaptureBackend
    {
        public Task<EncodedScreenshot> CaptureAsync(ICaptureTarget target, ScreenshotRequest request, CancellationToken token) =>
            Task.FromResult(new EncodedScreenshot(bytes, "image/jpeg", width, height, new(0, 0, width, height), 80,
                new(width, height, 0, 0, 96, "synthetic"), ObservationClock.Ticks, DateTimeOffset.UtcNow));
    }
    private static async Task<byte[]> Jpeg(int width, int height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        var pixels = new byte[width * height * 4];
        // Distinct quadrants expose vertical/horizontal flips and B/R channel swaps.
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var (r, g, b) = Quadrant(x >= width / 2, y >= height / 2);
            int i = (y * width + x) * 4;
            pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = 255;
        }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync(); stream.Seek(0);
        var bytes = new byte[(int)stream.Size]; await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None); return bytes;
    }
    private static (byte R, byte G, byte B) Quadrant(bool right, bool bottom) => (right, bottom) switch
    {
        (false, false) => (220, 40, 40),
        (true, false) => (40, 210, 40),
        (false, true) => (40, 40, 220),
        (true, true) => (210, 210, 40)
    };

    [Theory]
    [InlineData(65, 49)] // Pad the final row and column.
    [InlineData(96, 64)] // No padding.
    public async Task PlatformJpegContactSheetAndH264Mp4RoundTrip(int width, int height)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var jpeg = await Jpeg(width, height); using var session = new CaptureSession(new Backend(jpeg, width, height));
        session.SelectAndAllow(new Target()); session.SetSharing(true); session.Observation.StartRecording();
        while (session.Observation.Frames(false).Length < 4)
        {
            if (!session.Observation.IsRecording) throw new Exception(session.Observation.Summary(false).StopReason);
            await Task.Delay(10, timeout.Token);
        }
        session.Observation.StopRecording(); await session.Observation.Completion;
        var frame = session.Observation.ReadFrame(0);
        var sheet = await new ContactSheetRenderer().RenderAsync([new(frame.Frame, frame.Bytes), new(frame.Frame, frame.Bytes)], timeout.Token);
        Assert.Equal(2, sheet.Cells.Length); Assert.InRange(sheet.Bytes.Length, 1, ImagePolicy.MaximumBytes);
        string path = Path.Combine(Path.GetTempPath(), "capture-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            await VideoExporter.SaveMp4Async(session.Observation, path, timeout.Token);
            Assert.True(new FileInfo(path).Length > 0);
            var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
            Assert.InRange(clip.OriginalDuration.TotalMilliseconds, 200, 5000);
            var composition = new MediaComposition(); composition.Clips.Add(clip);
            int outputWidth = (width + 1) & ~1, outputHeight = (height + 1) & ~1;
            using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromMilliseconds(100), outputWidth, outputHeight, VideoFramePrecision.NearestFrame);
            Assert.True(thumbnail.Size > 0);
            var decoded = await BitmapDecoder.CreateAsync(thumbnail);
            Assert.Equal((uint)outputWidth, decoded.PixelWidth);
            Assert.Equal((uint)outputHeight, decoded.PixelHeight);
            using var bitmap = await decoded.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            var pixels = new byte[outputWidth * outputHeight * 4]; bitmap.CopyToBuffer(pixels.AsBuffer());
            foreach (bool bottom in new[] { false, true })
            foreach (bool right in new[] { false, true })
            {
                // Sample interiors, away from JPEG/H.264 chroma edges and the padding.
                int x = right ? width * 3 / 4 : width / 4;
                int y = bottom ? height * 3 / 4 : height / 4;
                int i = (y * outputWidth + x) * 4;
                var (r, g, b) = Quadrant(right, bottom);
                Assert.InRange((int)pixels[i], b - 35, b + 35);
                Assert.InRange((int)pixels[i + 1], g - 35, g + 35);
                Assert.InRange((int)pixels[i + 2], r - 35, r + 35);
            }
        }
        finally { File.Delete(path); }
    }
}
