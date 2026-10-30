using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Flamoris.Capture.Core;
using Flamoris.Capture.Wpf;
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
    private readonly PrivacyNotice notice = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private TargetInputRecorder? input;
    private WindowChoice? selectedChoice;
    private bool closing, previewUpdating, exporting, viewingRecording, playing, updatingControls;
    private long playbackOrigin, nextPreview;
    private double playbackOffset;
    private Task? exportTask;
    private readonly CancellationTokenSource shutdown = new();
    public MainWindow()
    {
        InitializeComponent(); host = new(session, new ContactSheetRenderer());
        string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLAMORIS", "Capture", "mcp-settings.json");
        mcp = new(this, McpMenu, "flamoris.capture", settings, "FLAMORIS/Capture",
            Path.Combine(AppContext.BaseDirectory, "Flamoris.Capture.Bridge.exe"), Attach,
            () => !closing);
        McpIndicator.Content = mcp.StatusIndicator;
        ShowNoticeSetting.IsChecked = !notice.Suppress;
        session.Invalidating += SessionInvalidated;
        Loaded += (_, _) =>
        {
            input = new(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!, session.Observation);
            ReloadWindows(); timer.Start();
        };
        timer.Tick += async (_, _) =>
        {
            session.CheckTarget();
            if (!session.Observation.IsRecordingOperations) input?.Stop();
            UpdateStatus();
            if (playing)
            {
                var frames = session.Observation.Frames(false);
                double time = playbackOffset + (ObservationClock.Ticks - playbackOrigin) / (double)TimeSpan.TicksPerMillisecond;
                var frame = frames.LastOrDefault(f => f.TimeMs <= time);
                if (frame is not null) { FrameSlider.Value = frame.Index; ShowRecordedFrame(frame.Index); }
                if (frames.Length == 0 || time >= frames[^1].TimeMs) { playing = false; PlayButton.Content = "再生"; }
            }
            else if (!viewingRecording && session.Info.SharingAllowed && !previewUpdating && !exporting)
            {
                if (session.Observation.IsRecording)
                {
                    if (session.Observation.Frames(false).Length > 0) ShowRecordedFrame(null);
                }
                else if (ObservationClock.Ticks >= nextPreview)
                { nextPreview = ObservationClock.Ticks + TimeSpan.TicksPerMillisecond * 500; await RefreshPreview(false); }
            }
        };
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        Closing += OnClosing;
        UpdateStatus();
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true; closing = true; IsEnabled = false; timer.Stop();
        input?.Dispose(); session.Stop(); shutdown.Cancel(); mcp.Shutdown();
        SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerModeChanged;
        session.Invalidating -= SessionInvalidated;
        try { await session.Observation.Completion; if (exportTask is not null) await exportTask; }
        catch { }
        session.Dispose(); shutdown.Dispose(); Close();
    }
    private async Task<McpDesktopAttachment> Attach(McpPermission permission)
    {
        if (permission != McpPermission.ReadOnly)
        {
            MessageBox.Show(this, "Captureは読み取り専用で接続します。\n接続確認画面で「読み取り専用」を選択してください。", "FLAMORIS Capture");
            throw new McpFault(McpErrors.Forbidden);
        }
        var boundary = new McpBoundary(host, host.Tools(), new McpOptions { ApplicationLifetime = true, RequestTimeoutMs = 5000, MaxConcurrentRequests = 1 }, new McpDiagnostics(FlamorisLogger.Create()));
        try { return new(boundary, await boundary.EnableAsync(McpPermission.ReadOnly)); }
        catch { boundary.Dispose(); throw; }
    }
    private void SessionInvalidated()
    {
        if (!closing) _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (closing) return;
            var info = session.Info;
            if (!info.SharingAllowed) { Preview.Source = null; viewingRecording = playing = false; }
            if (!info.CaptureAllowed) { TargetLabel.Text = "対象未選択"; selectedChoice = null; }
            updatingControls = true;
            Share.IsChecked = info.SharingAllowed;
            Operations.IsChecked = session.Observation.IsRecordingOperations;
            updatingControls = false;
            if (!session.Observation.IsRecordingOperations) input?.Stop();
            UpdateStatus();
        }));
    }
    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect) session.Stop();
    }
    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) session.Stop(); }
    private void ReloadWindows() => WindowsList.ItemsSource = WindowTarget.List(new WindowInteropHelper(this).Handle);
    private void RefreshWindows(object sender, RoutedEventArgs e) => ReloadWindows();
    private void SelectWindow(object sender, RoutedEventArgs e)
    {
        if (exporting || session.Observation.IsRecording) return;
        if (WindowsList.SelectedItem is not WindowChoice choice) return;
        WindowTarget? selected = null;
        try
        {
            selected = WindowTarget.Select(choice);
            string name = selected.DisplayName;
            input?.Stop(); session.SelectAndAllow(selected); selected = null; selectedChoice = choice;
            updatingControls = true; Share.IsChecked = Operations.IsChecked = false; updatingControls = false;
            Preview.Source = null; TargetLabel.Text = name; viewingRecording = playing = false; UpdateStatus();
        }
        catch (CaptureFault fault) { ShowError(fault.Code); }
        finally { selected?.Dispose(); }
    }
    private void ChangeSharing(object sender, RoutedEventArgs e)
    {
        if (closing || updatingControls || mcp is null) return;
        try
        {
            if (Share.IsChecked == true && !notice.Confirm(this)) { Share.IsChecked = false; return; }
            ShowNoticeSetting.IsChecked = !notice.Suppress;
            // SetSharing revokes/cancels synchronously before UI refresh.
            session.SetSharing(Share.IsChecked == true);
            Preview.Source = null; viewingRecording = playing = false;
        }
        catch (Exception fault) { Share.IsChecked = false; ShowError(fault is CaptureFault f ? f.Code : "settings_failed"); }
        UpdateStatus();
    }
    private void ChangeOperations(object sender, RoutedEventArgs e)
    {
        if (closing || updatingControls || input is null) return;
        try
        {
            if (Operations.IsChecked == true)
            {
                if (selectedChoice is null) throw new CaptureFault("target_lost");
                if (!notice.Confirm(this)) { Operations.IsChecked = false; return; }
                session.Observation.SetOperations(true);
                input.Start(selectedChoice.Handle);
            }
            else { input.Stop(); session.Observation.SetOperations(false); }
            ShowNoticeSetting.IsChecked = !notice.Suppress;
        }
        catch (Exception fault)
        {
            input.Stop(); session.Observation.SetOperations(false); Operations.IsChecked = false;
            ShowError(fault is CaptureFault f ? f.Code : "input_unavailable");
        }
        UpdateStatus();
    }
    private void ChangeNoticeSetting(object sender, RoutedEventArgs e)
    {
        try { notice.SetDisplay(ShowNoticeSetting.IsChecked); }
        catch { ShowError("settings_failed"); }
    }
    private async void CaptureLocal(object sender, RoutedEventArgs e) { viewingRecording = playing = false; await RefreshPreview(true); }
    private async Task RefreshPreview(bool report)
    {
        if (previewUpdating || !session.Info.SharingAllowed || session.Info.Busy) return;
        previewUpdating = true;
        try
        {
            string expected = session.ResolveSession();
            var result = await session.CaptureAsync(new() { SessionId = expected }, false);
            session.Demand(expected, true); ShowImage(result.Image.Bytes);
        }
        catch (CaptureFault fault) { if (report) ShowError(fault.Code); }
        catch (OperationCanceledException) { }
        catch { if (report) ShowError("frame_unavailable"); }
        finally { previewUpdating = false; UpdateStatus(); }
    }
    private void ShowImage(byte[] bytes)
    {
        if (closing) return;
        var bitmap = new BitmapImage(); using var stream = new MemoryStream(bytes);
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Preview.Source = bitmap;
    }
    private void StartRecording(object sender, RoutedEventArgs e)
    {
        try { viewingRecording = playing = false; session.Observation.StartRecording(); UpdateStatus(); }
        catch (CaptureFault fault) { ShowError(fault.Code); }
        catch { ShowError("recording_failed"); }
    }
    private async void StopRecording(object sender, RoutedEventArgs e)
    { session.Observation.StopRecording(); await session.Observation.Completion; viewingRecording = true; RefreshRecordingControls(); ShowRecordedFrame(null); UpdateStatus(); }
    private void RefreshRecordingControls()
    {
        var frames = session.Observation.Frames(false);
        FrameSlider.Maximum = Math.Max(0, frames.Length - 1);
        FrameSlider.IsEnabled = frames.Length > 0 && !session.Observation.IsRecording;
    }
    private void ViewRecording(object sender, RoutedEventArgs e)
    { viewingRecording = true; playing = false; RefreshRecordingControls(); ShowRecordedFrame((int)FrameSlider.Value); }
    private void SelectFrame(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (viewingRecording) ShowRecordedFrame((int)e.NewValue); }
    private void ShowRecordedFrame(int? index)
    {
        try
        {
            var frame = session.Observation.ReadFrame(index, disclosure: false); ShowImage(frame.Bytes);
            FrameLabel.Text = $"{frame.Frame.TimeMs / 1000:F2} 秒 / コマ {frame.Frame.Index}";
        }
        catch (CaptureFault) { }
        catch (IOException) { ShowError("frame_unavailable"); }
    }
    private void PlayRecording(object sender, RoutedEventArgs e)
    {
        if (session.Observation.IsRecording) return;
        var frames = session.Observation.Frames(false); if (frames.Length == 0) return;
        viewingRecording = true; playing = !playing;
        if (playing)
        {
            int index = (int)FrameSlider.Value;
            if (index >= frames.Length - 1) { index = 0; FrameSlider.Value = 0; }
            playbackOffset = frames[index].TimeMs; playbackOrigin = ObservationClock.Ticks;
        }
        PlayButton.Content = playing ? "一時停止" : "再生";
    }
    private async void SaveVideo(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "MP4動画（音声なし）|*.mp4", FileName = "Capture.mp4" };
        if (dialog.ShowDialog(this) != true) return;
        exporting = true; UpdateStatus();
        try
        {
            exportTask = VideoExporter.SaveMp4Async(session.Observation, dialog.FileName, shutdown.Token);
            await exportTask;
            if (!closing) MessageBox.Show(this, "MP4を保存しました。", "FLAMORIS Capture");
        }
        catch (OperationCanceledException) { }
        catch (Exception fault) { ShowError(fault is CaptureFault f ? f.Code : "encoding_failed"); }
        finally { exporting = false; UpdateStatus(); }
    }
    private void SaveEvents(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "操作履歴・コマ時刻 (JSON)|*.json", FileName = "Capture-events.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { session.Observation.SaveEvents(dialog.FileName); }
        catch { ShowError("save_failed"); }
    }
    private void StopCapture(object sender, RoutedEventArgs e)
    {
        input?.Stop(); session.Stop(); updatingControls = true; Share.IsChecked = Operations.IsChecked = false; updatingControls = false;
        Preview.Source = null; TargetLabel.Text = "対象未選択"; viewingRecording = playing = false; UpdateStatus();
    }
    private void ShowError(string code) { if (!closing) MessageBox.Show(this, "実行できませんでした: " + code, "FLAMORIS Capture"); }
    private void UpdateStatus()
    {
        if (closing || mcp is null) return;
        var info = session.Info; var recording = session.Observation.Summary(false);
        CaptureButton.IsEnabled = info.SharingAllowed && !info.Busy && !recording.Recording && !exporting;
        Share.IsEnabled = Operations.IsEnabled = info.CaptureAllowed;
        WindowsList.IsEnabled = !recording.Recording && !exporting;
        RecordButton.IsEnabled = info.SharingAllowed && !recording.Recording && !info.Busy && !exporting;
        StopRecordButton.IsEnabled = recording.Recording;
        SaveVideoButton.IsEnabled = recording.FrameCount > 0 && !recording.Recording && !exporting;
        SaveEventsButton.IsEnabled = recording.EventCount > 0 || recording.FrameCount > 0;
        PlayButton.IsEnabled = recording.FrameCount > 0 && !recording.Recording;
        OperationBadge.Visibility = recording.RecordingOperations ? Visibility.Visible : Visibility.Collapsed;
        RecordBadge.Visibility = recording.Recording ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = exporting ? "MP4を保存中…" : $"画面共有 {(info.SharingAllowed ? "ON" : "OFF")} / 操作記録 {(recording.RecordingOperations ? "ON" : "OFF")} / {recording.FrameCount} コマ・{recording.DurationMs / 1000:F1} 秒";
        if (recording.StopReason is not null and not "stopped") Status.Text += " / 録画停止: " + recording.StopReason;
        if (!recording.Recording) RefreshRecordingControls();
        mcp.Refresh();
    }
}
