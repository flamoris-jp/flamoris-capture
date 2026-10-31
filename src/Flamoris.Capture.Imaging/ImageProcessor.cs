using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Flamoris.Capture.Core;
using SkiaSharp;

namespace Flamoris.Capture.Imaging;

public sealed record ProcessedImage(byte[] Bytes, ImageMetadata Metadata);
public sealed record ImageMetadata(string SchemaVersion, int SourceWidth, int SourceHeight,
    PixelRect Roi, int OutputWidth, int OutputHeight, string MimeType, int ByteLength,
    string Sha256, int? Quality, string Alpha, string ColorSpace);

/// <summary>Bounded still-image processing. No acquisition, transport, paths or authority.</summary>
public static class ImageProcessor
{
    public const int MaximumInputBytes = 32 * 1024 * 1024;

    public static ProcessedImage Process(byte[] encoded, ScreenshotRequest request,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        request.Validate();
        if (encoded.Length is < 1 or > MaximumInputBytes) throw new CaptureFault("resource_limit");
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new CaptureFault("invalid_image");
        // Inspect headers BEFORE allocating the decoded bitmap; only still PNG/JPEG is supported.
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg)
            || codec.EncodedOrigin != SKEncodedOrigin.TopLeft || codec.FrameCount > 1)
            throw new CaptureFault("unsupported_image");
        int sw = codec.Info.Width, sh = codec.Info.Height;
        var roi = ImagePolicy.Region(sw, sh, request.Roi);
        using var color = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(sw, sh, SKColorType.Bgra8888, SKAlphaType.Unpremul, color);
        using var source = new SKBitmap(info);
        if (source.GetPixels() == IntPtr.Zero) throw new CaptureFault("resource_limit");
        token.ThrowIfCancellationRequested();
        if (codec.GetPixels(info, source.GetPixels()) != SKCodecResult.Success)
            throw new CaptureFault("invalid_image");
        token.ThrowIfCancellationRequested();
        byte[] pixels = new byte[checked(sw * sh * 4)];
        Marshal.Copy(source.GetPixels(), pixels, 0, pixels.Length);
        foreach (var (w, h, quality) in ImagePolicy.Attempts(roi, request))
        {
            token.ThrowIfCancellationRequested();
            byte[] cropped = ImagePolicy.CropScale(pixels, sw, sh, roi, w, h, token, preserveAlpha: true);
            if (request.Format == "jpeg")
            {
                // JPEG has no alpha: composite explicitly onto white in output sRGB.
                for (int i = 0; i < cropped.Length; i += 4)
                {
                    int a = cropped[i + 3];
                    for (int c = 0; c < 3; c++) cropped[i + c] = (byte)((cropped[i + c] * a + 255 * (255 - a) + 127) / 255);
                    cropped[i + 3] = 255;
                }
            }
            using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul, color));
            Marshal.Copy(cropped, 0, bitmap.GetPixels(), cropped.Length);
            using var image = SKImage.FromBitmap(bitmap);
            using var output = image.Encode(request.Format == "png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, quality ?? 100)
                ?? throw new CaptureFault("encoding_failed");
            token.ThrowIfCancellationRequested();
            if (output.Size > ImagePolicy.MaximumBytes) continue;
            byte[] bytes = output.ToArray();
            string mime = request.Format == "png" ? "image/png" : "image/jpeg";
            return new(bytes, new("1", sw, sh, roi, w, h, mime, bytes.Length,
                Convert.ToHexStringLower(SHA256.HashData(bytes)), quality,
                request.Format == "png" ? "preserved" : "white_matte", "srgb"));
        }
        throw new CaptureFault("response_too_large");
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token = default)
    {
        // Probe one extra byte; never call unbounded ReadAllBytes on untrusted input.
        using var output = new MemoryStream();
        byte[] buffer = new byte[65536];
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaximumInputBytes + 1 - (int)output.Length)), token);
            if (read == 0) return output.ToArray();
            output.Write(buffer, 0, read);
            if (output.Length > MaximumInputBytes) throw new CaptureFault("resource_limit");
        }
    }
}
