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

/// <summary>Opt-in observation of one host window through its existing MCP boundary.</summary>
public sealed class EmbeddedWindowCapture : IDisposable
{
    private readonly Window owner;
    private readonly Action revokeMcp;
    private readonly CaptureSession session = new(new WindowCaptureBackend());
    private readonly CaptureMcpHost host;
    private readonly PrivacyNotice notice;
    private readonly CheckBox share = new() { Content = "画面共有", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gainsboro,
        ToolTip = "このアプリの画面をAIに共有します。変更後はMCPを再接続してください。右クリックで確認メッセージを再表示できます。" };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool updating, disposed;

    public FrameworkElement Control => share;

    public EmbeddedWindowCapture(Window owner, string preferencePath, Action revokeMcp)
    {
        this.owner = owner; this.revokeMcp = revokeMcp;
        notice = new(preferencePath); host = new(session);
        var restore = new MenuItem { Header = "安全確認メッセージを再表示する" };
        restore.Click += (_, _) =>
        {
            try { notice.SetDisplay(true); }
            catch { MessageBox.Show(owner, "設定を保存できませんでした。", "FLAMORIS Capture"); }
        };
        share.ContextMenu = new ContextMenu(); share.ContextMenu.Items.Add(restore);
        share.Checked += ChangeSharing; share.Unchecked += ChangeSharing;
        session.Invalidating += Invalidated;
        timer.Tick += CheckTarget;
        timer.Start();
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        owner.Closed += OwnerClosed;
        owner.Dispatcher.ShutdownStarted += DispatcherShutdown;
    }

    // These tools use the editor's guard/transport and the SAME CaptureSession.
    // Capture performs its own final consent check before returning any pixels.
    public IEnumerable<HostTool> Tools() => host.ScreenshotTools();

    private void ChangeSharing(object sender, RoutedEventArgs e)
    {
        if (updating || disposed) return;
        try
        {
            if (share.IsChecked != true) { Stop(); return; }
            if (!notice.Confirm(owner)) { Refresh(); return; }
            var handle = new WindowInteropHelper(owner).Handle;
            if (handle == 0) throw new CaptureFault("target_lost");
            session.SelectAndAllow(WindowTarget.Select(new WindowChoice(handle, owner.Title)));
            session.SetSharing(true);
            Refresh();
        }
        catch (Exception)
        {
            Stop();
            MessageBox.Show(owner, "画面共有を開始できませんでした。Windowsの画面キャプチャ機能とウィンドウの状態を確認してください。", "FLAMORIS Capture");
        }
    }

    private void Invalidated()
    {
        // Revoke pixels synchronously inside Capture; marshal only UI/provider work.
        if (owner.Dispatcher.CheckAccess()) RefreshAndRevoke();
        else if (!owner.Dispatcher.HasShutdownStarted)
            _ = owner.Dispatcher.BeginInvoke(new Action(RefreshAndRevoke));
    }
    private void RefreshAndRevoke()
    {
        if (disposed) return;
        revokeMcp(); Refresh();
    }
    private void Refresh()
    {
        updating = true;
        try
        {
            bool enabled = session.Info.SharingAllowed;
            share.IsChecked = enabled;
            share.Content = enabled ? "画面共有中" : "画面共有";
            share.Background = enabled ? Brushes.Goldenrod : Brushes.Transparent;
            share.Foreground = enabled ? Brushes.Black : Brushes.Gainsboro;
        }
        finally { updating = false; }
    }
    private void CheckTarget(object? sender, EventArgs e) => session.CheckTarget();
    public void Stop() { if (!disposed) session.Stop(); }
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
        if (disposed) return;
        Stop(); disposed = true;
        timer.Stop(); timer.Tick -= CheckTarget;
        SystemEvents.SessionSwitch -= SessionSwitch;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        owner.Closed -= OwnerClosed;
        owner.Dispatcher.ShutdownStarted -= DispatcherShutdown;
        session.Invalidating -= Invalidated;
        share.Checked -= ChangeSharing; share.Unchecked -= ChangeSharing;
        session.Dispose();
    }
}
