using System.Windows;
using System.Windows.Interop;
using System.Runtime.CompilerServices;
using SnapBar.Windows;
using SnapBar.Core;

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
    internal static Rect? GetWorkAreaOverride(Window window) => constraints.TryGetValue(window, out var constraint) ? constraint.Override : null;
    internal static void Attach(Window window)
    {
        if (constraints.TryGetValue(window, out _)) return;
        var constraint = new Constraint(); constraints.Add(window, constraint);
        var minimumWidth = window.MinWidth;
        var minimumHeight = window.MinHeight;
        var adjusting = false;
        var moving = false;
        var recoveryPending = false;
        var initialSizePending = true;
        var queued = false;
        var updatePending = false;
        var previousState = window.WindowState;
        void Apply()
        {
            if (adjusting || moving || !window.IsVisible || window.WindowState != WindowState.Normal) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == 0 || !Native.GetWindowRect(handle, out var rect)) return;
            updatePending = false;
            adjusting = true;
            try
            {
                var dpi = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
                var bounds = new ScreenRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                if (constraint.Override is { } area)
                {
                    var work = new ScreenRect(area.X * dpi, area.Y * dpi, area.Width * dpi, area.Height * dpi);
                    window.MinWidth = Math.Min(minimumWidth, work.Width / dpi);
                    window.MinHeight = Math.Min(minimumHeight, work.Height / dpi);
                    var width = Math.Min(bounds.Width, work.Width);
                    var height = Math.Min(bounds.Height, work.Height);
                    Move(bounds with { X = Math.Clamp(bounds.X, work.X, work.Right - width),
                        Y = Math.Clamp(bounds.Y, work.Y, work.Bottom - height), Width = width, Height = height });
                    return;
                }
                var areas = WindowDisplay.WorkAreas();
                if (areas.Count == 0) return;
                var nearest = areas.OrderBy(work =>
                {
                    var x = Math.Clamp(bounds.X + bounds.Width / 2, work.X, work.Right);
                    var y = Math.Clamp(bounds.Y + bounds.Height / 2, work.Y, work.Bottom);
                    return Math.Pow(x - bounds.X - bounds.Width / 2, 2) + Math.Pow(y - bounds.Y - bounds.Height / 2, 2);
                }).First();
                // Updating minima must not enlarge a window the user already resized.
                window.MinWidth = Math.Min(minimumWidth, Math.Min(bounds.Width, nearest.Width) / dpi);
                window.MinHeight = Math.Min(minimumHeight, Math.Min(bounds.Height, nearest.Height) / dpi);
                if (initialSizePending)
                {
                    bounds = bounds with { Width = Math.Min(bounds.Width, nearest.Width), Height = Math.Min(bounds.Height, nearest.Height) };
                    Move(bounds);
                    initialSizePending = false;
                }
                if (recoveryPending)
                {
                    recoveryPending = false;
                    if (WindowDisplay.DraggableCaption(handle) is { } caption)
                        Move(WindowRecovery.Recover(bounds, caption, areas, dpi));
                }

                void Move(ScreenRect target)
                {
                    if (target.X != rect.Left || target.Y != rect.Top || target.Width != rect.Right - rect.Left || target.Height != rect.Bottom - rect.Top)
                        Native.SetWindowPos(handle, 0, (int)target.X, (int)target.Y, (int)target.Width, (int)target.Height, 0x0014);
                }
            }
            finally { adjusting = false; }
        }
        void Schedule(bool recover)
        {
            recoveryPending |= recover;
            updatePending = true;
            if (queued || moving) return;
            queued = true;
            window.Dispatcher.BeginInvoke(() => { queued = false; Apply(); });
        }
        constraint.Apply = Apply;
        window.SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                if (message == 0x0231) moving = true; // WM_ENTERSIZEMOVE
                if (message == 0x0232)
                {
                    moving = false; // WM_EXITSIZEMOVE alone never requests recovery.
                    if (updatePending) Schedule(false);
                }
                if (message == 0x007E || message == 0x001A && wParam == 0x002F) Schedule(true); // display/work-area change
                if (message == 0x02E0) Schedule(false); // DPI: let WPF/Windows position the window.
                return 0;
            });
        };
        window.Loaded += (_, _) => { recoveryPending = true; Apply(); };
        window.IsVisibleChanged += (_, _) => { if (window.IsVisible) Schedule(true); };
        window.StateChanged += (_, _) =>
        {
            var restored = previousState == WindowState.Minimized && window.WindowState == WindowState.Normal;
            previousState = window.WindowState;
            if (restored) Schedule(true);
        };
        window.LocationChanged += (_, _) =>
        {
            if (adjusting) return;
            if (constraint.Override != null) Apply();
            else Schedule(false); // Refresh minima on a smaller monitor without moving or resizing.
        };
        window.SizeChanged += (_, _) => { if (constraint.Override != null) Apply(); };
    }
}
