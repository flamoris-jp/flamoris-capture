using System.Security.Cryptography;

namespace Flamoris.Capture.Core;

// The same lane serializes UI consent, target replacement and final MCP disclosure checks.
public sealed class CaptureSession : IDisposable
{
    private readonly object gate = new();
    private readonly ICaptureBackend backend;
    public ObservationWorkspace Observation { get; }
    public CaptureSession(ICaptureBackend backend) { this.backend = backend; Observation = new(this); }
    private ICaptureTarget? target;
    private CancellationTokenSource lifetime = new();
    private string sessionId = NewId(), targetId = NewId();
    private long epoch;
    private bool sharing, busy, disposed;
    public bool IsDisposed => Invoke(() => disposed);
    public SessionInfo Status(string? expected = null) => Invoke(() =>
    {
        if (disposed) throw new CaptureFault("stale_session");
        if (expected is not null && expected != sessionId) throw new CaptureFault("stale_session");
        return Info;
    });
    public event Action? Invalidating;
    internal event Action? TargetResetting;
    private static string NewId() => Guid.NewGuid().ToString("N");
    public T Invoke<T>(Func<T> action, CancellationToken token = default)
    {
        lock (gate) { token.ThrowIfCancellationRequested(); return action(); }
    }
    public SessionInfo Info => Invoke(() => new SessionInfo("0.1", sessionId, target is null ? null : targetId,
        epoch, target is not null, sharing, busy, ["capture.session.get", "capture.screenshot.get", "capture.recording.get", "capture.frame.get", "capture.contactSheet.get", "capture.events.get"],
        new { maxSourcePixels = ImagePolicy.MaximumSourcePixels, maxSourceEdge = 8192,
            maxEdge = 1920, defaultMaxEdge = 1280, maxImageBytes = ImagePolicy.MaximumBytes,
            requestTimeoutMs = 5000, maxConcurrentCaptures = 1 }));
    public void SelectAndAllow(ICaptureTarget selected)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Reset(); target = selected; target.Lost += OnLost;
            if (!target.IsAlive) { Reset(); throw new CaptureFault("target_lost"); }
        }
    }
    public void SetSharing(bool allowed)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (allowed && (target is null || !target.IsAlive)) throw new CaptureFault("target_lost");
            if (sharing == allowed) return;
            Invalidate(); sharing = allowed;
        }
    }
    public void CheckTarget()
    {
        lock (gate) { if (!disposed && target is not null && !target.IsAlive) Reset(); }
    }
    private void OnLost() => Stop();
    public void Stop() { lock (gate) { if (!disposed) Reset(); } }
    private void Invalidate()
    {
        sharing = false; epoch++; sessionId = NewId();
        lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
        Invalidating?.Invoke();
    }
    private void Reset()
    {
        TargetResetting?.Invoke();
        Invalidate();
        var old = target; target = null; targetId = NewId();
        if (old is not null) { old.Lost -= OnLost; old.Dispose(); }
    }
    public string ResolveSession(string? expectedSession = null, bool requireScreen = true) => Invoke(() =>
    {
        string resolved = expectedSession ?? sessionId; Demand(resolved, requireScreen);
        if (!requireScreen && !sharing && !Observation.IsRecordingOperations) throw new CaptureFault("forbidden");
        return resolved;
    });
    public void Demand(string expectedSession, bool disclosure)
    {
        lock (gate)
        {
            if (disposed) throw new CaptureFault("stale_session");
            if (expectedSession != sessionId) throw new CaptureFault("stale_session");
            if (target is null || !target.IsAlive) throw new CaptureFault("target_lost");
            if (disclosure && !sharing) throw new CaptureFault("forbidden");
        }
    }
    public async Task<CaptureResult> CaptureAsync(ScreenshotRequest request, bool disclosure, CancellationToken token = default)
    {
        request.Validate();
        ICaptureTarget selected; string selectedId; long selectedEpoch; CancellationTokenSource job;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            request = request with { SessionId = ResolveSession(request.SessionId) };
            Demand(request.SessionId!, true);
            if (busy) throw new CaptureFault("busy");
            busy = true; selected = target!; selectedId = targetId; selectedEpoch = epoch;
            job = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token); job.CancelAfter(5000);
        }
        try
        {
            var image = await Task.Run(() => backend.CaptureAsync(selected, request, job.Token), CancellationToken.None).ConfigureAwait(false);
            job.Token.ThrowIfCancellationRequested();
            if (image.Bytes.Length is < 1 or > ImagePolicy.MaximumBytes) throw new CaptureFault("resource_limit");
            var metadata = new ScreenshotMetadata("0.2", request.SessionId!, selectedId, selectedEpoch,
                image.MimeType, image.Bytes.Length, Convert.ToHexStringLower(SHA256.HashData(image.Bytes)),
                image.OutputWidth, image.OutputHeight, image.Roi,
                (double)image.OutputWidth / image.Roi.Width, (double)image.OutputHeight / image.Roi.Height,
                image.Quality, image.Geometry, image.SystemRelativeTimeTicks, image.CapturedAtUtc);
            lock (gate)
            {
                job.Token.ThrowIfCancellationRequested(); Demand(request.SessionId!, true);
                return new(image, metadata);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            lock (gate)
                throw new CaptureFault(request.SessionId != sessionId ? "stale_session" : "timeout");
        }
        finally { job.Dispose(); lock (gate) busy = false; }
    }
    public void Dispose()
    {
        Observation.Dispose();
        lock (gate) { if (disposed) return; disposed = true; Reset(); lifetime.Dispose(); }
    }
}
