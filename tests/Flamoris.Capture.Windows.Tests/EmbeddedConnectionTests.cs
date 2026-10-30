using System.Windows;
using Flamoris.Capture.Core;
using Flamoris.Capture.Wpf;
using Xunit;

namespace Flamoris.Capture.Windows.Tests;

public sealed class EmbeddedConnectionTests
{
    private sealed class Target : ICaptureTarget
    {
        public bool IsAlive { get; private set; } = true;
        public event Action? Lost { add { } remove { } }
        public void Dispose() => IsAlive = false;
    }
    private sealed class Backend : ICaptureBackend
    {
        public Func<Task>? Wait { get; set; }
        public async Task<EncodedScreenshot> CaptureAsync(ICaptureTarget target, ScreenshotRequest request, CancellationToken token)
        {
            if (Wait is not null) await Wait();
            return new([1], "image/png", 1, 1, new(0, 0, 1, 1), null,
                new(1, 1, 0, 0, 96, "synthetic"), 0, DateTimeOffset.UtcNow);
        }
    }
    private static async Task Sta(Action test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { test(); done.SetResult(); } catch (Exception e) { done.SetException(e); } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public Task McpAttachmentAndScreenConsentAreIndependent() => Sta(() =>
    {
        var session = new CaptureSession(new Backend()); int selected = 0, revoked = 0;
        using var capture = new EmbeddedWindowCapture(new Window(), session, () => { selected++; return new Target(); });
        Assert.False(session.Info.SharingAllowed); Assert.Equal(0, selected);
        Assert.IsType<System.Windows.Controls.CheckBox>(capture.Control);
        using var connection = capture.BeginConnection();
        Assert.False(session.Info.SharingAllowed); Assert.Equal(0, selected);
        capture.SetSharing(true);
        Assert.True(session.Info.SharingAllowed); Assert.Equal(1, selected); Assert.Equal(0, revoked);
        Assert.Single(session.CaptureAsync(new(), true).GetAwaiter().GetResult().Image.Bytes);
        // StopHost cleanup can run off the dispatcher after provider disposal.
        Task.Run(connection.Dispose).GetAwaiter().GetResult();
        Assert.False(session.Info.SharingAllowed); Assert.Equal(0, revoked);
    });

    [Fact]
    public Task StaleLeaseCannotStopNewAttachmentAndSharingStopKeepsConnection() => Sta(() =>
    {
        var session = new CaptureSession(new Backend()); int revoked = 0;
        using var capture = new EmbeddedWindowCapture(new Window(), session, () => new Target());
        using var old = capture.BeginConnection();
        using var current = capture.BeginConnection();
        capture.SetSharing(true);
        old.Dispose(); Assert.True(session.Info.SharingAllowed); Assert.Equal(0, revoked);
        capture.Stop(); Assert.False(session.Info.SharingAllowed); Assert.Equal(0, revoked);
        current.Dispose(); Assert.Equal(0, revoked);
    });

    [Fact]
    public Task FailedConnectionAndPendingShotDoNotLeaveImagesAvailable() => Sta(() =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { Wait = () => { entered.SetResult(); return release.Task; } };
        var session = new CaptureSession(backend); bool fail = false;
        using var capture = new EmbeddedWindowCapture(new Window(), session,
            () => fail ? throw new CaptureFault("target_lost") : new Target());
        using var connection = capture.BeginConnection();
        capture.SetSharing(true);
        var shot = session.CaptureAsync(new(), true);
        entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        connection.Dispose(); release.SetResult();
        Assert.Throws<CaptureFault>(() => shot.GetAwaiter().GetResult());
        fail = true;
        using var retry = capture.BeginConnection();
        Assert.Throws<CaptureFault>(() => capture.SetSharing(true));
        Assert.False(session.Info.SharingAllowed);
    });
}
