using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Flamoris.Capture.Core;
using Windows.Graphics.Capture;
using Windows.Graphics;

namespace Flamoris.Capture.Windows;

// Display names stay in the local UI; neither HWND nor window title is an MCP selector.
public sealed record WindowChoice(nint Handle, string Title)
{
    public override string ToString() => Title;
}
public sealed class WindowTarget : ICaptureTarget
{
    internal GraphicsCaptureItem Item { get; }
    internal SizeInt32 CaptureSize { get; private set; }
    internal void UpdateCaptureSize(SizeInt32 size)
    { ImagePolicy.ValidateSource(size.Width, size.Height); CaptureSize = size; }
    internal nint Handle { get; }
    public string DisplayName => Item.DisplayName;
    private readonly uint pid;
    private readonly long processStarted;
    private int closed;
    public event Action? Lost;
    private WindowTarget(nint hwnd)
    {
        Handle = hwnd;
        Native.GetWindowThreadProcessId(hwnd, out pid);
        using var process = Process.GetProcessById(checked((int)pid));
        processStarted = process.StartTime.ToUniversalTime().Ticks;
        Item = Native.CreateItem(hwnd); // Bind once; never silently retarget a recycled HWND.
        CaptureSize = Item.Size;
        Item.Closed += ItemClosed;
        if (!IsAlive) { Dispose(); throw new CaptureFault("target_lost"); }
    }
    public static WindowTarget Select(WindowChoice choice)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new CaptureFault("unsupported_capability");
        try { return new(choice.Handle); }
        catch (CaptureFault) { throw; }
        catch { throw new CaptureFault("target_lost"); }
    }
    public bool IsAlive
    {
        get
        {
            if (Volatile.Read(ref closed) != 0 || !Native.IsWindow(Handle)) return false;
            Native.GetWindowThreadProcessId(Handle, out uint currentPid);
            if (currentPid != pid) return false;
            try { using var p = Process.GetProcessById((int)pid); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == processStarted; }
            catch { return false; }
        }
    }
    internal ImageGeometry Geometry(int width, int height)
    {
        if (Native.DwmGetWindowAttribute(Handle, 9, out var bounds, Marshal.SizeOf<Native.Rect>()) != 0)
            throw new CaptureFault("frame_unavailable");
        uint dpi = Native.GetDpiForWindow(Handle);
        if (dpi == 0) throw new CaptureFault("frame_unavailable");
        return new(width, height, bounds.Left, bounds.Top, dpi, $"{width}x{height}@{bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}:{dpi}");
    }
    internal void DemandAvailable()
    {
        if (!IsAlive) throw new CaptureFault("target_lost");
        if (!Native.IsWindowVisible(Handle) || Native.IsIconic(Handle)) throw new CaptureFault("frame_unavailable");
    }
    private void ItemClosed(GraphicsCaptureItem sender, object args)
    {
        if (Interlocked.Exchange(ref closed, 1) == 0) Lost?.Invoke();
    }
    public void Dispose() { Interlocked.Exchange(ref closed, 1); Item.Closed -= ItemClosed; }
    public static IReadOnlyList<WindowChoice> List(nint exclude)
    {
        var items = new List<WindowChoice>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (hwnd == exclude || !Native.IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(512); Native.GetWindowText(hwnd, title, title.Capacity);
            if (title.Length > 0) items.Add(new(hwnd, title.ToString()));
            return true;
        }, 0);
        return items.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
