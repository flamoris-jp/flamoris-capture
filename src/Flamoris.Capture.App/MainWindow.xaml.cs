using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Flamoris.Capture.Core;
using Flamoris.Capture.Mcp;
using Flamoris.Capture.Windows;
using Flamoris.Logging;
using Flamoris.Mcp.Core;
using Flamoris.Mcp.Wpf;
using Microsoft.Win32;

namespace Flamoris.Capture.App;

public partial class MainWindow : Window
{
    private readonly CaptureSession session = new(new WindowCaptureBackend());
    private readonly CaptureMcpHost host;
    private readonly McpDesktopUi mcp;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool closing;
    public MainWindow()
    {
        InitializeComponent(); host = new(session);
        string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLAMORIS", "Capture", "mcp-settings.json");
        mcp = new(this, McpMenu, "flamoris.capture", settings, "FLAMORIS/Capture",
            Path.Combine(AppContext.BaseDirectory, "Flamoris.Capture.Bridge.exe"), Attach,
            () => session.Info is { CaptureAllowed: true, SharingAllowed: true });
        McpIndicator.Content = mcp.StatusIndicator;
        session.Invalidating += SessionInvalidated;
        Loaded += (_, _) => { ReloadWindows(); timer.Start(); };
        timer.Tick += (_, _) =>
        {
            // Detect target loss while idle as well as during acquisition.
            session.CheckTarget();
            UpdateStatus();
        };
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        Closed += (_, _) =>
        {
            closing = true; timer.Stop();
            SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerModeChanged;
            session.Invalidating -= SessionInvalidated; mcp.Shutdown(); session.Dispose();
        };
        UpdateStatus();
    }
    private async Task<McpDesktopAttachment> Attach(McpPermission permission)
    {
        if (permission != McpPermission.ReadOnly) throw new McpFault(McpErrors.Forbidden);
        var boundary = new McpBoundary(host, host.Tools(), new McpOptions { RequestTimeoutMs = 5000, MaxConcurrentRequests = 1 },
            new McpDiagnostics(FlamorisLogger.Create()));
        try { return new(boundary, await boundary.EnableAsync(McpPermission.ReadOnly)); }
        catch { boundary.Dispose(); throw; }
    }
    private void SessionInvalidated()
    {
        // Core revocation is synchronous through IMcpHost.Invalidating, even from an OS callback.
        if (!closing) _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (closing) return;
            Preview.Source = null;
            Share.IsChecked = session.Info.SharingAllowed;
            if (!session.Info.CaptureAllowed) TargetLabel.Text = "対象未選択";
            mcp.Invalidate(); UpdateStatus();
        }));
    }
    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            session.Stop();
    }
    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Suspend) session.Stop(); }
    private void ReloadWindows() => WindowsList.ItemsSource = WindowTarget.List(new WindowInteropHelper(this).Handle);
    private void RefreshWindows(object sender, RoutedEventArgs e) => ReloadWindows();
    private void SelectWindow(object sender, RoutedEventArgs e)
    {
        if (WindowsList.SelectedItem is not WindowChoice choice) return;
        WindowTarget? selected = null;
        try
        {
            selected = WindowTarget.Select(choice);
            if (MessageBox.Show(this, "選択した1ウィンドウの静止画取得を許可しますか？\n" + selected.DisplayName,
                "撮影対象の確認", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            string name = selected.DisplayName;
            session.SelectAndAllow(selected); selected = null;
            Share.IsChecked = false; Preview.Source = null; TargetLabel.Text = name;
            UpdateStatus();
        }
        catch (CaptureFault fault) { ShowError(fault.Code); }
        finally { selected?.Dispose(); }
    }
    private void ChangeSharing(object sender, RoutedEventArgs e)
    {
        if (closing) return;
        try { session.SetSharing(Share.IsChecked == true); }
        catch (CaptureFault fault) { Share.IsChecked = false; ShowError(fault.Code); }
        UpdateStatus();
    }
    private async void CaptureLocal(object sender, RoutedEventArgs e)
    {
        try
        {
            CaptureButton.IsEnabled = false; Status.Text = "静止画を取得中…";
            string expected = session.Info.SessionId;
            var result = await session.CaptureAsync(new() { SessionId = expected }, false);
            session.Demand(expected, false);
            var bitmap = new BitmapImage(); using var stream = new MemoryStream(result.Image.Bytes);
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            Preview.Source = bitmap;
        }
        catch (CaptureFault fault) { ShowError(fault.Code); }
        catch (OperationCanceledException) { ShowError("cancelled"); }
        catch { ShowError("frame_unavailable"); }
        finally { UpdateStatus(); }
    }
    private void StopCapture(object sender, RoutedEventArgs e) { session.Stop(); Share.IsChecked = false; Preview.Source = null; TargetLabel.Text = "対象未選択"; UpdateStatus(); }
    private void ShowError(string code)
    { if (!closing) MessageBox.Show(this, "取得できませんでした: " + code, "FLAMORIS Capture"); }
    private void UpdateStatus()
    {
        if (closing || mcp is null) return;
        var info = session.Info;
        CaptureButton.IsEnabled = info.CaptureAllowed && !info.Busy;
        Share.IsEnabled = info.CaptureAllowed;
        Status.Text = $"撮影 {(info.CaptureAllowed ? "許可済み" : "OFF")} / MCP共有 {(info.SharingAllowed ? "許可済み" : "OFF")} / {(info.Busy ? "取得中" : "待機")} — 常時録画は行いません";
        mcp.Refresh();
    }
}
