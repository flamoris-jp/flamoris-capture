using System.Text.Json;
using System.Text.Json.Serialization;
using Flamoris.Capture.Core;
using Flamoris.Mcp.Core;

namespace Flamoris.Capture.Mcp;

public sealed class CaptureMcpHost(CaptureSession session, IContactSheetRenderer? sheets = null) : IMcpHost
{
    private readonly string runtimeId = Guid.NewGuid().ToString("N");
    public HostSnapshot Snapshot
    {
        get { var s = session.Info; return new("flamoris.capture", "0.2.0", runtimeId, s.SessionId,
            s.PermissionEpoch, !session.IsDisposed) { HasDocument = false }; }
    }
    // Content revocation belongs to CaptureSession; callers revoke the transport on app shutdown.
    public event Action? Invalidating { add { } remove { } }
    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token) => Task.FromResult(session.Invoke(action, token));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false };
    private record SessionRequest { public string? SessionId { get; init; } }
    private sealed record FrameRequest : SessionRequest { public int? FrameIndex { get; init; } public double? TimeMs { get; init; } }
    private sealed record SheetRequest : SessionRequest { public int Count { get; init; } = 6; }
    private sealed record EventsRequest : SessionRequest { public long AfterSequence { get; init; } public int Limit { get; init; } = 100; }
    private static T Decode<T>(JsonElement input)
    {
        // System.Text.Json otherwise permits duplicate property names. Reject them recursively.
        static void Unique(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject()) { if (!keys.Add(p.Name) || p.Value.ValueKind == JsonValueKind.Null) throw new JsonException(); Unique(p.Value); }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Unique(v);
        }
        Unique(input);
        var value = input.Deserialize<T>(Json) ?? throw new JsonException();
        if (value is ScreenshotRequest screenshot)
        {
            try { screenshot.Validate(); } catch (CaptureFault) { throw new JsonException(); }
        }
        if (value is SessionRequest sessionRequest && (sessionRequest.SessionId is not null && string.IsNullOrWhiteSpace(sessionRequest.SessionId))) throw new JsonException();
        return value;
    }
    /// <summary>Read-only observation surface for an editor's existing MCP boundary.</summary>
    public IEnumerable<HostTool> ScreenshotTools() => Tools().Where(tool =>
        tool.Name is "capture.session.get" or "capture.screenshot.get");

    public IEnumerable<HostTool> Tools()
    {
        yield return new HostTool<SessionRequest>("capture.session.get", "Read the currently approved window and limits. No sessionId needed; no pixels or window enumeration.",
            Schema(false), OperationKind.Query, Decode<SessionRequest>, async (ctx, request, _) =>
            {
                try { return await ctx.ReadAsync(() => { return JsonSerializer.SerializeToElement(session.Status(request.SessionId), Json); }); }
                catch (CaptureFault e) { throw new McpFault(e.Code); }
            }, foreground: false) { RequiresDocument = false };
        yield return new HostImageTool<ScreenshotRequest>("capture.screenshot.get", "Capture the currently shared window. No sessionId needed; no target switching. PNG/JPEG <= 1 MiB.",
            Schema(true), Decode<ScreenshotRequest>, async (ctx, request, token) =>
            {
                try
                {
                    await BindContent(ctx, request.SessionId, true);
                    string expected = "";
                    await ctx.ReadAsync(() => { expected = session.ResolveSession(request.SessionId); return JsonSerializer.SerializeToElement(true); });
                    request = request with { SessionId = expected };
                    var result = await session.CaptureAsync(request, true, token);
                    return await InvokeAsync(() =>
                    {
                        session.Demand(request.SessionId!, true);
                        return new McpResult(JsonSerializer.SerializeToElement(result.Metadata, Json))
                            { Image = new McpImage(result.Image.MimeType, result.Image.Bytes) };
                    }, token);
                }
                catch (CaptureFault e) { throw new McpFault(e.Code); }
            }) { RequiresDocument = false };

        yield return new HostTool<SessionRequest>("capture.recording.get", "Read the current recording summary and bounded resource counters. Acquisition is controlled by the human UI.",
            Schema(false), OperationKind.Query, Decode<SessionRequest>, async (ctx, request, _) =>
            {
                try { await BindContent(ctx, request.SessionId, false); return await ctx.ReadAsync(() => { session.ResolveSession(request.SessionId, false); return JsonSerializer.SerializeToElement(session.Observation.Summary(), Json); }); }
                catch (CaptureFault e) { throw new McpFault(e.Code); }
            }, foreground: false) { RequiresDocument = false };
        yield return new HostTool<EventsRequest>("capture.events.get", "Read a page of recorded target-window key and mouse events, relative to the recording QPC time origin. Never starts recording.",
            ExtraSchema(new { afterSequence = new { type = "integer", minimum = 0, @default = 0 }, limit = new { type = "integer", minimum = 1, maximum = 500, @default = 100 } }),
            OperationKind.Query, Decode<EventsRequest>, async (ctx, request, _) =>
            {
                try { await BindContent(ctx, request.SessionId, false, operations: true); return await ctx.ReadAsync(() => { session.ResolveSession(request.SessionId, false); return JsonSerializer.SerializeToElement(session.Observation.Events(request.AfterSequence, request.Limit), Json); }); }
                catch (CaptureFault e) { throw new McpFault(e.Code); }
            }, foreground: false) { RequiresDocument = false };
        yield return new HostImageTool<FrameRequest>("capture.frame.get", "Read one recorded frame by zero-based frameIndex OR recording-relative timeMs. Omit both for latest. Returns one bounded image; never uploads video.",
            ExtraSchema(new { frameIndex = new { type = "integer", minimum = 0 }, timeMs = new { type = "number", minimum = 0 } }), Decode<FrameRequest>, async (ctx, request, token) =>
            {
                try
                {
                    await BindContent(ctx, request.SessionId, true);
                    McpResult? result = null;
                    await ctx.ReadAsync(() =>
                    {
                        session.ResolveSession(request.SessionId);
                        var frame = session.Observation.ReadFrame(request.FrameIndex, request.TimeMs);
                        result = new(JsonSerializer.SerializeToElement(frame.Frame, Json)) { Image = new(frame.Frame.Image.MimeType, frame.Bytes) };
                        return JsonSerializer.SerializeToElement(true);
                    });
                    return result!;
                }
                catch (CaptureFault e) { throw new McpFault(e.Code); }
            }) { RequiresDocument = false };
        yield return new HostImageTool<SheetRequest>("capture.contactSheet.get", "Read 1-9 evenly spaced recorded frames as ONE image with cell bounds and exact frame indices/timestamps. No video transfer.",
            ExtraSchema(new { count = new { type = "integer", minimum = 1, maximum = 9, @default = 6 } }), Decode<SheetRequest>, async (ctx, request, token) =>
            {
                try
                {
                    await BindContent(ctx, request.SessionId, true);
                    string expected = ""; string? recordingId = null; SheetInput[] selected = [];
                    await ctx.ReadAsync(() =>
                    {
                        expected = session.ResolveSession(request.SessionId);
                        if (request.Count is < 1 or > 9) throw new CaptureFault("invalid_request");
                        recordingId = session.Observation.Summary().RecordingId;
                        var frames = session.Observation.Frames();
                        if (frames.Length == 0) throw new CaptureFault("frame_unavailable");
                        int count = Math.Min(request.Count, frames.Length);
                        selected = Enumerable.Range(0, count).Select(i =>
                        {
                            int index = count == 1 ? frames.Length - 1 : (int)((long)i * (frames.Length - 1) / (count - 1));
                            var f = session.Observation.ReadFrame(index); return new SheetInput(f.Frame, f.Bytes);
                        }).ToArray();
                        return JsonSerializer.SerializeToElement(true);
                    });
                    if (sheets is null) throw new CaptureFault("unsupported_capability");
                    var sheet = await sheets.RenderAsync(selected, token);
                    return await InvokeAsync(() =>
                    {
                        session.Demand(expected, true); session.Observation.Frames();
                        if (recordingId != session.Observation.Summary().RecordingId) throw new CaptureFault("stale_recording");
                        if (sheet.Bytes.Length > ImagePolicy.MaximumBytes) throw new CaptureFault("resource_limit");
                        return new McpResult(JsonSerializer.SerializeToElement(new { sheet.Width, sheet.Height, sheet.Cells }, Json)) { Image = new(sheet.MimeType, sheet.Bytes) };
                    }, token);
                }
                catch (CaptureFault e) { throw new McpFault(e.Code); }
            }) { RequiresDocument = false };
    }
    private async Task BindContent(RequestContext context, string? expected, bool screen, bool operations = false)
    {
        string identity = ""; long operationEpoch = 0;
        await context.ReadAsync(() => {
            identity = session.ResolveSession(expected, screen);
            operationEpoch = session.Observation.OperationEpoch;
            return JsonSerializer.SerializeToElement(true);
        });
        await context.BindDisclosureAsync(() => {
            try {
                session.ResolveSession(identity, screen);
                if (operations && (!session.Observation.IsRecordingOperations || operationEpoch != session.Observation.OperationEpoch))
                    throw new CaptureFault("forbidden");
            } catch (CaptureFault e) { throw new McpFault(e.Code); }
        });
    }
    private static JsonElement ExtraSchema(object extra)
    {
        var properties = new Dictionary<string, JsonElement> { ["sessionId"] = JsonSerializer.SerializeToElement(new { type = "string", minLength = 1 }) };
        foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject()) properties.Add(property.Name, property.Value);
        return JsonSerializer.SerializeToElement(new { type = "object", properties, additionalProperties = false });
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
        return JsonSerializer.SerializeToElement(new { type = "object", properties, required = Array.Empty<string>(), additionalProperties = false });
    }
}
