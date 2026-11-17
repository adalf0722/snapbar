using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class CopyFeedbackWindow : Window
{
    private readonly TextBlock message;
    private readonly DispatcherTimer timer;

    internal CopyFeedbackWindow()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Width = 144; Height = 38; IsHitTestVisible = false;
        message = new TextBlock { Foreground = Brushes.White, FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(37, 46, 60)),
            CornerRadius = new(8), Child = message };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, Native.GetWindowLongPtr(handle, -20) | (nint)0x080000A0);
        };
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) => HideFeedback();
        Closed += (_, _) => timer.Stop();
    }

    internal void Present(int x, int y, ScreenRect? bounds, ScreenRect? toolbar = null)
    {
        timer.Stop(); message.Text = UiText.Get("AutoCopy_Copied");
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var area = Native.WorkArea(x, y);
        void Place()
        {
            var dpi = Native.GetDpiForWindow(handle);
            var scale = (dpi == 0 ? 96 : dpi) / 96d;
            var width = Math.Min(area.Width, Width * scale);
            var height = Math.Min(area.Height, Height * scale);
            var below = Math.Max(Math.Max(y, bounds?.Bottom ?? y), toolbar?.Bottom ?? y) + 18 * scale;
            var top = below + height <= area.Bottom ? below
                : Math.Min(Math.Min(y, bounds?.Y ?? y), toolbar?.Y ?? y) - height - 18 * scale;
            var left = Math.Clamp(x + 18 * scale, area.X, area.Right - width);
            top = Math.Clamp(top, area.Y, area.Bottom - height);
            Native.SetWindowPos(handle, new(-1), (int)left, (int)top, (int)width, (int)height, 0x10);
        }
        Place(); Show(); Place(); timer.Start();
    }

    internal void HideFeedback() { timer.Stop(); Hide(); }
}
