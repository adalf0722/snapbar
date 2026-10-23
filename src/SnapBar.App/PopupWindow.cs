using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed class PopupWindow : Window
{
    private readonly Button cut, paste;
    private readonly TextBlock details;
    private nint handle;
    public event Action<TextAction>? ActionRequested;
    public PopupWindow()
    {
        Title = "SnapBar Actions"; Width = 330; Height = 58;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(23, 32, 48));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(8) };
        Button Add(string label, TextAction action)
        {
            var button = new Button { Content = label, Width = 70, Margin = new(3), Padding = new(4), Focusable = false, IsTabStop = false, Cursor = System.Windows.Input.Cursors.Hand };
            button.Click += (_, _) => ActionRequested?.Invoke(action); row.Children.Add(button); return button;
        }
        Add("複製", TextAction.Copy); cut = Add("剪下", TextAction.Cut); paste = Add("貼上", TextAction.Paste);
        details = new TextBlock { Foreground = Brushes.LightSteelBlue, VerticalAlignment = VerticalAlignment.Center, Margin = new(7, 0, 0, 0), FontSize = 11 };
        row.Children.Add(details); Content = row;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, (nint)((long)Native.GetWindowLongPtr(handle, -20) | 0x08000000));
            HwndSource.FromHwnd(handle).AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) =>
            {
                if (msg == Native.WM_MOUSEACTIVATE) { handled = true; return 3; }
                return 0;
            });
        };
    }

    public void Present(SelectionSnapshot selection, int pointerX, int pointerY)
    {
        cut.IsEnabled = paste.IsEnabled = SessionPolicy.CanWrite(selection, selection.Target);
        details.Text = $"{selection.CharacterCount} 字";
        Show();
        var anchor = selection.Bounds ?? new ScreenRect(pointerX, pointerY, 1, 1);
        var area = Native.WorkArea((int)anchor.X, (int)anchor.Y);
        var dpi = Native.GetDpiForWindow(handle); var scale = (dpi == 0 ? 96 : dpi) / 96d;
        var placement = PopupPlacement.Place(anchor, area, Width * scale, Height * scale);
        Native.SetWindowPos(handle, new nint(-1), (int)placement.X, (int)placement.Y, (int)placement.Width, (int)placement.Height, 0x10);
        // Crossing monitors can change this HWND's DPI during SetWindowPos.
        var newDpi = Native.GetDpiForWindow(handle);
        if (newDpi != 0 && newDpi != dpi)
        {
            placement = PopupPlacement.Place(anchor, area, Width * newDpi / 96d, Height * newDpi / 96d);
            Native.SetWindowPos(handle, new nint(-1), (int)placement.X, (int)placement.Y, (int)placement.Width, (int)placement.Height, 0x10);
        }
    }

    public bool Contains(int x, int y) => IsVisible && Native.GetWindowRect(handle, out var r) && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
}
