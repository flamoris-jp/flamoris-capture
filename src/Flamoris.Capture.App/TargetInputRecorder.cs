using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using Flamoris.Capture.Core;

namespace Flamoris.Capture.App;

// Raw Input is registered only while the visible human checkbox is ON.
// Filter to the approved foreground root window BEFORE reading the device packet.
// This never injects input, translates text, reads the clipboard, or records another app.
internal sealed class TargetInputRecorder(HwndSource source, ObservationWorkspace trace) : IDisposable
{
    private nint target;
    private long lastMove;
    private bool active;
    private const int WmInput = 0xff;
    public void Start(nint window)
    {
        Stop(); target = window;
        source.AddHook(Receive);
        RawDevice[] devices = [new(1, 2, 0x100, source.Handle), new(1, 6, 0x100, source.Handle)];
        if (!RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<RawDevice>()))
        { source.RemoveHook(Receive); target = 0; throw new Win32Exception(Marshal.GetLastWin32Error()); }
        active = true;
    }
    public void Stop()
    {
        if (!active) return;
        // Removing uses hwndTarget=NULL, as required by RegisterRawInputDevices.
        RawDevice[] devices = [new(1, 2, 1, 0), new(1, 6, 1, 0)];
        RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<RawDevice>());
        active = false; target = 0; source.RemoveHook(Receive);
    }
    private nint Receive(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (!active || message != WmInput || !trace.IsRecordingOperations || GetAncestor(GetForegroundWindow(), 2) != target) return 0;
        uint size = 0, headerSize = (uint)Marshal.SizeOf<RawHeader>();
        if (GetRawInputData(lParam, 0x10000003, 0, ref size, headerSize) != 0 || size < headerSize || size > 128) return 0;
        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, 0x10000003, buffer, ref size, headerSize) != size) return 0;
            var header = Marshal.PtrToStructure<RawHeader>(buffer);
            var data = buffer + (int)headerSize;
            if (header.Type == 1 && size >= headerSize + Marshal.SizeOf<RawKeyboard>())
            {
                var key = Marshal.PtrToStructure<RawKeyboard>(data);
                if (key.VirtualKey == 255 || IsEditableOrProtected()) return 0;
                string name = KeyName(key);
                trace.AddEvent((key.Flags & 1) == 0 ? "keyDown" : "keyUp", name);
            }
            else if (header.Type == 0 && size >= headerSize + Marshal.SizeOf<RawMouse>() && GetCursorPos(out var point)
                && GetAncestor(WindowFromPoint(point), 2) == target && DwmGetWindowAttribute(target, 9, out var bounds, Marshal.SizeOf<Rect>()) == 0)
            {
                var mouse = Marshal.PtrToStructure<RawMouse>(data);
                int x = point.X - bounds.Left, y = point.Y - bounds.Top;
                string[] buttons = ["Left", "Right", "Middle", "X1", "X2"];
                for (int i = 0; i < buttons.Length; i++)
                {
                    if ((mouse.ButtonFlags & (1 << (i * 2))) != 0) trace.AddEvent("mouseDown", buttons[i], x, y);
                    if ((mouse.ButtonFlags & (2 << (i * 2))) != 0) trace.AddEvent("mouseUp", buttons[i], x, y);
                }
                if ((mouse.ButtonFlags & 0x400) != 0) trace.AddEvent("wheel", x: x, y: y, delta: (short)mouse.ButtonData);
                if ((mouse.ButtonFlags & 0x800) != 0) trace.AddEvent("horizontalWheel", x: x, y: y, delta: (short)mouse.ButtonData);
                long now = ObservationClock.Ticks;
                if ((mouse.LastX != 0 || mouse.LastY != 0) && now - lastMove >= TimeSpan.TicksPerMillisecond * 33)
                { lastMove = now; trace.AddEvent("mouseMove", x: x, y: y); }
            }
        }
        catch { /* A disappearing target/automation provider must never break Windows message processing. */ }
        finally { Marshal.FreeHGlobal(buffer); }
        // Let WPF/DefWindowProc perform WM_INPUT cleanup; do not mark the message handled.
        return 0;
    }
    private static bool IsEditableOrProtected()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return false;
            return focused.Current.IsPassword || focused.Current.ControlType == ControlType.Edit
                || focused.TryGetCurrentPattern(TextPattern.Pattern, out _)
                || focused.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && value is ValuePattern p && !p.Current.IsReadOnly;
        }
        catch (ElementNotAvailableException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (COMException) { return true; }
    }
    private static string KeyName(RawKeyboard k)
    {
        bool extended = (k.Flags & 2) != 0;
        if (!extended)
        {
            string? numpad = k.MakeCode switch { 0x52 => "NumPad0", 0x4f => "NumPad1", 0x50 => "NumPad2", 0x51 => "NumPad3",
                0x4b => "NumPad4", 0x4c => "NumPad5", 0x4d => "NumPad6", 0x47 => "NumPad7", 0x48 => "NumPad8", 0x49 => "NumPad9", 0x53 => "Decimal", _ => null };
            if (numpad is not null) return numpad;
        }
        return k.VirtualKey switch { 0x10 => k.MakeCode == 0x36 ? "RightShift" : "LeftShift",
            0x11 => extended ? "RightCtrl" : "LeftCtrl", 0x12 => extended ? "RightAlt" : "LeftAlt",
            0x0d when extended => "NumPadEnter", _ => KeyInterop.KeyFromVirtualKey(k.VirtualKey).ToString() };
    }
    public void Dispose() => Stop();
    [StructLayout(LayoutKind.Sequential)] private readonly record struct RawDevice(ushort UsagePage, ushort Usage, uint Flags, nint Target);
    [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type, Size; public nint Device, WParam; }
    [StructLayout(LayoutKind.Sequential)] private struct RawKeyboard { public ushort MakeCode, Flags, Reserved, VirtualKey; public uint Message, Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct RawMouse
    { [FieldOffset(4)] public ushort ButtonFlags; [FieldOffset(6)] public ushort ButtonData; [FieldOffset(12)] public int LastX; [FieldOffset(16)] public int LastY; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(nint input, uint command, nint buffer, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out Rect rect, int size);
}
