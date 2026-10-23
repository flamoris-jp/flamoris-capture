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
    private sealed class Backend(byte[] bytes) : ICaptureBackend
    {
        public Task<EncodedScreenshot> CaptureAsync(ICaptureTarget target, ScreenshotRequest request, CancellationToken token) =>
            Task.FromResult(new EncodedScreenshot(bytes, "image/jpeg", 65, 49, new(0, 0, 65, 49), 80,
                new(65, 49, 0, 0, 96, "synthetic"), ObservationClock.Ticks, DateTimeOffset.UtcNow));
    }
    private static async Task<byte[]> Jpeg()
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        var pixels = new byte[65 * 49 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 100; pixels[i + 2] = 220; pixels[i + 3] = 255; }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 65, 49, 96, 96, pixels);
        await encoder.FlushAsync(); stream.Seek(0);
        var bytes = new byte[(int)stream.Size]; await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None); return bytes;
    }
    [Fact]
    public async Task PlatformJpegContactSheetAndH264Mp4RoundTrip()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var jpeg = await Jpeg(); using var session = new CaptureSession(new Backend(jpeg));
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
            using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromMilliseconds(100), 0, 50, VideoFramePrecision.NearestFrame);
            Assert.True(thumbnail.Size > 0);
            var decoded = await BitmapDecoder.CreateAsync(thumbnail);
            Assert.Equal((uint)50, decoded.PixelHeight);
        }
        finally { File.Delete(path); }
    }
}
