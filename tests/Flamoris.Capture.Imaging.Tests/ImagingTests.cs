using System.Security.Cryptography;
using Flamoris.Capture.Core;
using Flamoris.Capture.Imaging;
using SkiaSharp;
using Xunit;

public class ImagingTests
{
    public static byte[] Fixture(int width = 64, int height = 32)
    {
        using var b = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        b.Erase(new SKColor(255, 0, 0, 128));
        using var data = b.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    [Theory][InlineData("png")][InlineData("jpeg")]
    public void RealPixelsAndMetadata(string format)
    {
        var result = ImageProcessor.Process(Fixture(), new() { Format = format, MaxEdge = 16, Roi = new(8, 4, 32, 16) });
        var m = result.Metadata;
        Assert.Equal((64, 32, 16, 8), (m.SourceWidth, m.SourceHeight, m.OutputWidth, m.OutputHeight));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(result.Bytes)), m.Sha256);
        Assert.Equal(result.Bytes.Length, m.ByteLength);
        using var decoded = SKBitmap.Decode(result.Bytes);
        Assert.Equal((16, 8), (decoded.Width, decoded.Height));
        var color = decoded.GetPixel(0, 0);
        Assert.InRange(color.Red, (byte)250, (byte)255);
        if (format == "png") { Assert.Equal((byte)128, color.Alpha); Assert.Equal((byte)0, color.Green); }
        else { Assert.Equal((byte)255, color.Alpha); Assert.InRange(color.Green, (byte)120, (byte)135); }
    }
    [Fact] public void CropRetainsOnlyRequestedPixels()
    {
        using var b = new SKBitmap(4, 2); b.Erase(SKColors.Blue); b.SetPixel(2, 1, SKColors.Lime);
        using var d = b.Encode(SKEncodedImageFormat.Png, 100);
        var r = ImageProcessor.Process(d.ToArray(), new() { Format = "png", Roi = new(2, 1, 1, 1) });
        using var decoded = SKBitmap.Decode(r.Bytes); Assert.Equal(SKColors.Lime, decoded.GetPixel(0, 0));
    }
    [Fact] public void CorruptionBoundsAndCancellation()
    {
        Assert.Throws<CaptureFault>(() => ImageProcessor.Process([1, 2, 3], new()));
        Assert.Throws<CaptureFault>(() => ImageProcessor.Process(new byte[ImageProcessor.MaximumInputBytes + 1], new()));
        Assert.Throws<CaptureFault>(() => ImageProcessor.Process(Fixture(), new() { Roi = new(int.MaxValue, 0, 2, 2) }));
        Assert.Throws<CaptureFault>(() => ImageProcessor.Process(Fixture(), new() { MaxEdge = 1921 }));
        Assert.Throws<CaptureFault>(() => ImagePolicy.Region(8192, 8192, null));
        Assert.Equal("resource_limit", Assert.Throws<CaptureFault>(() => ImageProcessor.Process(Fixture(8193, 1), new())).Code);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => ImageProcessor.Process(Fixture(), new(), stop.Token));
    }
    [Fact] public void OutputCapAndExactSize()
    {
        using var b = new SKBitmap(1920, 1080, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var pixels = new byte[1920 * 1080 * 4]; new Random(71).NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, b.GetPixels(), pixels.Length);
        using var d = b.Encode(SKEncodedImageFormat.Png, 100);
        Assert.Equal("response_too_large", Assert.Throws<CaptureFault>(() => ImageProcessor.Process(d.ToArray(), new() { Format = "png", MaxEdge = 1920, RequireExactSize = true })).Code);
        Assert.Equal("response_too_large", Assert.Throws<CaptureFault>(() => ImageProcessor.Process(d.ToArray(), new() { Format = "png", MaxEdge = 1920 })).Code);
        var r = ImageProcessor.Process(d.ToArray(), new() { Format = "png", MaxEdge = 1280 });
        Assert.InRange(r.Bytes.Length, 1, ImagePolicy.MaximumBytes); Assert.True(r.Metadata.OutputWidth < 1920);
    }
    [Fact] public async Task StreamIsBounded()
    {
        using var stream = new MemoryStream(new byte[ImageProcessor.MaximumInputBytes + 1]);
        await Assert.ThrowsAsync<CaptureFault>(() => ImageProcessor.ReadBoundedAsync(stream));
    }
    [Fact] public void LegacyCropStaysOpaque()
    {
        Assert.Equal((byte)255, ImagePolicy.CropScale([1, 2, 3, 4], 1, 1, new(0,0,1,1), 1, 1, default)[3]);
        Assert.Equal((byte)4, ImagePolicy.CropScale([1, 2, 3, 4], 1, 1, new(0,0,1,1), 1, 1, default, true)[3]);
    }
}
