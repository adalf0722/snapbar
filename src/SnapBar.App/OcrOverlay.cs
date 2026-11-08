using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class OcrOverlay : Window
{
    private readonly ScreenRect monitor;
    private readonly Canvas canvas = new() { Focusable = true, Background = new SolidColorBrush(Color.FromArgb(65, 0, 0, 0)) };
    private readonly Rectangle selection = new() { Stroke = Brushes.White, StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(60, 45, 105, 200)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private Point? start;

    internal OcrOverlay(ScreenRect monitor, Action<ScreenRect?> complete)
    {
        this.monitor = monitor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        var help = new Border { Child = ActionUi.Text(UiText.Get("OcrOverlay_000"), 15, Brushes.White),
            Background = Brushes.Black, Padding = new(12, 8, 12, 8), IsHitTestVisible = false };
        Canvas.SetLeft(help, 24);
        Canvas.SetTop(help, 24);
        canvas.Children.Add(selection);
        canvas.Children.Add(help);
        Content = canvas;
        Activated += (_, _) => Keyboard.Focus(canvas);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; complete(null); }
        };
        canvas.MouseRightButtonDown += (_, _) => complete(null);
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            start = Clamp(e.GetPosition(canvas));
            if (!canvas.CaptureMouse()) { start = null; return; }
            UpdateSelection(start.Value);
            selection.Visibility = Visibility.Visible;
        };
        canvas.MouseMove += (_, e) =>
        {
            if (start != null) UpdateSelection(Clamp(e.GetPosition(canvas)));
        };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (start is not { } from) return;
            var to = Clamp(e.GetPosition(canvas));
            start = null;
            canvas.ReleaseMouseCapture();
            var a = canvas.PointToScreen(from);
            var b = canvas.PointToScreen(to);
            var left = Math.Clamp(Math.Floor(Math.Min(a.X, b.X)), monitor.X, monitor.Right);
            var top = Math.Clamp(Math.Floor(Math.Min(a.Y, b.Y)), monitor.Y, monitor.Bottom);
            var right = Math.Clamp(Math.Ceiling(Math.Max(a.X, b.X)), monitor.X, monitor.Right);
            var bottom = Math.Clamp(Math.Ceiling(Math.Max(a.Y, b.Y)), monitor.Y, monitor.Bottom);
            if (right - left >= 4 && bottom - top >= 4) complete(new(left, top, right - left, bottom - top));
            else selection.Visibility = Visibility.Collapsed;
        };
        canvas.LostMouseCapture += (_, _) => { start = null; selection.Visibility = Visibility.Collapsed; };
    }

    internal bool ShowOnMonitor()
    {
        Show();
        return Native.SetWindowPos(new WindowInteropHelper(this).Handle, new(-1), (int)monitor.X, (int)monitor.Y,
            (int)monitor.Width, (int)monitor.Height, 0x10);
    }

    internal void ActivateCapture()
    {
        Activate();
        Keyboard.Focus(canvas);
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, canvas.ActualWidth), Math.Clamp(point.Y, 0, canvas.ActualHeight));

    private void UpdateSelection(Point end)
    {
        var origin = start!.Value;
        Canvas.SetLeft(selection, Math.Min(origin.X, end.X));
        Canvas.SetTop(selection, Math.Min(origin.Y, end.Y));
        selection.Width = Math.Abs(origin.X - end.X);
        selection.Height = Math.Abs(origin.Y - end.Y);
    }
}
