using System.Text.Json;
using Flamoris.Capture.Core;
using Flamoris.Capture.Mcp;
using Flamoris.Logging;
using Flamoris.Mcp.Core;
using Xunit;

namespace Flamoris.Capture.Tests;

public sealed class ObservationTests
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private static McpBoundary Boundary(CaptureSession session, IContactSheetRenderer? renderer = null)
    {
        var host = new CaptureMcpHost(session, renderer);
        return new(host, host.Tools(), new() { ApplicationLifetime = true }, new McpDiagnostics(FlamorisLogger.Create()));
    }
    private static CaptureSession Session()
    {
        var session = new CaptureSession(new CaptureTests.Backend { LiveClock = true });
        session.SelectAndAllow(new CaptureTests.Target()); session.SetSharing(true); return session;
    }
    private static async Task WaitFrames(CaptureSession session, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.Observation.Frames(false).Length < count)
        {
            if (!session.Observation.IsRecording) throw new Exception("Unexpected stop: " + session.Observation.Summary(false).StopReason);
            await Task.Delay(10, timeout.Token);
        }
    }
    [Fact] public async Task EmptyInputUsesCurrentTargetAndOldExplicitIdStillFails()
    {
        using var session = Session(); using var boundary = Boundary(session); using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        Assert.False((await boundary.InvokeAsync(grant, "capture.screenshot.get", Empty, null)).IsError);
        Assert.False((await boundary.InvokeAsync(grant, "capture.session.get", Empty, null)).IsError);
        var old = JsonSerializer.SerializeToElement(new { sessionId = "expired-id" });
        Assert.Equal("stale_session", (await boundary.InvokeAsync(grant, "capture.screenshot.get", old, null)).Error);
    }
    [Fact] public async Task OperationsOnlyMcpReadsEventsButCannotReadPixels()
    {
        using var session = Session(); session.SetSharing(false); session.Observation.SetOperations(true);
        session.Observation.AddEvent("keyDown", "NumPad1");
        using var boundary = Boundary(session); using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        Assert.False((await boundary.InvokeAsync(grant, "capture.session.get", Empty, null)).IsError);
        var events = await boundary.InvokeAsync(grant, "capture.events.get", Empty, null);
        Assert.False(events.IsError); Assert.Single(events.Value!.Value.GetProperty("events").EnumerateArray());
        Assert.Equal("forbidden", (await boundary.InvokeAsync(grant, "capture.screenshot.get", Empty, null)).Error);
        session.Observation.SetOperations(false);
        Assert.True((await boundary.InvokeAsync(grant, "capture.events.get", Empty, null)).IsError);
    }
    [Fact] public async Task RecordStopAndSelectTimeOrIndexWithoutSendingVideo()
    {
        using var session = Session(); session.Observation.StartRecording(); await WaitFrames(session, 3);
        session.Observation.StopRecording(); await session.Observation.Completion;
        var frames = session.Observation.Frames();
        Assert.True(frames[2].TimeMs > frames[1].TimeMs);
        Assert.Equal(1, session.Observation.ReadFrame(timeMs: (frames[1].TimeMs + frames[2].TimeMs) / 2).Frame.Index);
        using var boundary = Boundary(session); using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        var reply = await boundary.InvokeAsync(grant, "capture.frame.get", JsonSerializer.SerializeToElement(new { frameIndex = 1 }), null);
        Assert.False(reply.IsError); Assert.NotNull(reply.Image);
        Assert.Equal(CaptureTests.Backend.Png, reply.Image.Bytes.ToArray());
        Assert.Equal(1, reply.Value!.Value.GetProperty("index").GetInt32());
        var summary = await boundary.InvokeAsync(grant, "capture.recording.get", Empty, null);
        Assert.False(summary.IsError); Assert.False(summary.Value!.Value.GetProperty("recording").GetBoolean());
        Assert.Throws<CaptureFault>(() => session.Observation.ReadFrame(0, 1));
        Assert.Throws<CaptureFault>(() => session.Observation.ReadFrame(timeMs: double.NaN));
        Assert.Throws<CaptureFault>(() => session.Observation.ReadFrame(frames.Length));
        Assert.Throws<CaptureFault>(() => session.Observation.ReadFrame(timeMs: frames[^1].TimeMs + 1));
    }
    [Fact] public async Task ScreenOffCancelsRecordingAndBlocksHistoricalDisclosure()
    {
        using var session = Session(); session.Observation.SetOperations(true); session.Observation.StartRecording();
        await WaitFrames(session, 2);
        session.SetSharing(false); await session.Observation.Completion;
        Assert.False(session.Observation.IsRecording);
        Assert.True(session.Observation.IsRecordingOperations); // independent consent
        Assert.Throws<CaptureFault>(() => session.Observation.ReadFrame());
        Assert.NotEmpty(session.Observation.ReadFrame(disclosure: false).Bytes); // human can save stopped footage
        session.SetSharing(true);
        Assert.Throws<CaptureFault>(() => session.Observation.ReadFrame()); // toggling ON cannot reauthorize an old asset
        session.Observation.SetOperations(false); session.Observation.SetOperations(true);
        Assert.Throws<CaptureFault>(() => session.Observation.ReadFrame());
        session.Stop(); Assert.False(session.Observation.IsRecordingOperations);
    }
    [Fact] public void EventRecordingIsOptInIndependentAndBounded()
    {
        using var session = Session();
        session.Observation.AddEvent("keyDown", "G");
        Assert.Throws<CaptureFault>(() => session.Observation.Events());
        session.Observation.SetOperations(true);
        session.Observation.AddEvent("keyDown", "NumPad1"); session.Observation.AddEvent("keyUp", "NumPad1");
        var page = session.Observation.Events(limit: 1);
        Assert.Equal("NumPad1", Assert.Single(page.Events).Key);
        Assert.Single(session.Observation.Events(page.NextSequence).Events);
        for (int i = 0; i < ObservationWorkspace.MaximumEvents; i++) session.Observation.AddEvent("mouseMove", x: i, y: 0);
        Assert.Equal(2, session.Observation.Events().DroppedEvents);
        session.Observation.SetOperations(false);
        var count = session.Observation.Summary().EventCount;
        session.Observation.AddEvent("keyDown", "R"); Assert.Equal(count, session.Observation.Summary().EventCount);
        Assert.Throws<CaptureFault>(() => session.Observation.Events(limit: 501));
    }
    private sealed class SheetRenderer : IContactSheetRenderer
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ContactSheetResult> RenderAsync(SheetInput[] frames, CancellationToken token)
        {
            Entered.SetResult(); await Release.Task;
            return new(CaptureTests.Backend.Png, "image/png", 1, 1, [new(frames[0].Frame.Index, frames[0].Frame.TimeMs, new(0, 0, 1, 1))]);
        }
    }
    [Fact] public async Task RevokeDuringContactSheetRenderingCannotDiscloseOldImages()
    {
        using var session = Session(); session.Observation.StartRecording(); await WaitFrames(session, 2);
        session.Observation.StopRecording(); await session.Observation.Completion;
        var renderer = new SheetRenderer(); using var boundary = Boundary(session, renderer); using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        var task = boundary.InvokeAsync(grant, "capture.contactSheet.get", Empty, null);
        await renderer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.SetSharing(false); renderer.Release.SetResult();
        var result = await task; Assert.True(result.IsError); Assert.Null(result.Image);
    }
}
