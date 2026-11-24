using System.Runtime.InteropServices;
using SnapBar.Core;

namespace SnapBar.Windows;

public static class WindowDisplay
{
    private delegate bool MonitorCallback(nint monitor, nint dc, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool GetTitleBarInfo(nint window, ref TitleBarInfo info);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [StructLayout(LayoutKind.Sequential)]
    private struct TitleBarInfo
    {
        public int Size;
        public Native.Rect Bounds;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public uint[] States;
    }

    public static IReadOnlyList<ScreenRect> WorkAreas()
    {
        var areas = new List<ScreenRect>();
        var complete = true;
        var success = EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            if (!Native.GetMonitorInfo(monitor, ref info)) { complete = false; return false; }
            var work = info.Work;
            if (work.Right <= work.Left || work.Bottom <= work.Top) { complete = false; return false; }
            areas.Add(new(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top));
            return true;
        }, 0);
        return success && complete ? areas : [];
    }

    public static ScreenRect? DraggableCaption(nint window)
    {
        var info = new TitleBarInfo { Size = Marshal.SizeOf<TitleBarInfo>(), States = new uint[6] };
        if (!GetTitleBarInfo(window, ref info) || (info.States[0] & 0x8000) != 0) return null;
        var rect = info.Bounds;
        var style = (long)Native.GetWindowLongPtr(window, -16);
        var button = GetSystemMetricsForDpi(30, Math.Max(96, Native.GetDpiForWindow(window)));
        // Reserve the system menu and caption buttons rather than counting them as a drag target.
        var hasMenu = (style & 0x80000) != 0;
        var left = rect.Left + (hasMenu ? button : 0);
        var right = rect.Right - (hasMenu ? button * (1 + ((style & 0x20000) != 0 ? 1 : 0)
            + ((style & 0x10000) != 0 ? 1 : 0)) : 0);
        return right > left && rect.Bottom > rect.Top
            ? new(left, rect.Top, right - left, rect.Bottom - rect.Top) : null;
    }
}
