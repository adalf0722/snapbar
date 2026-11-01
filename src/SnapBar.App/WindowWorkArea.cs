using System.Windows;
using System.Windows.Interop;
using System.Runtime.CompilerServices;
using SnapBar.Windows;

namespace SnapBar.App;

internal static class WindowWorkArea
{
    private sealed class Constraint
    {
        internal Rect? Override;
        internal Action Apply = () => { };
    }
    private static readonly ConditionalWeakTable<Window, Constraint> constraints = new();
    internal static void SetWorkAreaOverride(Window window, Rect? workArea)
    {
        if (!constraints.TryGetValue(window, out var constraint)) return;
        constraint.Override = workArea; constraint.Apply();
    }
    internal static void Attach(Window window)
    {
        if (constraints.TryGetValue(window, out _)) return;
        var constraint = new Constraint(); constraints.Add(window, constraint);
        var minimumWidth = window.MinWidth;
        var minimumHeight = window.MinHeight;
        var adjusting = false;
        void Constrain()
        {
            if (adjusting || window.WindowState != WindowState.Normal) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == 0 || !Native.GetWindowRect(handle, out var rect)) return;
            adjusting = true;
            try
            {
                var dpi = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
                var work = constraint.Override is { } area
                    ? new SnapBar.Core.ScreenRect(area.X * dpi, area.Y * dpi, area.Width * dpi, area.Height * dpi)
                    : Native.WorkArea((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
                window.MinWidth = Math.Min(minimumWidth, work.Width / dpi);
                window.MinHeight = Math.Min(minimumHeight, work.Height / dpi);
                var width = Math.Min(rect.Right - rect.Left, (int)work.Width);
                var height = Math.Min(rect.Bottom - rect.Top, (int)work.Height);
                var x = (int)Math.Clamp(rect.Left, work.X, Math.Max(work.X, work.Right - width));
                var y = (int)Math.Clamp(rect.Top, work.Y, Math.Max(work.Y, work.Bottom - height));
                if (x != rect.Left || y != rect.Top || width != rect.Right - rect.Left || height != rect.Bottom - rect.Top)
                    Native.SetWindowPos(handle, 0, x, y, width, height, 0x0014);
            }
            finally { adjusting = false; }
        }
        constraint.Apply = Constrain;
        window.SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message is 0x02E0 or 0x007E or 0x001A) window.Dispatcher.BeginInvoke(Constrain);
                return 0;
            });
            window.Dispatcher.BeginInvoke(Constrain);
        };
        window.Loaded += (_, _) => Constrain();
        window.LocationChanged += (_, _) => Constrain();
        window.SizeChanged += (_, _) => Constrain();
    }
}
