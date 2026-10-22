using System.Diagnostics;
using System.Text.Json;

namespace Flamoris.Capture.Core;

public static class ObservationClock
{
    public static long Ticks => (long)(Stopwatch.GetTimestamp() * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
}
public sealed record RecordedFrame(int Index, double TimeMs, ScreenshotMetadata Image);
public sealed record InteractionEvent(long Sequence, double TimeMs, string Kind, string? Key = null,
    int? X = null, int? Y = null, int? Delta = null);
public sealed record RecordingSummary(bool Recording, bool RecordingOperations, int FrameCount, double DurationMs,
    long StoredBytes, int EventCount, long DroppedEvents, int FramesPerSecond, string? StopReason, string? RecordingId,
    string FrameSelection = "timeMs selects the frame at or immediately before that time; frameIndex is zero-based");
public sealed record EventPage(InteractionEvent[] Events, long NextSequence, long DroppedEvents, long ClockOriginTicks);

// Human controls acquisition. MCP can only inspect this bounded, already approved workspace.
// All state changes run on the capture session's authority lane. No external path selector exists.
public sealed class ObservationWorkspace : IDisposable
{
    public const int Fps = 10, MaximumFrames = 9000, MaximumEvents = 20000;
    public const long MaximumBytes = 512L * 1024 * 1024;
    public const double MaximumDurationMs = 15 * 60 * 1000;
    private readonly CaptureSession session;
    private readonly List<RecordedFrame> frames = [];
    private readonly Queue<InteractionEvent> events = new();
    private CancellationTokenSource? acquisition;
    private Task? worker;
    private string? directory, ownerSession, recordingId;
    private long origin, recordingOrigin, bytes, sequence, dropped;
    private double duration;
    private bool operations, disposed;
    private string? reason;
    public ObservationWorkspace(CaptureSession session)
    { this.session = session; session.Invalidating += Invalidating; session.TargetResetting += TargetResetting; }
    private void Invalidating()
    {
        // Called synchronously on the authority lane, before a revoked request can disclose.
        acquisition?.Cancel(); ownerSession = null;
    }
    private void TargetResetting() { operations = false; events.Clear(); }
    private double NowMs => Math.Max(0, (ObservationClock.Ticks - origin) / (double)TimeSpan.TicksPerMillisecond);
    private void Demand() => session.Demand(ownerSession ?? "", true);
    public bool IsRecording => session.Invoke(() => worker is { IsCompleted: false });
    public bool IsRecordingOperations => session.Invoke(() => operations);
    public Task Completion => session.Invoke(() => worker ?? Task.CompletedTask);
    public RecordingSummary Summary(bool disclosure = true) => session.Invoke(() =>
    {
        if (disclosure) session.ResolveSession(requireScreen: false);
        bool visible = !disclosure || ownerSession == session.Info.SessionId && session.Info.SharingAllowed;
        return new RecordingSummary(worker is { IsCompleted: false }, operations, visible ? frames.Count : 0,
            worker is { IsCompleted: false } || operations ? Math.Max(duration, NowMs) : duration, visible ? bytes : 0,
            events.Count, dropped, Fps, reason, visible ? recordingId : null);
    });
    public void StartRecording() => session.Invoke(() =>
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string expected = session.ResolveSession();
        if (worker is { IsCompleted: false }) throw new CaptureFault("busy");
        DeleteFrames(strict: true); frames.Clear(); bytes = 0; duration = 0; reason = null;
        // A new recording starts a new synchronized frame/event timeline.
        events.Clear(); origin = recordingOrigin = ObservationClock.Ticks; dropped = 0;
        ownerSession = expected;
        recordingId = Guid.NewGuid().ToString("N");
        directory = Path.Combine(Path.GetTempPath(), "FLAMORIS-Capture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        acquisition?.Dispose(); acquisition = new();
        var token = acquisition.Token;
        worker = Task.Run(() => Record(expected, token));
        return true;
    });
    private async Task Record(string expected, CancellationToken token)
    {
        using var cadence = new PeriodicTimer(TimeSpan.FromSeconds(1d / Fps));
        try
        {
            do
            {
                token.ThrowIfCancellationRequested();
                if (session.Invoke(() => NowMs >= MaximumDurationMs)) throw new CaptureFault("resource_limit");
                CaptureResult result;
                try { result = await session.CaptureAsync(new() { SessionId = expected, MaxEdge = 1280, RequireExactSize = true }, false, token); }
                catch (CaptureFault f) when (f.Code is "busy" or "frame_unavailable" or "timeout") { continue; }
                session.Invoke(() =>
                {
                    token.ThrowIfCancellationRequested(); session.Demand(expected, true);
                    double time = Math.Max(0, (result.Image.SystemRelativeTimeTicks - origin) / (double)TimeSpan.TicksPerMillisecond);
                    if (time >= MaximumDurationMs || frames.Count >= MaximumFrames || bytes + result.Image.Bytes.Length > MaximumBytes)
                        throw new CaptureFault("resource_limit");
                    if (frames.Count > 0 && (result.Image.OutputWidth != frames[0].Image.OutputWidth || result.Image.OutputHeight != frames[0].Image.OutputHeight))
                        throw new CaptureFault("geometry_changed");
                    // Monotonic timestamps, from WGC's QPC clock shared with input events.
                    if (frames.Count > 0 && time <= frames[^1].TimeMs) throw new CaptureFault("timestamp_unavailable");
                    File.WriteAllBytes(FramePath(frames.Count), result.Image.Bytes);
                    frames.Add(new(frames.Count, time, result.Metadata)); bytes += result.Image.Bytes.Length; duration = time;
                    return true;
                });
            } while (await cadence.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) { session.Invoke(() => { reason ??= "stopped"; return true; }); }
        catch (CaptureFault fault) { session.Invoke(() => { reason = fault.Code; return true; }); }
        catch { session.Invoke(() => { reason = "recording_failed"; return true; }); }
    }
    public void StopRecording() => session.Invoke(() => { acquisition?.Cancel(); return true; });
    public void SetOperations(bool enabled) => session.Invoke(() =>
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        // Operation consent is independent of screen sharing, but still bound to the selected target.
        var info = session.Info;
        if (enabled && !info.CaptureAllowed) throw new CaptureFault("target_lost");
        if (enabled && !operations && worker is not { IsCompleted: false })
        {
            events.Clear(); dropped = 0;
            if (frames.Count == 0 || ownerSession != info.SessionId) origin = ObservationClock.Ticks;
        }
        if (operations && !enabled) duration = Math.Max(duration, NowMs);
        operations = enabled;
        return true;
    });
    public void AddEvent(string kind, string? key = null, int? x = null, int? y = null, int? delta = null) => session.Invoke(() =>
    {
        if (!operations || disposed) return false;
        // Only the Windows target-filtered adapter calls this. Never accept text or arbitrary payloads.
        if (kind is not ("keyDown" or "keyUp" or "mouseDown" or "mouseUp" or "mouseMove" or "wheel" or "horizontalWheel"))
            throw new CaptureFault("invalid_request");
        if (key is { Length: > 32 }) throw new CaptureFault("invalid_request");
        if (events.Count == MaximumEvents) { events.Dequeue(); dropped++; }
        events.Enqueue(new(++sequence, NowMs, kind, key, x, y, delta)); return true;
    });
    public EventPage Events(long afterSequence = 0, int limit = 100) => session.Invoke(() =>
    {
        session.ResolveSession(requireScreen: false);
        if (afterSequence < 0 || limit is < 1 or > 500) throw new CaptureFault("invalid_request");
        var page = events.Where(e => e.Sequence > afterSequence).Take(limit).ToArray();
        return new EventPage(page, page.Length == 0 ? afterSequence : page[^1].Sequence, dropped, origin);
    });
    public RecordedFrame[] Frames(bool disclosure = true) => session.Invoke(() =>
    { if (disclosure) Demand(); return frames.ToArray(); });
    public (RecordedFrame Frame, byte[] Bytes) ReadFrame(int? frameIndex = null, double? timeMs = null, bool disclosure = true) => session.Invoke(() =>
    {
        if (disclosure) Demand();
        if (frameIndex is not null && timeMs is not null || frameIndex is < 0 || timeMs is { } time && (!double.IsFinite(time) || time < 0))
            throw new CaptureFault("invalid_request");
        if (frames.Count == 0) throw new CaptureFault("frame_unavailable");
        int index = frameIndex ?? (timeMs is { } at ? frames.FindLastIndex(f => f.TimeMs <= at) : frames.Count - 1);
        if (index < 0 || index >= frames.Count || timeMs > frames[^1].TimeMs) throw new CaptureFault("frame_unavailable");
        var frame = frames[index]; return (frame, File.ReadAllBytes(FramePath(index)));
    });
    public void SaveEvents(string path) => session.Invoke(() =>
    {
        File.WriteAllText(path, JsonSerializer.Serialize(new { schemaVersion = "0.2", clock = "qpc", eventOriginTicks = origin, recordingOriginTicks = recordingOrigin,
            recording = Summary(false), mp4TimelineOriginMs = frames.Count > 0 ? frames[0].TimeMs : (double?)null, frames = frames.ToArray(), events = events.ToArray() }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return true;
    });
    private string FramePath(int index) => Path.Combine(directory ?? throw new CaptureFault("frame_unavailable"), $"{index:D6}.jpg");
    private void DeleteFrames(bool strict = false)
    {
        try { if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true); directory = null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { if (strict) throw new CaptureFault("cleanup_failed"); }
    }
    public void Dispose() => session.Invoke(() =>
    {
        if (disposed) return true;
        disposed = true; operations = false; acquisition?.Cancel(); session.Invalidating -= Invalidating; session.TargetResetting -= TargetResetting;
        // Clean after in-flight disk writes have stopped; never delete a live worker's directory.
        void Clean() { DeleteFrames(); events.Clear(); acquisition?.Dispose(); }
        if (worker is null || worker.IsCompleted) Clean();
        else _ = worker.ContinueWith(_ => session.Invoke(() => { Clean(); return true; }), TaskScheduler.Default);
        return true;
    });
}
