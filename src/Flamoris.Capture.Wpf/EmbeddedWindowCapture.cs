using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Flamoris.Capture.Core;
using Flamoris.Capture.Mcp;
using Flamoris.Capture.Windows;
using Flamoris.Mcp.Core;
using Microsoft.Win32;

namespace Flamoris.Capture.Wpf;

/// <summary>Observation of the host window for the lifetime of its MCP attachment.</summary>
public sealed class EmbeddedWindowCapture : IDisposable
{
    private readonly object connectionGate = new();
    private readonly Window owner;
    private readonly Action revokeMcp;
    private readonly CaptureSession session;
    private readonly CaptureMcpHost host;
    private readonly Func<ICaptureTarget> selectOwner;
    private readonly TextBlock status = new() { Text = "画面共有なし", VerticalAlignment = VerticalAlignment.Center,
        Foreground = Brushes.Gainsboro, Padding = new Thickness(4, 1, 4, 1),
        ToolTip = "MCP接続中はこのアプリの画面をAIが確認できます。MCPを切断すると共有も停止します。" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool disposed;
    private int changingConnection;
    private long connectionEpoch;

    public FrameworkElement Control => status;

    public EmbeddedWindowCapture(Window owner, Action revokeMcp)
        : this(owner, revokeMcp, new CaptureSession(new WindowCaptureBackend()), () =>
        {
            var handle = new WindowInteropHelper(owner).Handle;
            if (handle == 0) throw new CaptureFault("target_lost");
            return WindowTarget.Select(new WindowChoice(handle, owner.Title));
        }) { }

    internal EmbeddedWindowCapture(Window owner, Action revokeMcp, CaptureSession session, Func<ICaptureTarget> selectOwner)
    {
        this.owner = owner; this.revokeMcp = revokeMcp; this.session = session; this.selectOwner = selectOwner;
        host = new(session);
        session.Invalidating += Invalidated;
        timer.Tick += CheckTarget;
        timer.Start();
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        owner.Closed += OwnerClosed;
        owner.Dispatcher.ShutdownStarted += DispatcherShutdown;
    }

    // The editor's normal connection permission includes observation of itself.
    // The lease must be released on detach, failed connection and cancelled attach.
    public IDisposable BeginConnection()
    {
        owner.Dispatcher.VerifyAccess();
        lock (connectionGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            long epoch = ++connectionEpoch;
            Interlocked.Increment(ref changingConnection);
            try
            {
                session.SelectAndAllow(selectOwner());
                session.SetSharing(true);
                Refresh();
                return new ConnectionLease(() => EndConnection(epoch));
            }
            catch { session.Stop(); Refresh(); throw; }
            finally { Interlocked.Decrement(ref changingConnection); }
        }
    }
    private void EndConnection(long epoch)
    {
        lock (connectionGate)
        {
            if (disposed || epoch != connectionEpoch) return;
            connectionEpoch++;
            Interlocked.Increment(ref changingConnection);
            try { session.Stop(); }
            finally { Interlocked.Decrement(ref changingConnection); }
        }
        Post(Refresh);
    }
    private sealed class ConnectionLease(Action stop) : IDisposable
    {
        private Action? stop = stop;
        public void Dispose() => Interlocked.Exchange(ref stop, null)?.Invoke();
    }

    public IEnumerable<HostTool> Tools() => host.ScreenshotTools();

    private void Invalidated()
    {
        // Normal attach/detach already belongs to the editor's connection lifecycle.
        if (Volatile.Read(ref changingConnection) != 0) return;
        Post(() => { if (!disposed) { revokeMcp(); Refresh(); } });
    }
    private void Post(Action action)
    {
        if (owner.Dispatcher.CheckAccess()) action();
        else if (!owner.Dispatcher.HasShutdownStarted)
            _ = owner.Dispatcher.BeginInvoke(action);
    }
    private void Refresh()
    {
        if (disposed) return;
        bool enabled = session.Info.SharingAllowed;
        status.Text = enabled ? "MCP画面共有中" : "画面共有なし";
        status.Background = enabled ? Brushes.Goldenrod : Brushes.Transparent;
        status.Foreground = enabled ? Brushes.Black : Brushes.Gainsboro;
    }
    private void CheckTarget(object? sender, EventArgs e) => session.CheckTarget();
    public void Stop()
    {
        lock (connectionGate)
        {
            if (disposed) return;
            connectionEpoch++;
            session.Stop();
        }
    }
    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect) Stop();
    }
    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Suspend) Stop(); }
    private void OwnerClosed(object? sender, EventArgs e) => Dispose();
    private void DispatcherShutdown(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        lock (connectionGate)
        {
            if (disposed) return;
            Stop(); disposed = true;
            session.Invalidating -= Invalidated;
            session.Dispose();
        }
        timer.Stop(); timer.Tick -= CheckTarget;
        SystemEvents.SessionSwitch -= SessionSwitch;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        owner.Closed -= OwnerClosed;
        owner.Dispatcher.ShutdownStarted -= DispatcherShutdown;
    }
}
