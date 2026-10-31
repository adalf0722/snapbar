using System.Diagnostics;
using System.Runtime.InteropServices;
using SnapBar.Core;

namespace SnapBar.Windows;

public static class Native
{
    public const long InputMarker = 0x534E4150;
    public const int WM_HOTKEY = 0x0312, WM_MOUSEACTIVATE = 0x21;
    public delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseHookData { public Point Point; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyboardHookData { public uint Key, Scan, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }

    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, System.Text.StringBuilder value, int length);
    public static string GetWindowClass(nint window)
    {
        var value = new System.Text.StringBuilder(256);
        return GetClassName(window, value, value.Capacity) > 0 ? value.ToString() : "";
    }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();

    public static TargetIdentity? GetIdentity(nint window)
    {
        if (window == 0 || !IsWindow(window)) return null;
        try
        {
            GetWindowThreadProcessId(window, out var pid);
            using var process = Process.GetProcessById((int)pid);
            return new((long)window, (int)pid, process.StartTime.ToUniversalTime().Ticks);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    public static string? GetExecutablePath(TargetIdentity target)
    {
        try
        {
            using var process = Process.GetProcessById(target.ProcessId);
            return process.StartTime.ToUniversalTime().Ticks == target.ProcessStarted
                ? process.MainModule?.FileName : null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    public static bool AnyModifierDown() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);

    public static bool SendControlKey(ushort key)
        => SendChord(key, [0x11]);

    public static bool SendSublimeCopy() => SendChord(0x7B, [0x11, 0x12, 0x10]);
    public static bool SendSublimeEditableCopy() => SendChord(0x7A, [0x11, 0x12, 0x10]);
    public static bool SendSublimeAction(TextAction action) => action switch
    {
        TextAction.Cut => SendChord(0x79, [0x11, 0x12, 0x10]),
        TextAction.Paste => SendChord(0x78, [0x11, 0x12, 0x10]),
        _ => false
    };

    private static bool SendChord(ushort key, ushort[] modifiers)
    {
        if (AnyModifierDown()) return false;
        Input K(ushort k, bool up = false) => new()
        {
            Type = 1,
            Union = new() { Keyboard = new() { Key = k, Flags = up ? 2u : 0u, ExtraInfo = (nuint)InputMarker } }
        };
        var inputs = modifiers.Select(k => K(k)).Concat([K(key), K(key, true)])
            .Concat(modifiers.Reverse().Select(k => K(k, true))).ToArray();
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            // A partial insertion must not leave our synthetic Ctrl/key held down.
            var releases = new[] { K(key, true) }.Concat(modifiers.Reverse().Select(k => K(k, true))).ToArray();
            SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
        }
        return sent == inputs.Length;
    }

    public static ScreenRect WorkArea(int x, int y)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromPoint(new() { X = x, Y = y }, 2);
        if (!GetMonitorInfo(monitor, ref info)) return new(0, 0, 1920, 1080);
        return new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }
}
