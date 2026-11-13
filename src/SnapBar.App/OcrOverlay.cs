using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class OcrOverlay : Window
{
    private readonly ScreenRect monitor;
    private readonly Canvas canvas = new() { Focusable = true, Background = Brushes.Transparent };
    private readonly System.Windows.Shapes.Path mask = new() { Fill = new SolidColorBrush(Color.FromArgb(65, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle edge = new() { Stroke = Brushes.Black, StrokeThickness = 3, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Rectangle selection = new() { Stroke = Brushes.White, StrokeThickness = 1,
        Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Border help;
    private bool helpActive;
    private Point? start;

    internal OcrOverlay(ScreenRect monitor, BitmapSource image, Action<ScreenRect?> complete, Action<OcrOverlay> entered)
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
        help = new Border { Child = ActionUi.Text(UiText.Get("OcrOverlay_000"), 15, Brushes.White),
            Background = Brushes.Black, Padding = new(12, 8, 12, 8), IsHitTestVisible = false };
        AutomationProperties.SetAutomationId(help.Child, "OcrCaptureHelp");
        var background = new Image { Source = image, Stretch = Stretch.Fill, IsHitTestVisible = false };
        canvas.Children.Add(background);
        canvas.Children.Add(mask);
        canvas.Children.Add(edge);
        canvas.Children.Add(selection);
        canvas.Children.Add(help);
        Content = canvas;
        canvas.SizeChanged += (_, _) =>
        {
            background.Width = canvas.ActualWidth; background.Height = canvas.ActualHeight;
            ResetSelection(); PositionHelp();
        };
        canvas.MouseEnter += (_, _) => { if (start == null) entered(this); };
        Closed += (_, _) => { background.Source = null; Content = null; };
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
            help.Visibility = Visibility.Collapsed;
            UpdateSelection(start.Value);
            selection.Visibility = edge.Visibility = Visibility.Visible;
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
            else ResetSelection();
        };
        canvas.LostMouseCapture += (_, _) => { start = null; ResetSelection(); };
    }

    internal void ShowHelp(bool visible)
    {
        helpActive = visible;
        help.Visibility = visible && start == null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PositionHelp()
    {
        help.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var work = Native.WorkArea((int)monitor.X, (int)monitor.Y);
        var scale = canvas.ActualWidth > 0 ? monitor.Width / canvas.ActualWidth : 1;
        Canvas.SetLeft(help, Math.Max(0, (work.X - monitor.X + work.Width / 2) / scale - help.DesiredSize.Width / 2));
        Canvas.SetTop(help, Math.Max(0, (work.Y - monitor.Y) / scale + 24));
    }

    private void ResetSelection()
    {
        selection.Visibility = edge.Visibility = Visibility.Collapsed;
        mask.Data = new RectangleGeometry(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight));
        help.Visibility = helpActive ? Visibility.Visible : Visibility.Collapsed;
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
        Canvas.SetLeft(edge, Canvas.GetLeft(selection)); Canvas.SetTop(edge, Canvas.GetTop(selection));
        edge.Width = selection.Width; edge.Height = selection.Height;
        mask.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight)),
            new RectangleGeometry(new Rect(Canvas.GetLeft(selection), Canvas.GetTop(selection), selection.Width, selection.Height)));
    }
}
