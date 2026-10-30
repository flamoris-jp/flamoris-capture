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

/// <summary>Explicit, independently revocable observation of the host window.</summary>
public sealed class EmbeddedWindowCapture : IDisposable
{
    private readonly object connectionGate = new();
    private readonly Window owner;
    private readonly CaptureSession session;
    private readonly CaptureMcpHost host;
    private readonly Func<ICaptureTarget> selectOwner;
    private readonly CheckBox sharing = new() { Content = "画面共有 OFF", IsEnabled = false,
        VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gainsboro,
        Margin = new Thickness(4, 1, 4, 1),
        ToolTip = "MCP接続とは別に、このアプリの画面共有を許可します。OFFにすると取得中の画像も共有しません。" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool disposed, connectionActive, updating;
    private long connectionEpoch;
    public FrameworkElement Control => sharing;

    public EmbeddedWindowCapture(Window owner)
        : this(owner, new CaptureSession(new WindowCaptureBackend()), () =>
        {
            var handle = new WindowInteropHelper(owner).Handle;
            if (handle == 0) throw new CaptureFault("target_lost");
            return WindowTarget.Select(new WindowChoice(handle, owner.Title));
        }) { }

    internal EmbeddedWindowCapture(Window owner, CaptureSession session, Func<ICaptureTarget> selectOwner)
    {
        this.owner = owner; this.session = session; this.selectOwner = selectOwner;
        host = new(session);
        session.Invalidating += Invalidated;
        sharing.Checked += ChangeSharing; sharing.Unchecked += ChangeSharing;
        timer.Tick += CheckTarget; timer.Start();
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        owner.Closed += OwnerClosed;
        owner.Dispatcher.ShutdownStarted += DispatcherShutdown;
    }

    // A connection only enables the consent control. It never selects/captures the window.
    public IDisposable BeginConnection()
    {
        owner.Dispatcher.VerifyAccess();
        lock (connectionGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            session.Stop(); connectionActive = true;
            long epoch = ++connectionEpoch;
            Refresh();
            return new ConnectionLease(() => EndConnection(epoch));
        }
    }
    private void EndConnection(long epoch)
    {
        lock (connectionGate)
        {
            if (disposed || epoch != connectionEpoch) return;
            connectionEpoch++; connectionActive = false; session.Stop();
        }
        Post(Refresh);
    }
    private sealed class ConnectionLease(Action stop) : IDisposable
    {
        private Action? stop = stop;
        public void Dispose() => Interlocked.Exchange(ref stop, null)?.Invoke();
    }
    private void ChangeSharing(object sender, RoutedEventArgs e)
    {
        if (updating || disposed) return;
        bool enabled = sharing.IsChecked == true;
        try
        {
            if (enabled && !new PrivacyNotice().Confirm(owner)) enabled = false;
            SetSharing(enabled);
        }
        catch { Stop(); }
    }
    internal void SetSharing(bool enabled)
    {
        owner.Dispatcher.VerifyAccess();
        lock (connectionGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (enabled)
            {
                if (!connectionActive) throw new CaptureFault("forbidden");
                try { session.SelectAndAllow(selectOwner()); session.SetSharing(true); }
                catch { session.Stop(); throw; }
            }
            else session.Stop();
            Refresh();
        }
    }
    public IEnumerable<HostTool> Tools() => host.ScreenshotTools();
    private void Invalidated() => Post(Refresh);
    private void Post(Action action)
    {
        if (owner.Dispatcher.CheckAccess()) action();
        else if (!owner.Dispatcher.HasShutdownStarted) _ = owner.Dispatcher.BeginInvoke(action);
    }
    private void Refresh()
    {
        if (disposed) return;
        bool enabled = session.Info.SharingAllowed;
        updating = true;
        try
        {
            sharing.IsChecked = enabled;
            sharing.IsEnabled = connectionActive;
            sharing.Content = enabled ? "画面共有 ON" : "画面共有 OFF";
            sharing.Background = enabled ? Brushes.Goldenrod : Brushes.Transparent;
            sharing.Foreground = enabled ? Brushes.Black : Brushes.Gainsboro;
        }
        finally { updating = false; }
    }
    private void CheckTarget(object? sender, EventArgs e) => session.CheckTarget();
    public void Stop()
    {
        lock (connectionGate) { if (disposed) return; session.Stop(); }
        Post(Refresh);
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
            connectionActive = false; session.Stop(); disposed = true;
            session.Invalidating -= Invalidated; session.Dispose();
        }
        timer.Stop(); timer.Tick -= CheckTarget;
        sharing.Checked -= ChangeSharing; sharing.Unchecked -= ChangeSharing;
        SystemEvents.SessionSwitch -= SessionSwitch;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        owner.Closed -= OwnerClosed;
        owner.Dispatcher.ShutdownStarted -= DispatcherShutdown;
    }
}
