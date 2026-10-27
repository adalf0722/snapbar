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
    private readonly StackPanel root = new(), row = new() { Orientation = Orientation.Horizontal }, menuRows = new();
    private readonly Border shell, more;
    private readonly TextBlock queryLabel, count;
    private readonly ScrollViewer menuScroll;
    private readonly Dictionary<TextAction, Button> builtinButtons = new();
    private readonly List<Button> menuButtons = [];
    private SelectionSnapshot? snapshot;
    private ActionSettings settings = new();
    private string searchTemplate = WebActionPolicy.DefaultSearchTemplate, query = "";
    private nint handle;
    private int pointerX, pointerY, menuIndex = -1;
    public event Action<TextAction>? ActionRequested;
    public event Action<string>? CustomActionRequested;
    public event Action<ActionSettings>? SettingsChanged;
    public event Action? ManageRequested;
    public bool MenuOpen => more.Visibility == Visibility.Visible;
    internal Button FindActionButton(TextAction action) => builtinButtons[action];

    public PopupWindow()
    {
        Title = "SnapBar Actions"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; AllowsTransparency = true; Background = Brushes.Transparent;
        FontFamily = new("Segoe UI, Microsoft JhengHei UI"); UseLayoutRounding = true;
        shell = new Border { CornerRadius = new(11), BorderThickness = new(1), Padding = new(6), Child = root };
        root.Children.Add(row);
        var menu = new StackPanel { Margin = new(3, 8, 3, 2) };
        queryLabel = ActionUi.Text("搜尋動作…　·　Ctrl+V 可貼上", 12, ActionUi.Muted);
        var searchBox = new Border { Background = ActionUi.Canvas, CornerRadius = new(6), Padding = new(11, 9, 11, 9), Child = queryLabel };
        AutomationProperties.SetAutomationId(searchBox, "MoreActionSearch"); menu.Children.Add(searchBox);
        menuScroll = new ScrollViewer { Content = menuRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 260, Margin = new(0, 5, 0, 4), Focusable = false };
        menu.Children.Add(menuScroll); count = ActionUi.Text("", 10, ActionUi.Muted); count.Margin = new(8, 7, 0, 8); menu.Children.Add(count);
        var manage = ActionUi.Button("管理／新增動作", () => { Dismiss(); ManageRequested?.Invoke(); }, "settings");
        manage.Focusable = false; manage.IsTabStop = false; menu.Children.Add(manage);
        more = new Border { Child = menu, Background = Brushes.White, CornerRadius = new(8), Visibility = Visibility.Collapsed };
        root.Children.Add(more); Content = shell;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, (nint)((long)Native.GetWindowLongPtr(handle, -20) | 0x08000000));
            HwndSource.FromHwnd(handle).AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) =>
            { if (msg == Native.WM_MOUSEACTIVATE) { handled = true; return 3; } return 0; });
        };
    }

    public void Present(SelectionSnapshot selection, int x, int y, string searchTemplate = WebActionPolicy.DefaultSearchTemplate,
        ActionSettings? actionSettings = null)
    {
        snapshot = selection; pointerX = x; pointerY = y; this.searchTemplate = searchTemplate;
        settings = ActionCatalog.Normalize(actionSettings ?? new()); query = ""; menuIndex = -1; more.Visibility = Visibility.Collapsed;
        RenderBar(); Place(); Show(); Place();
    }

    private void RenderBar()
    {
        if (snapshot == null) return;
        var dark = settings.Theme == "Dark"; var fg = dark ? Brushes.White : ActionUi.Ink;
        shell.Background = dark ? ActionUi.Brush("#252B2A") : Brushes.White; shell.BorderBrush = dark ? ActionUi.Brush("#47504B") : ActionUi.Line;
        row.Children.Clear(); builtinButtons.Clear();
        var chosen = ActionCatalog.GetBar(settings, snapshot);
        var shown = chosen.Select(a => a.Id).ToHashSet();
        foreach (var item in chosen.Concat(ActionCatalog.GetItems(settings).Where(a => !shown.Contains(a.Id))))
        {
            var button = ActionUi.Button(settings.ShowLabels ? item.Name : "", () => Dispatch(item), item.Icon);
            button.Tag = item; button.Height = 36; button.Margin = new(1); button.Padding = new(9, 6, 9, 6); button.BorderThickness = new(0);
            button.Background = Brushes.Transparent; button.Focusable = false; button.IsTabStop = false;
            var stack = (StackPanel)button.Content;
            if (stack.Children[0] is Viewbox icon) ((System.Windows.Shapes.Path)icon.Child).Stroke = fg;
            foreach (var text in stack.Children.OfType<TextBlock>()) text.Foreground = fg;
            button.ToolTip = item.Name; AutomationProperties.SetName(button, item.Name);
            button.Visibility = shown.Contains(item.Id) ? Visibility.Visible : Visibility.Collapsed; button.IsEnabled = ActionCatalog.IsApplicable(item, snapshot);
            if (item.BuiltIn is { } action)
            {
                AutomationProperties.SetAutomationId(button, action + "Action"); builtinButtons[action] = button;
                if (action == TextAction.Search) button.IsEnabled = WebActionPolicy.Search(snapshot.Text, searchTemplate).Success;
            }
            else AutomationProperties.SetAutomationId(button, "CustomAction_" + item.Id);
            row.Children.Add(button);
        }
        var dots = ActionUi.Button("", ToggleMore, "more"); dots.Height = 36; dots.Margin = new(2, 0, 0, 0);
        dots.Padding = new(10, 6, 10, 6); dots.Background = Brushes.Transparent; dots.BorderThickness = new(0);
        dots.ToolTip = "更多動作 · 輸入名稱、上下選擇、Enter 執行、Esc 關閉"; dots.Focusable = false; dots.IsTabStop = false;
        ((System.Windows.Shapes.Path)((Viewbox)((StackPanel)dots.Content).Children[0]).Child).Stroke = fg;
        AutomationProperties.SetName(dots, "更多動作"); AutomationProperties.SetAutomationId(dots, "MoreActions"); row.Children.Add(dots);
    }

    private void Dispatch(ActionItem item)
    { if (item.BuiltIn is { } action) ActionRequested?.Invoke(action); else CustomActionRequested?.Invoke(item.Id); }
    private void ToggleMore()
    { more.Visibility = MenuOpen ? Visibility.Collapsed : Visibility.Visible; query = ""; menuIndex = -1; RenderMenu(); Place(); }

    private void RenderMenu()
    {
        if (snapshot == null) return;
        queryLabel.Text = query.Length == 0 ? "搜尋動作…　·　Ctrl+V 可貼上" : query;
        menuRows.Children.Clear(); menuButtons.Clear();
        var barIds = ActionCatalog.GetBar(settings, snapshot).Select(a => a.Id).ToHashSet();
        var items = ActionCatalog.GetMore(settings, snapshot, query).ToList();
        foreach (var button in row.Children.OfType<Button>().Where(b => b.Tag is ActionItem && b.Visibility == Visibility.Collapsed))
        {
            var item = (ActionItem)button.Tag;
            if (barIds.Contains(item.Id) && ActionCatalog.IsApplicable(item, snapshot)
                && (query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase))) items.Add(item);
        }
        var recentShown = false; var allShown = false;
        foreach (var item in items.DistinctBy(a => a.Id))
        {
            var recent = query.Length == 0 && settings.RecentIds.Contains(item.Id);
            if (recent && !recentShown || !recent && !allShown)
            {
                var heading = ActionUi.Text(recent ? "最近使用" : query.Length == 0 ? "所有動作" : "搜尋結果", 10, ActionUi.Muted);
                heading.Margin = new(10, 8, 0, 4); menuRows.Children.Add(heading);
                if (recent) recentShown = true; else allShown = true;
            }
            var line = new DockPanel(); var pin = ActionUi.Button("", () => Pin(item.Id), "pin");
            pin.Focusable = false; pin.IsTabStop = false; pin.ToolTip = "釘選到動作列"; pin.BorderThickness = new(0); pin.Margin = new(0);
            pin.IsEnabled = settings.Layout.Count(e => e.Placement == ActionPlacement.Bar) < 6;
            DockPanel.SetDock(pin, Dock.Right); line.Children.Add(pin);
            var button = ActionUi.Button(item.Name, () => Dispatch(item), item.Icon); button.Focusable = false; button.IsTabStop = false;
            button.BorderThickness = new(0); button.Margin = new(0); AutomationProperties.SetAutomationId(button, "MoreAction_" + item.Id);
            menuButtons.Add(button); line.Children.Add(button); menuRows.Children.Add(line);
        }
        if (items.Count == 0) { var empty = ActionUi.Text("找不到動作，試試其他名稱或到管理頁新增。", 12, ActionUi.Muted); empty.Margin = new(10, 18, 10, 18); menuRows.Children.Add(empty); }
        count.Text = $"{snapshot.CharacterCount} 字 · 來源選取仍保留";
        for (var i = 0; i < menuButtons.Count; i++) menuButtons[i].Background = i == menuIndex ? ActionUi.Pale : Brushes.White;
        if (menuIndex >= 0 && menuIndex < menuButtons.Count)
        {
            var selected = menuButtons[menuIndex];
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (MenuOpen && menuButtons.Contains(selected)) selected.BringIntoView();
            }));
        }
    }

    private void Pin(string id)
    {
        try { settings = ActionCatalog.SetPlacement(settings, id, ActionPlacement.Bar); SettingsChanged?.Invoke(settings); RenderBar(); RenderMenu(); Place(); }
        catch (ArgumentException) { }
    }

    public bool HandleMenuKey(uint key)
    {
        if (!IsVisible || !MenuOpen) return false;
        if (key is 0x10 or 0x11 or 0xA0 or 0xA1 or 0xA2 or 0xA3) return true;
        if (key == 0x1B) { if (query.Length > 0) query = ""; else more.Visibility = Visibility.Collapsed; menuIndex = -1; RenderMenu(); Place(); return true; }
        if (key is 0x26 or 0x28) { if (menuButtons.Count > 0) { menuIndex = (menuIndex + (key == 0x28 ? 1 : -1) + menuButtons.Count) % menuButtons.Count; RenderMenu(); } return true; }
        if (key == 0x0D) { if (menuButtons.Count > 0) menuButtons[Math.Max(0, menuIndex)].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return true; }
        if (key == 0x08) { if (query.Length > 0) query = query[..^1]; }
        else if (key == 0x56 && (Native.GetAsyncKeyState(0x11) & 0x8000) != 0)
        { try { if (Clipboard.ContainsText()) { var text = Clipboard.GetText().Replace("\r", "").Replace("\n", " "); query = text[..Math.Min(128, text.Length)]; } } catch (System.Runtime.InteropServices.ExternalException) { } }
        else if ((Native.GetAsyncKeyState(0x11) & 0x8000) != 0) return false;
        else if (key is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 || key == 0x20) { if (query.Length < 128) query += (char)key; }
        else return false;
        menuIndex = -1; RenderMenu(); return true;
    }

    private void Place()
    {
        if (snapshot == null) return;
        new WindowInteropHelper(this).EnsureHandle();
        var anchor = snapshot.Bounds ?? new ScreenRect(pointerX, pointerY, 1, 1); var area = Native.WorkArea((int)anchor.X, (int)anchor.Y);
        void SizeAndPlace()
        {
            var dpi = Native.GetDpiForWindow(handle); var scale = (dpi == 0 ? 96 : dpi) / 96d; var available = Math.Max(1, area.Width / scale);
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            foreach (var button in row.Children.OfType<Button>().Where(b => b.Tag is ActionItem && b.Visibility == Visibility.Visible).Reverse())
            { if (row.DesiredSize.Width + 14 <= available) break; button.Visibility = Visibility.Collapsed; row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); }
            Width = Math.Min(available, Math.Max(MenuOpen ? 320 : 0, row.DesiredSize.Width + 14));
            if (more.Child is FrameworkElement panel) panel.MaxWidth = Math.Max(1, Width - 14);
            menuScroll.MaxHeight = Math.Max(20, Math.Min(260, area.Height / scale - 190)); if (MenuOpen) RenderMenu();
            root.Measure(new Size(Math.Max(1, Width - 14), double.PositiveInfinity)); Height = Math.Min(area.Height / scale, root.DesiredSize.Height + 14);
            var p = PopupPlacement.Place(anchor, area, Width * scale, Height * scale);
            Native.SetWindowPos(handle, new(-1), (int)p.X, (int)p.Y, (int)p.Width, (int)p.Height, 0x10);
        }
        var previous = Native.GetDpiForWindow(handle); SizeAndPlace(); if (Native.GetDpiForWindow(handle) != previous) SizeAndPlace();
    }

    public void Dismiss() { more.Visibility = Visibility.Collapsed; query = ""; Hide(); }
    public bool Contains(int x, int y) => IsVisible && Native.GetWindowRect(handle, out var r) && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
}
