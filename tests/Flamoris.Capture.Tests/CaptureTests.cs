using System.Text.Json;
using Flamoris.Capture.Core;
using Flamoris.Capture.Mcp;
using Flamoris.Logging;
using Flamoris.Mcp.Core;
using Xunit;

namespace Flamoris.Capture.Tests;

public sealed class CaptureTests
{
    internal sealed class Target : ICaptureTarget
    {
        public bool IsAlive { get; private set; } = true;
        public event Action? Lost;
        public void Lose() { IsAlive = false; Lost?.Invoke(); }
        public void Dispose() => IsAlive = false;
    }
    internal sealed class Backend : ICaptureBackend
    {
        public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9xkAAAAASUVORK5CYII=");
        public Func<CancellationToken, Task>? Wait { get; set; }
        public int Calls;
        public bool LiveClock { get; set; }
        public byte[] Bytes { get; set; } = Png;
        public async Task<EncodedScreenshot> CaptureAsync(ICaptureTarget target, ScreenshotRequest request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (Wait is not null) await Wait(token);
            return new(Bytes, "image/png", 1, 1, new(0, 0, 1, 1), null,
                new(1, 1, -1920, 0, 144, "test-geometry"), LiveClock ? ObservationClock.Ticks : 1234, DateTimeOffset.UnixEpoch);
        }
    }
    private static McpBoundary Boundary(CaptureSession session)
    {
        var host = new CaptureMcpHost(session);
        return new(host, host.Tools(), new() { ApplicationLifetime = true }, new McpDiagnostics(FlamorisLogger.Create()));
    }
    [Fact] public async Task EmptyStartupStatusWorksAndSharingDoesNotRotateConnection()
    {
        using var session = new CaptureSession(new Backend());
        using var boundary = Boundary(session); using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        string credential = grant.ExportCredential();
        var empty = JsonSerializer.SerializeToElement(new { });
        Assert.False((await boundary.InvokeAsync(grant, "capture.session.get", empty, null)).IsError);
        Assert.True((await boundary.InvokeAsync(grant, "capture.screenshot.get", empty, null)).IsError);
        session.SelectAndAllow(new Target()); session.SetSharing(true);
        Assert.False((await boundary.InvokeAsync(grant, "capture.screenshot.get", empty, null)).IsError);
        session.SetSharing(false);
        Assert.True(grant.Authenticate(credential));
        Assert.False((await boundary.InvokeAsync(grant, "capture.session.get", empty, null)).IsError);
        Assert.Equal("forbidden", (await boundary.InvokeAsync(grant, "capture.screenshot.get", empty, null)).Error);
    }
    private static ScreenshotRequest Request(CaptureSession s) => new() { SessionId = s.Info.SessionId };
    private static JsonElement Input(CaptureSession s) => JsonSerializer.SerializeToElement(new { sessionId = s.Info.SessionId });
    [Fact] public async Task StartsOffAndScreenCheckboxGatesBothLocalAndMcp()
    {
        var backend = new Backend(); using var s = new CaptureSession(backend);
        Assert.False(s.Info.CaptureAllowed); Assert.False(s.Info.SharingAllowed);
        s.SelectAndAllow(new Target());
        Assert.Equal("forbidden", (await Assert.ThrowsAsync<CaptureFault>(() => s.CaptureAsync(Request(s), true))).Code);
        Assert.Equal(0, backend.Calls);
        Assert.Equal("forbidden", (await Assert.ThrowsAsync<CaptureFault>(() => s.CaptureAsync(Request(s), false))).Code);
        s.SetSharing(true);
        var local = await s.CaptureAsync(Request(s), false);
        Assert.Equal(-1920, local.Metadata.Geometry.DesktopOriginX); Assert.Equal((uint)144, local.Metadata.Geometry.Dpi);
        Assert.Equal(1234, local.Metadata.SystemRelativeTimeTicks); Assert.Null(local.Metadata.SyncErrorBoundMs);
    }
    [Fact] public async Task ReadOnlyMcpReturnsSingleImageAndMetadataWithoutBytesOrTitle()
    {
        using var s = new CaptureSession(new Backend()); s.SelectAndAllow(new Target()); s.SetSharing(true);
        using var b = Boundary(s); using var g = await b.EnableAsync(McpPermission.ReadOnly);
        var reply = await b.InvokeAsync(g, "capture.screenshot.get", Input(s), null);
        Assert.False(reply.IsError); Assert.NotNull(reply.Image); Assert.Equal(Backend.Png, reply.Image.Bytes.ToArray());
        var json = reply.Value!.Value;
        Assert.Equal(64, json.GetProperty("sha256").GetString()!.Length);
        Assert.Equal(s.Info.TargetId, json.GetProperty("targetId").GetString());
        Assert.DoesNotContain(Convert.ToBase64String(Backend.Png), json.GetRawText());
        Assert.DoesNotContain("title", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, b.Tools.Count);
        var metadata = await b.InvokeAsync(g, "capture.session.get", Input(s), null);
        Assert.False(metadata.IsError); Assert.Null(metadata.Image);
    }
    [Fact] public async Task TargetReplacementAndSharingRevokeContentWithoutDisconnecting()
    {
        using var s = new CaptureSession(new Backend()); s.SelectAndAllow(new Target()); s.SetSharing(true);
        using var b = Boundary(s); using var g = await b.EnableAsync(McpPermission.ReadOnly);
        var old = Request(s); s.SetSharing(false);
        Assert.True(g.IsActive); Assert.True(s.Info.CaptureAllowed);
        Assert.Equal("forbidden", (await b.InvokeAsync(g, "capture.screenshot.get", Input(s), null)).Error);
        Assert.Equal("stale_session", (await Assert.ThrowsAsync<CaptureFault>(() => s.CaptureAsync(old, false))).Code);
        s.SelectAndAllow(new Target()); Assert.False(s.Info.SharingAllowed);
    }
    [Fact] public async Task RevokeDuringUncooperativeCaptureCannotDiscloseAndKeepsBusyUntilFinished()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { Wait = _ => { entered.SetResult(); return release.Task; } };
        using var s = new CaptureSession(backend); s.SelectAndAllow(new Target()); s.SetSharing(true);
        var task = s.CaptureAsync(Request(s), true); await entered.Task;
        Assert.Equal("busy", (await Assert.ThrowsAsync<CaptureFault>(() => s.CaptureAsync(Request(s), false))).Code);
        s.SetSharing(false); Assert.True(s.Info.Busy); release.SetResult();
        Assert.Equal("stale_session", (await Assert.ThrowsAsync<CaptureFault>(() => task)).Code);
        Assert.False(s.Info.Busy);
    }
    [Fact] public async Task CloseCancelsAndInvalidatesWithoutRetargeting()
    {
        var target = new Target(); using var s = new CaptureSession(new Backend());
        s.SelectAndAllow(target); s.SetSharing(true); using var b = Boundary(s); using var g = await b.EnableAsync(McpPermission.ReadOnly);
        target.Lose(); Assert.False(s.Info.CaptureAllowed); Assert.True(g.IsActive);
        Assert.Null(s.Info.TargetId);
    }
    [Theory]
    [InlineData("{\"sessionId\":\"x\",\"unexpected\":1}")]
    [InlineData("{\"sessionId\":\"x\",\"maxEdge\":2147483648}")]
    [InlineData("{\"sessionId\":\"x\",\"sessionId\":\"x\"}")]
    [InlineData("{\"sessionId\":\"x\",\"roi\":{\"x\":0,\"y\":0,\"width\":2,\"height\":2,\"oops\":true}}")]
    [InlineData("{\"sessionId\":null}")]
    [InlineData("{\"sessionId\":\"x\",\"maxEdge\":1921}")]
    [InlineData("{\"sessionId\":\"x\",\"roi\":{\"width\":2,\"height\":2}}")]
    public async Task MalformedClosedDtosAreRejectedBeforeAcquisition(string json)
    {
        var backend = new Backend(); using var s = new CaptureSession(backend); s.SelectAndAllow(new Target()); s.SetSharing(true);
        using var b = Boundary(s); using var g = await b.EnableAsync(McpPermission.ReadOnly);
        var result = await b.InvokeAsync(g, "capture.screenshot.get", JsonDocument.Parse(json).RootElement, null);
        Assert.Equal("invalid_request", result.Error); Assert.Equal(0, backend.Calls);
    }
    [Theory] [InlineData(0, 20)] [InlineData(8193, 1)] [InlineData(4096, 4096)]
    public void SourceBudgetCheckedBeforeAllocation(int width, int height) => Assert.Throws<CaptureFault>(() => ImagePolicy.ValidateSource(width, height));
    [Fact] public void RoiOverflowIsRejectedRatherThanClamped()
    {
        Assert.Throws<CaptureFault>(() => ImagePolicy.Region(20, 20, new(int.MaxValue, 0, int.MaxValue, 1)));
        Assert.Throws<CaptureFault>(() => ImagePolicy.Region(20, 20, new(19, 19, 2, 2)));
    }
    [Fact] public void RoiScaleAndEncodedAttemptsAreBoundedAndExactSizeNeverShrinks()
    {
        var roi = new PixelRect(10, 10, 200, 100);
        Assert.Equal((100, 50), ImagePolicy.Size(roi, 100));
        Assert.Equal(12, ImagePolicy.Attempts(roi, new() { SessionId = "s" }).Count());
        var exact = ImagePolicy.Attempts(roi, new() { SessionId = "s", RequireExactSize = true }).ToArray();
        Assert.All(exact, a => { Assert.Equal(200, a.Width); Assert.Equal(100, a.Height); });
        Assert.Equal(new int?[] { 80, 65, 50 }, exact.Select(a => a.Quality));
    }
    [Fact] public void CropNeverReadsOutsideSelectedRoi()
    {
        byte[] pixels = [0,0,0,255, 1,2,3,255, 4,5,6,255, 7,8,9,255];
        Assert.Equal(new byte[] {1,2,3,255, 7,8,9,255}, ImagePolicy.CropScale(pixels, 2, 2, new(1,0,1,2), 1, 2, default));
    }
    [Fact] public async Task CallerCancellationPropagatesAndReleasesAdmission()
    {
        var backend = new Backend { Wait = t => Task.Delay(Timeout.Infinite, t) };
        using var s = new CaptureSession(backend); s.SelectAndAllow(new Target()); s.SetSharing(true); using var ct = new CancellationTokenSource();
        var task = s.CaptureAsync(Request(s), false, ct.Token); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); Assert.False(s.Info.Busy);
    }
    [Theory] [InlineData(0)] [InlineData(1_048_577)]
    public async Task EncodedPayloadBudgetIsEnforced(int length)
    {
        using var s = new CaptureSession(new Backend { Bytes = new byte[length] }); s.SelectAndAllow(new Target()); s.SetSharing(true);
        Assert.Equal("resource_limit", (await Assert.ThrowsAsync<CaptureFault>(() => s.CaptureAsync(Request(s), false))).Code);
        Assert.False(s.Info.Busy);
    }
    [Fact] public async Task StaleGuardCannotCaptureNewTarget()
    {
        var backend = new Backend(); using var s = new CaptureSession(backend); s.SelectAndAllow(new Target()); s.SetSharing(true);
        using var boundary = Boundary(s); using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        var host = new CaptureMcpHost(s);
        var guard = new RequestGuard("old-runtime", host.Snapshot.DocumentToken, null);
        Assert.Equal("stale_session", (await boundary.InvokeAsync(grant, "capture.screenshot.get", Input(s), guard)).Error);
        Assert.Equal(0, backend.Calls);
    }

}
