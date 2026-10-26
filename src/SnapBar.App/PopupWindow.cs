using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed class PopupWindow : Window
{
    private readonly Button cut, paste, search, openLink, revealPath;
    private readonly WrapPanel row;
    private readonly TextBlock details;
    private nint handle;
    public event Action<TextAction>? ActionRequested;
    public PopupWindow()
    {
        Title = "SnapBar Actions"; Width = 330; Height = 58;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(23, 32, 48));
        row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new(8) };
        Button Add(string label, TextAction action)
        {
            var button = new Button { Content = label, Width = 70, Height = 36, Margin = new(3), Padding = new(4), Focusable = false, IsTabStop = false, Cursor = System.Windows.Input.Cursors.Hand };
            AutomationProperties.SetAutomationId(button, action + "Action");
            button.Click += (_, _) => ActionRequested?.Invoke(action); row.Children.Add(button); return button;
        }
        Add("複製", TextAction.Copy); cut = Add("剪下", TextAction.Cut); paste = Add("貼上", TextAction.Paste);
        search = Add("搜尋", TextAction.Search); openLink = Add("開啟連結", TextAction.OpenLink);
        revealPath = Add("檔案位置", TextAction.RevealPath);
        revealPath.ToolTip = "直接開啟資料夾；檔案則顯示位置並選取，不會執行檔案";
        details = new TextBlock { Foreground = Brushes.LightSteelBlue, VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center, Margin = new(3), Width = 70, Height = 36, Padding = new(0, 10, 0, 0), FontSize = 11 };
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

    public void Present(SelectionSnapshot selection, int pointerX, int pointerY,
        string searchTemplate = WebActionPolicy.DefaultSearchTemplate)
    {
        cut.IsEnabled = paste.IsEnabled = SessionPolicy.CanWrite(selection, selection.Target);
        search.IsEnabled = WebActionPolicy.Search(selection.Text, searchTemplate).Success;
        search.ToolTip = search.IsEnabled ? "以預設瀏覽器搜尋選取文字" : "選取內容過長或搜尋設定無效";
        var link = WebActionPolicy.OpenLink(selection.Text);
        openLink.Visibility = link.Success ? Visibility.Visible : Visibility.Collapsed;
        openLink.ToolTip = link.Success ? "開啟網址：" + link.Uri!.Host : null;
        revealPath.Visibility = PathActionPolicy.Resolve(selection.Text).Success ? Visibility.Visible : Visibility.Collapsed;
        details.Text = $"{selection.CharacterCount} 字";
        var anchor = selection.Bounds ?? new ScreenRect(pointerX, pointerY, 1, 1);
        var area = Native.WorkArea((int)anchor.X, (int)anchor.Y);
        new WindowInteropHelper(this).EnsureHandle();
        var dpi = Native.GetDpiForWindow(handle); var scale = (dpi == 0 ? 96 : dpi) / 96d;
        SizeForWorkArea(area, scale);
        Show();
        var placement = PopupPlacement.Place(anchor, area, Width * scale, Height * scale);
        Native.SetWindowPos(handle, new nint(-1), (int)placement.X, (int)placement.Y, (int)placement.Width, (int)placement.Height, 0x10);
        // Crossing monitors can change this HWND's DPI during SetWindowPos.
        var newDpi = Native.GetDpiForWindow(handle);
        if (newDpi != 0 && newDpi != dpi)
        {
            SizeForWorkArea(area, newDpi / 96d);
            placement = PopupPlacement.Place(anchor, area, Width * newDpi / 96d, Height * newDpi / 96d);
            Native.SetWindowPos(handle, new nint(-1), (int)placement.X, (int)placement.Y, (int)placement.Width, (int)placement.Height, 0x10);
        }
    }

    private void SizeForWorkArea(ScreenRect area, double scale)
    {
        var children = row.Children.OfType<FrameworkElement>().Where(child => child.Visibility == Visibility.Visible).ToArray();
        var available = Math.Max(1, area.Width / scale);
        var itemWidth = Math.Min(76, Math.Max(1, available - 16));
        var columns = Math.Max(1, Math.Min(children.Length, (int)((available - 16) / itemWidth)));
        foreach (var child in children) child.Width = Math.Max(1, itemWidth - 6);
        Width = Math.Min(available, 16 + columns * itemWidth);
        Height = Math.Min(area.Height / scale, 16 + 42 * Math.Ceiling((double)children.Length / columns));
    }

    public bool Contains(int x, int y) => IsVisible && Native.GetWindowRect(handle, out var r) && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
}
