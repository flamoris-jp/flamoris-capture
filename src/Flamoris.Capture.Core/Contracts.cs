using System.Text.Json.Serialization;

namespace Flamoris.Capture.Core;

public sealed class CaptureFault(string code) : Exception(code)
{
    public string Code { get; } = code;
}
public sealed record PixelRect([property: JsonRequired] int X, [property: JsonRequired] int Y,
    [property: JsonRequired] int Width, [property: JsonRequired] int Height);
public sealed record ScreenshotRequest
{
    public string? SessionId { get; init; }
    public PixelRect? Roi { get; init; }
    public int MaxEdge { get; init; } = 1280;
    public string Format { get; init; } = "jpeg";
    public bool RequireExactSize { get; init; }
    public void Validate()
    {
        if ((SessionId is not null && string.IsNullOrWhiteSpace(SessionId)) || MaxEdge is < 1 or > 1920 || Format is not ("png" or "jpeg"))
            throw new CaptureFault("invalid_request");
        if (Roi is { } r && (r.X < 0 || r.Y < 0 || r.Width < 1 || r.Height < 1))
            throw new CaptureFault("invalid_request");
    }
}
public sealed record ImageGeometry(int SourceWidth, int SourceHeight, int DesktopOriginX,
    int DesktopOriginY, uint Dpi, string GeometryId);
public sealed record EncodedScreenshot(byte[] Bytes, string MimeType, int OutputWidth, int OutputHeight,
    PixelRect Roi, int? Quality, ImageGeometry Geometry, long SystemRelativeTimeTicks, DateTimeOffset CapturedAtUtc);
public sealed record ScreenshotMetadata(string SchemaVersion, string SessionId, string TargetId,
    long PermissionEpoch, string MimeType, int ByteLength, string Sha256, int OutputWidth, int OutputHeight,
    PixelRect Roi, double ScaleX, double ScaleY, int? Quality, ImageGeometry Geometry,
    long SystemRelativeTimeTicks, DateTimeOffset CapturedAtUtc, string CursorMode = "excluded",
    string TimestampSource = "wgc_qpc", double? SyncErrorBoundMs = null);
public sealed record CaptureResult(EncodedScreenshot Image, ScreenshotMetadata Metadata);
public sealed record SessionInfo(string SchemaVersion, string SessionId, string? TargetId,
    long PermissionEpoch, bool CaptureAllowed, bool SharingAllowed, bool Busy,
    string[] SupportedQueries, object Limits);

// Targets are immutable OS capture items. Never recreate an item from a saved HWND during acquisition.
public interface ICaptureTarget : IDisposable
{
    bool IsAlive { get; }
    event Action? Lost;
}
public interface ICaptureBackend
{
    Task<EncodedScreenshot> CaptureAsync(ICaptureTarget target, ScreenshotRequest request, CancellationToken token);
}
