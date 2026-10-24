using System.Runtime.InteropServices.WindowsRuntime;
using Flamoris.Capture.Core;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Flamoris.Capture.Windows;

// Platform H.264/MP4 encoder; no external executable, codec bundle or arbitrary MCP file API.
// Acquisition ends before export starts, keeping both capture and export memory bounded.
public static class VideoExporter
{
    public static async Task SaveMp4Async(ObservationWorkspace recording, string destination, CancellationToken token = default)
    {
        if (recording.IsRecording) throw new CaptureFault("busy");
        var frames = recording.Frames(false);
        if (frames.Length == 0) throw new CaptureFault("frame_unavailable");
        int width = frames[0].Image.OutputWidth, height = frames[0].Image.OutputHeight;
        // H.264 encoders require even dimensions. Pad only the last row/column with black.
        int outputWidth = (width + 1) & ~1, outputHeight = (height + 1) & ~1;
        var raw = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)outputWidth, (uint)outputHeight);
        // BitmapDecoder and the padded buffer are top-down. Without an explicit
        // MF_MT_DEFAULT_STRIDE, Media Foundation can interpret system-memory RGB as bottom-up.
        // Positive stride declares top-down rows; include the padded output column.
        raw.Properties[new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6")] = checked((uint)outputWidth * 4);
        raw.FrameRate.Numerator = ObservationWorkspace.Fps; raw.FrameRate.Denominator = 1;
        var source = new MediaStreamSource(new VideoStreamDescriptor(raw)) { BufferTime = TimeSpan.Zero, IsLive = false, CanSeek = false };
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Audio = null;
        profile.Video.Width = (uint)outputWidth; profile.Video.Height = (uint)outputHeight;
        profile.Video.FrameRate.Numerator = ObservationWorkspace.Fps; profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = 4_000_000;
        int index = 0;
        Exception? failure = null;
        source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += async (_, e) =>
        {
            var deferral = e.Request.GetDeferral();
            try
            {
                token.ThrowIfCancellationRequested();
                if (index >= frames.Length) { e.Request.Sample = null; return; }
                var frame = recording.ReadFrame(index, disclosure: false);
                using var input = new InMemoryRandomAccessStream();
                await input.WriteAsync(frame.Bytes.AsBuffer()); input.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(input);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                var pixels = new byte[checked(width * height * 4)]; bitmap.CopyToBuffer(pixels.AsBuffer());
                var padded = new byte[checked(outputWidth * outputHeight * 4)];
                for (int row = 0; row < height; row++) System.Buffer.BlockCopy(pixels, row * width * 4, padded, row * outputWidth * 4, width * 4);
                double first = frames[0].TimeMs;
                double next = index + 1 < frames.Length ? frames[index + 1].TimeMs : frame.Frame.TimeMs + 1000d / ObservationWorkspace.Fps;
                var sample = MediaStreamSample.CreateFromBuffer(padded.AsBuffer(), TimeSpan.FromMilliseconds(frame.Frame.TimeMs - first));
                sample.Duration = TimeSpan.FromMilliseconds(next - frame.Frame.TimeMs);
                sample.KeyFrame = true; e.Request.Sample = sample; index++;
            }
            catch (Exception error) { failure = error; e.Request.Sample = null; }
            finally { deferral.Complete(); }
        };
        string output = Path.GetFullPath(destination);
        string temporary = Path.Combine(Path.GetDirectoryName(output)!, "." + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(output)!);
            var file = await folder.CreateFileAsync(Path.GetFileName(temporary), CreationCollisionOption.FailIfExists);
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
                var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile);
                if (!prepared.CanTranscode) throw new CaptureFault("encoder_unavailable");
                await prepared.TranscodeAsync().AsTask(token);
                if (failure is not null) throw new CaptureFault("encoding_failed");
                token.ThrowIfCancellationRequested();
                if (index != frames.Length) throw new CaptureFault("encoding_failed");
            }
            File.Move(temporary, output, true);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
    }
}
