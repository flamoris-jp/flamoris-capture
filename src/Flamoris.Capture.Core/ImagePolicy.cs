namespace Flamoris.Capture.Core;

public static class ImagePolicy
{
    public const int MaximumBytes = 1_048_576;
    public const long MaximumSourcePixels = 8_294_400;
    public static void ValidateSource(int width, int height)
    {
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > MaximumSourcePixels)
            throw new CaptureFault("resource_limit");
    }
    public static PixelRect Region(int width, int height, PixelRect? roi)
    {
        ValidateSource(width, height);
        var r = roi ?? new PixelRect(0, 0, width, height);
        if (r.X < 0 || r.Y < 0 || r.Width <= 0 || r.Height <= 0 ||
            (long)r.X + r.Width > width || (long)r.Y + r.Height > height)
            throw new CaptureFault("invalid_request");
        return r;
    }
    public static (int Width, int Height) Size(PixelRect roi, int maxEdge)
    {
        if (maxEdge is < 1 or > 1920) throw new CaptureFault("invalid_request");
        double scale = Math.Min(1, (double)maxEdge / Math.Max(roi.Width, roi.Height));
        return (Math.Max(1, (int)Math.Floor(roi.Width * scale)), Math.Max(1, (int)Math.Floor(roi.Height * scale)));
    }
    // Explicit bounded nearest-neighbour resampling; no interpolation across the ROI boundary.
    public static byte[] CropScale(byte[] bgra, int sourceWidth, int sourceHeight, PixelRect roi,
        int width, int height, CancellationToken token)
    {
        Region(sourceWidth, sourceHeight, roi);
        if (bgra.Length != checked(sourceWidth * sourceHeight * 4) || width < 1 || height < 1 || width > 1920 || height > 1920)
            throw new CaptureFault("invalid_request");
        var output = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            int sy = roi.Y + (int)((long)y * roi.Height / height);
            for (int x = 0; x < width; x++)
            {
                int sx = roi.X + (int)((long)x * roi.Width / width);
                int src = (sy * sourceWidth + sx) * 4, dst = (y * width + x) * 4;
                output[dst] = bgra[src]; output[dst + 1] = bgra[src + 1]; output[dst + 2] = bgra[src + 2];
                output[dst + 3] = 255;
            }
        }
        return output;
    }
    public static IEnumerable<(int Width, int Height, int? Quality)> Attempts(PixelRect roi, ScreenshotRequest request)
    {
        var (w, h) = Size(roi, request.MaxEdge);
        for (int step = 0; step <= (request.RequireExactSize ? 0 : 3); step++)
        {
            if (request.Format == "jpeg")
                foreach (int q in new[] { 80, 65, 50 }) yield return (w, h, q);
            else yield return (w, h, null);
            w = Math.Max(1, w * 3 / 4); h = Math.Max(1, h * 3 / 4);
        }
    }
}
