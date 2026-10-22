using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Flamoris.Capture.Core;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Flamoris.Capture.Windows;

public sealed class WindowCaptureBackend : ICaptureBackend
{
    public async Task<EncodedScreenshot> CaptureAsync(ICaptureTarget selected, ScreenshotRequest request, CancellationToken token)
    {
        if (selected is not WindowTarget target) throw new CaptureFault("unsupported_capability");
        try
        {
            token.ThrowIfCancellationRequested(); target.DemandAvailable();
            var size = target.CaptureSize; ImagePolicy.ValidateSource(size.Width, size.Height);
            var before = target.Geometry(size.Width, size.Height);
            ImagePolicy.Region(size.Width, size.Height, request.Roi);
            using var device = Native.CreateDevice();
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, size);
            using var session = pool.CreateCaptureSession(target.Item);
            session.IsCursorCaptureEnabled = false;
            var received = new TaskCompletionSource<(SoftwareBitmap Bitmap, long Ticks, DateTimeOffset Utc)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbackGate = new object(); bool accepting = true; Task? processing = null;
            async Task Copy(Direct3D11CaptureFrame frame)
            {
                using (frame)
                {
                    try
                    {
                        if (frame.ContentSize.Width != size.Width || frame.ContentSize.Height != size.Height)
                        {
                            // Item.Size may describe its initial size. Learn the current size from WGC,
                            // reject this potentially clipped surface, and use it on the next one-shot.
                            target.UpdateCaptureSize(frame.ContentSize);
                            throw new CaptureFault("frame_unavailable");
                        }
                        token.ThrowIfCancellationRequested();
                        var utc = DateTimeOffset.UtcNow;
                        var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                        if (!received.TrySetResult((bitmap, frame.SystemRelativeTime.Ticks, utc))) bitmap.Dispose();
                    }
                    catch (Exception e) { received.TrySetException(e); }
                }
            }
            void Arrived(Direct3D11CaptureFramePool sender, object args)
            {
                lock (callbackGate)
                {
                    if (!accepting || processing is not null) return;
                    try
                    {
                        var frame = sender.TryGetNextFrame();
                        if (frame is not null) processing = Copy(frame);
                    }
                    catch (Exception e) { received.TrySetException(e); }
                }
            }
            pool.FrameArrived += Arrived;
            SoftwareBitmap? owned = null;
            try
            {
                session.StartCapture();
                var captured = await received.Task.WaitAsync(token);
                owned = captured.Bitmap;
                token.ThrowIfCancellationRequested(); target.DemandAvailable();
                var now = target.Geometry(size.Width, size.Height);
                if (before != now)
                    throw new CaptureFault("frame_unavailable");
                var roi = ImagePolicy.Region(size.Width, size.Height, request.Roi);
                var pixels = new byte[checked(size.Width * size.Height * 4)]; owned.CopyToBuffer(pixels.AsBuffer());
                foreach (var attempt in ImagePolicy.Attempts(roi, request))
                {
                    token.ThrowIfCancellationRequested();
                    var output = ImagePolicy.CropScale(pixels, size.Width, size.Height, roi, attempt.Width, attempt.Height, token);
                    var bytes = await Encode(output, attempt.Width, attempt.Height, request.Format, attempt.Quality, token);
                    if (bytes is not null)
                        return new(bytes, request.Format == "png" ? "image/png" : "image/jpeg", attempt.Width, attempt.Height,
                            roi, attempt.Quality, before, captured.Ticks, captured.Utc);
                }
                throw new CaptureFault("resource_limit");
            }
            finally
            {
                Task? pending;
                lock (callbackGate) { accepting = false; pool.FrameArrived -= Arrived; pending = processing; }
                // Do not dispose the surface/pool while a native GPU copy still owns it.
                if (pending is not null) await pending;
                if (owned is not null) owned.Dispose();
                else if (received.Task.IsCompletedSuccessfully) received.Task.Result.Bitmap.Dispose();
                else if (received.Task.IsFaulted) _ = received.Task.Exception;
            }
        }
        catch (CaptureFault) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw new CaptureFault("forbidden"); }
        catch (COMException) { throw new CaptureFault("frame_unavailable"); }
    }
    private static async Task<byte[]?> Encode(byte[] pixels, int width, int height, string format, int? quality, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        var options = new BitmapPropertySet();
        if (quality is { } q) options.Add("ImageQuality", new BitmapTypedValue(q / 100f, PropertyType.Single));
        var encoder = await BitmapEncoder.CreateAsync(format == "png" ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId, stream, options);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        token.ThrowIfCancellationRequested(); await encoder.FlushAsync(); token.ThrowIfCancellationRequested();
        if (stream.Size is 0 or > ImagePolicy.MaximumBytes) return null;
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var result = new byte[(int)stream.Size]; reader.ReadBytes(result); return result;
    }
}
