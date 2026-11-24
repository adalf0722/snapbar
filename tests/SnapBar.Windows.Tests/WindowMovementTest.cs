using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class WindowMovementTest
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    private static async Task Drain(Window window)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new Window { Title = "SnapBar window movement fixture", Width = 420, Height = 300,
            MinWidth = 200, MinHeight = 150, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        WindowWorkArea.Attach(window);
        try
        {
            window.Show(); await Drain(window);
            var handle = new WindowInteropHelper(window).Handle;
            var dpi = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
            var areas = WindowDisplay.WorkAreas();
            check(areas.Count > 0, "movement reads actual monitor work areas without a synthetic fallback");
            check(WindowDisplay.DraggableCaption(handle) is not null, "movement reads the native draggable title bar");
            var work = areas.First();
            ScreenRect Bounds()
            {
                if (!Native.GetWindowRect(handle, out var rect)) throw new InvalidOperationException("Cannot read fixture bounds.");
                return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
            void Move(ScreenRect bounds) => Native.SetWindowPos(handle, 0, (int)bounds.X, (int)bounds.Y,
                (int)bounds.Width, (int)bounds.Height, 0x0014);
            var original = Bounds();
            var partial = original with { X = work.Right - 80, Y = work.Y + 80 };
            Move(partial); await Drain(window);
            check(Bounds() == partial, "ordinary move may leave part of window outside the monitor");
            var resized = partial with { Width = original.Width + 40, Height = original.Height + 40 };
            Move(resized); await Drain(window);
            check(Bounds() == resized, "ordinary resize does not force window inside one work area");

            var outside = resized with { X = areas.Max(area => area.Right) + 500 };
            SendMessage(handle, 0x0231, 0, 0);
            Move(outside); await Drain(window);
            check(Bounds() == outside, "interactive movement does not push window back");
            SendMessage(handle, 0x0232, 0, 0); await Drain(window);
            check(Bounds() == outside, "releasing a move without display changes does not request recovery");

            // A valid suggested rectangle exercises the DPI hook without changing the OS display settings.
            var suggestion = new Native.Rect { Left = (int)outside.X, Top = (int)outside.Y,
                Right = (int)outside.Right, Bottom = (int)outside.Bottom };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Native.Rect>());
            try
            {
                Marshal.StructureToPtr(suggestion, pointer, false);
                var currentDpi = (int)Native.GetDpiForWindow(handle);
                SendMessage(handle, 0x02E0, (nint)(currentDpi | currentDpi << 16), pointer);
                await Drain(window);
            }
            finally { Marshal.FreeHGlobal(pointer); }
            check(Bounds() == outside, "DPI notification does not add single-monitor position correction");

            var caption = WindowDisplay.DraggableCaption(handle)!;
            var expected = WindowRecovery.Recover(outside, caption, areas, dpi);
            check(expected != outside, "offscreen fixture genuinely needs title-bar recovery");
            window.Hide(); window.Show(); await Drain(window);
            check(Bounds() == expected, "showing an unreachable window applies only the minimum recovery");
            check(Bounds().Width == outside.Width && Bounds().Height == outside.Height,
                "recovery does not shrink the window");
            UiAppearanceTest.Snapshot(window, "window-titlebar-recovered.png");

            SendMessage(handle, 0x0231, 0, 0);
            Move(outside);
            SendMessage(handle, 0x007E, 0, 0); await Drain(window);
            check(Bounds() == outside, "display-change recovery waits until interactive movement finishes");
            SendMessage(handle, 0x0232, 0, 0); await Drain(window);
            check(Bounds() == expected, "pending display-change recovery runs after movement finishes");

            Move(outside);
            SendMessage(handle, 0x001A, 0, 0); await Drain(window);
            check(Bounds() == outside, "unrelated settings notification does not reposition the window");
            SendMessage(handle, 0x001A, 0x002F, 0); await Drain(window);
            check(Bounds() == expected, "work-area change recovers an unreachable title bar");

            Move(partial); await Drain(window);
            SendMessage(handle, 0x007E, 0, 0); await Drain(window);
            var partialCaption = WindowDisplay.DraggableCaption(handle)!;
            check(WindowRecovery.Recover(Bounds(), partialCaption, areas, dpi) == Bounds(),
                "display recovery restores a usable drag target");
            var usablePartial = original with { X = work.Right - 250, Y = work.Y + 80 };
            Move(usablePartial); await Drain(window);
            SendMessage(handle, 0x007E, 0, 0); await Drain(window);
            check(Bounds() == usablePartial, "display change preserves a reachable window even if its body extends offscreen");

            WindowWorkArea.SetWorkAreaOverride(window, new(work.X / dpi, work.Y / dpi, 260, 200));
            await Drain(window);
            check(window.ActualWidth <= 261 && window.ActualHeight <= 201,
                "explicit work-area override retains strict compact-layout behavior");
            WindowWorkArea.SetWorkAreaOverride(window, null);
            Move(partial); await Drain(window);
            check(Bounds() == partial, "removing override restores unrestricted movement");
        }
        finally { window.Close(); }
    }
}
