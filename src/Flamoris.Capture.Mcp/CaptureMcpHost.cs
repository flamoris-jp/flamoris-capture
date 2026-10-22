using System.Text.Json;
using System.Text.Json.Serialization;
using Flamoris.Capture.Core;
using Flamoris.Mcp.Core;

namespace Flamoris.Capture.Mcp;

public sealed class CaptureMcpHost(CaptureSession session) : IMcpHost
{
    private readonly string runtimeId = Guid.NewGuid().ToString("N");
    public HostSnapshot Snapshot
    {
        get { var s = session.Info; return new("flamoris.capture", "0.1.0", runtimeId, s.SessionId,
            s.PermissionEpoch, s.CaptureAllowed && s.SharingAllowed); }
    }
    public event Action? Invalidating { add => session.Invalidating += value; remove => session.Invalidating -= value; }
    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token) => Task.FromResult(session.Invoke(action, token));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false };
    private sealed record SessionRequest { public required string SessionId { get; init; } }
    private static T Decode<T>(JsonElement input)
    {
        // System.Text.Json otherwise permits duplicate property names. Reject them recursively.
        static void Unique(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject()) { if (!keys.Add(p.Name)) throw new JsonException(); Unique(p.Value); }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Unique(v);
        }
        Unique(input);
        var value = input.Deserialize<T>(Json) ?? throw new JsonException();
        if (value is ScreenshotRequest screenshot)
        {
            try { screenshot.Validate(); } catch (CaptureFault) { throw new JsonException(); }
        }
        if (value is SessionRequest sessionRequest && string.IsNullOrWhiteSpace(sessionRequest.SessionId)) throw new JsonException();
        return value;
    }
    public IEnumerable<HostTool> Tools()
    {
        yield return new HostTool<SessionRequest>("capture.session.get", "Read the approved one-window session and effective limits; no pixels or window enumeration.",
            Schema(false), OperationKind.Query, Decode<SessionRequest>, async (ctx, request, _) =>
            {
                try { return await ctx.ReadAsync(() => { session.Demand(request.SessionId, true); return JsonSerializer.SerializeToElement(session.Info, Json); }); }
                catch (CaptureFault e) { throw new McpFault(e.Code); }
            }, foreground: false);
        yield return new HostImageTool<ScreenshotRequest>("capture.screenshot.get", "Capture one fresh image of the locally approved window. No target switching. PNG/JPEG <= 1 MiB.",
            Schema(true), Decode<ScreenshotRequest>, async (ctx, request, token) =>
            {
                try
                {
                    await ctx.ReadAsync(() => { session.Demand(request.SessionId, true); return JsonSerializer.SerializeToElement(true); });
                    var result = await session.CaptureAsync(request, true, token);
                    return await InvokeAsync(() =>
                    {
                        session.Demand(request.SessionId, true);
                        return new McpResult(JsonSerializer.SerializeToElement(result.Metadata, Json))
                            { Image = new McpImage(result.Image.MimeType, result.Image.Bytes) };
                    }, token);
                }
                catch (CaptureFault e) { throw new McpFault(e.Code); }
            });
    }
    private static JsonElement Schema(bool image)
    {
        var properties = new Dictionary<string, object> { ["sessionId"] = new { type = "string", minLength = 1 } };
        if (image)
        {
            properties["maxEdge"] = new { type = "integer", minimum = 1, maximum = 1920, @default = 1280 };
            properties["format"] = new { type = "string", @enum = new[] { "png", "jpeg" }, @default = "jpeg" };
            properties["requireExactSize"] = new { type = "boolean", @default = false };
            properties["roi"] = new { type = "object", additionalProperties = false, required = new[] { "x", "y", "width", "height" },
                properties = new { x = new { type = "integer", minimum = 0 }, y = new { type = "integer", minimum = 0 },
                    width = new { type = "integer", minimum = 1 }, height = new { type = "integer", minimum = 1 } } };
        }
        return JsonSerializer.SerializeToElement(new { type = "object", properties, required = new[] { "sessionId" }, additionalProperties = false });
    }
}
