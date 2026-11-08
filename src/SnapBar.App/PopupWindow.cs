using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed class PopupWindow : Window
{
    private readonly StackPanel root = new(), row = new() { Orientation = Orientation.Horizontal }, menuRows = new();
    private readonly Border shell, more, searchBox;
    private readonly TextBlock searchPlaceholder, count;
    private readonly TextBox searchInput;
    private readonly ScrollViewer menuScroll;
    private readonly Dictionary<TextAction, Button> builtinButtons = new();
    private readonly List<Button> menuButtons = [];
    private SelectionSnapshot? snapshot;
    private ActionSettings settings = new();
    private string searchTemplate = WebActionPolicy.DefaultSearchTemplate, query = "";
    private nint handle;
    private int pointerX, pointerY, menuIndex = -1;
    private (int X, int Y)? lastPointer;
    private bool searchMode, syncingQuery, composing;
    private double unfilteredScrollOffset;
    public event Action<TextAction>? ActionRequested;
    public event Action<string>? CustomActionRequested;
    public event Action<ActionSettings>? SettingsChanged;
    public event Action? ManageRequested;
    public event Action? SessionAbandoned;
    public bool MenuOpen => more.Visibility == Visibility.Visible;
    public bool HasSearchForeground => searchMode && IsVisible && handle != 0 && Native.GetForegroundWindow() == handle;
    internal Button FindActionButton(TextAction action) => builtinButtons[action];

    public PopupWindow()
    {
        Title = "SnapBar Actions"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; AllowsTransparency = true; Background = Brushes.Transparent;
        ActionUi.LoadStyles(this); FontFamily = ActionUi.Font; FontSize = 14; UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        shell = new Border { CornerRadius = new(11), BorderThickness = new(1), Padding = new(6), Child = root };
        root.Children.Add(row);
        var menu = new StackPanel { Margin = new(3, 8, 3, 2) };
        searchInput = new TextBox { MaxLength = 128, FontSize = 13, BorderThickness = new(0), Background = Brushes.Transparent,
            Foreground = ActionUi.Ink, CaretBrush = ActionUi.Ink, Padding = new(2, 5, 2, 5), Cursor = Cursors.IBeam,
            VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(searchInput, "MoreActionSearch"); AutomationProperties.SetName(searchInput, UiText.Get("PopupWindow_000"));
        searchPlaceholder = ActionUi.Text(UiText.Get("PopupWindow_001"), 13, ActionUi.Muted); searchPlaceholder.IsHitTestVisible = false; searchPlaceholder.Margin = new(3, 0, 0, 0);
        var searchContent = new Grid(); searchContent.Children.Add(searchInput); searchContent.Children.Add(searchPlaceholder);
        searchBox = new Border { Background = Brushes.White, BorderBrush = ActionUi.Line, BorderThickness = new(1),
            CornerRadius = new(6), Padding = new(9, 3, 9, 3), Child = searchContent, Cursor = Cursors.IBeam };
        searchInput.PreviewMouseLeftButtonDown += (_, _) => BeginSearchInput();
        searchInput.IsKeyboardFocusWithinChanged += (_, _) => searchBox.BorderBrush = searchInput.IsKeyboardFocusWithin ? ActionUi.Accent : settings.Theme == "Dark" ? ActionUi.DarkLine : ActionUi.Line;
        TextCompositionManager.AddPreviewTextInputStartHandler(searchInput, (_, _) => composing = true);
        searchInput.PreviewTextInput += (_, _) => composing = false;
        searchInput.PreviewKeyUp += (_, e) => { var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key; if (key is Key.Escape or Key.Enter) composing = false; };
        searchInput.PreviewKeyDown += (_, e) =>
        {
            if (composing || e.Key == Key.ImeProcessed) return;
            var key = e.Key switch { Key.Escape => 0x1Bu, Key.Up => 0x26u, Key.Down => 0x28u, Key.Enter => 0x0Du, _ => 0u };
            if (key != 0) e.Handled = HandleMenuKey(key);
        };
        menu.Children.Add(searchBox);
        menuScroll = new ScrollViewer { Content = menuRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 260, Margin = new(0, 5, 0, 4), Focusable = false };
        menu.Children.Add(menuScroll); count = ActionUi.Text("", 10, ActionUi.Muted); count.Margin = new(8, 7, 0, 8); menu.Children.Add(count);
        var manage = ActionUi.Button(UiText.Get("PopupWindow_002"), () => { Dismiss(); ManageRequested?.Invoke(); }, "settings");
        manage.Focusable = false; manage.IsTabStop = false; menu.Children.Add(manage);
        more = new Border { Child = menu, Background = Brushes.White, CornerRadius = new(8), Visibility = Visibility.Collapsed };
        root.Children.Add(more); Content = shell;
        searchInput.TextChanged += (_, _) =>
        {
            if (syncingQuery) return;
            UpdateQuery(searchInput.Text);
        };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, (nint)((long)Native.GetWindowLongPtr(handle, -20) | 0x08000000));
            HwndSource.FromHwnd(handle).AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) =>
            {
                if (msg == Native.WM_MOUSEACTIVATE) { handled = true; return 3; }
                if (msg == 0x112 && ((long)w & 0xFFF0) == 0xF060)
                {
                    if (HasSearchForeground && !TryRestoreSourceFocus()) SessionAbandoned?.Invoke();
                    Dismiss(); handled = true; return 0;
                }
                return 0;
            });
        };
    }

    public void Present(SelectionSnapshot selection, int x, int y, string searchTemplate = WebActionPolicy.DefaultSearchTemplate,
        ActionSettings? actionSettings = null)
    {
        snapshot = selection; pointerX = x; pointerY = y; this.searchTemplate = searchTemplate;
        UiText.Refresh(this);
        settings = ActionCatalog.Normalize(actionSettings ?? new()); EndSearchInput(); query = ""; menuIndex = -1; more.Visibility = Visibility.Collapsed;
        RenderBar(); Place(); Show(); Place();
        lastPointer = Native.GetCursorPos(out var cursor) ? (cursor.X, cursor.Y) : null;
    }

    private void RenderBar()
    {
        if (snapshot == null) return;
        var dark = settings.Theme == "Dark"; var fg = dark ? Brushes.White : ActionUi.Ink;
        var surface = dark ? ActionUi.DarkSurface : Brushes.White;
        shell.Background = surface; shell.BorderBrush = dark ? ActionUi.DarkLine : ActionUi.Line;
        Resources["ControlHover"] = dark ? ActionUi.DarkHover : ActionUi.Brush("#EEF1F6");
        Resources["ControlPressed"] = dark ? ActionUi.Brush("#465167") : ActionUi.Brush("#E2E7F0");
        more.Background = surface; searchBox.Background = dark ? ActionUi.Brush("#1D2129") : ActionUi.Canvas;
        searchBox.BorderBrush = dark ? ActionUi.DarkLine : ActionUi.Line;
        searchInput.Foreground = searchInput.CaretBrush = fg;
        searchPlaceholder.Foreground = count.Foreground = dark ? ActionUi.DarkMuted : ActionUi.Muted;
        foreach (var manage in ((StackPanel)more.Child).Children.OfType<Button>())
        { manage.Background = surface; manage.BorderBrush = dark ? ActionUi.DarkLine : ActionUi.Line; ActionUi.TintButton(manage, dark); }
        row.Children.Clear(); builtinButtons.Clear();
        var chosen = ActionCatalog.GetBar(settings, snapshot);
        var shown = chosen.Select(a => a.Id).ToHashSet();
        foreach (var item in chosen.Concat(ActionCatalog.GetItems(settings).Where(a => !shown.Contains(a.Id))))
        {
            var button = ActionUi.Button(settings.ShowLabels ? UiNames.Action(item) : "", () => Dispatch(item), item.Icon);
            ActionUi.ReplaceCustomIcon(button, item);
            button.Tag = item; button.Height = 36; button.Margin = new(1); button.Padding = new(9, 6, 9, 6); button.BorderThickness = new(0);
            button.Background = Brushes.Transparent; button.Focusable = false; button.IsTabStop = false;
            ActionUi.TintButton(button, dark);
            button.ToolTip = UiNames.Action(item); AutomationProperties.SetName(button, UiNames.Action(item));
            button.Visibility = shown.Contains(item.Id) ? Visibility.Visible : Visibility.Collapsed; button.IsEnabled = ActionCatalog.IsApplicable(item, snapshot);
            if (item.BuiltIn is { } action)
            {
                AutomationProperties.SetAutomationId(button, action + "Action"); builtinButtons[action] = button;
                if (action == TextAction.Search) button.IsEnabled = WebActionPolicy.Search(snapshot.Text, searchTemplate).Success;
            }
            else AutomationProperties.SetAutomationId(button, "CustomAction_" + item.Id);
            row.Children.Add(button);
        }
        var dots = ActionUi.Button("", ToggleMore, "more"); dots.Height = 36; dots.Margin = new(2, 1, 0, 1);
        dots.Padding = new(10, 6, 10, 6); dots.Background = Brushes.Transparent; dots.BorderThickness = new(0);
        dots.ToolTip = UiText.Get("PopupWindow_003"); dots.Focusable = false; dots.IsTabStop = false;
        ((System.Windows.Shapes.Path)((Viewbox)((StackPanel)dots.Content).Children[0]).Child).Stroke = fg;
        AutomationProperties.SetName(dots, UiText.Get("PopupWindow_004")); AutomationProperties.SetAutomationId(dots, "MoreActions"); row.Children.Add(dots);
    }

    private void Dispatch(ActionItem item)
    { if (item.BuiltIn is { } action) ActionRequested?.Invoke(action); else CustomActionRequested?.Invoke(item.Id); }
    private void ToggleMore()
    {
        if (MenuOpen)
        {
            CloseMore();
            return;
        }

        more.Visibility = Visibility.Visible;
        query = "";
        menuIndex = -1;
        RenderMenu();
        Place();
        BeginSearchInput();
    }

    internal bool BeginSearchInput()
    {
        if (!IsVisible || !MenuOpen || snapshot == null) return false;
        if (!HasSearchForeground && Native.GetIdentity(Native.GetForegroundWindow()) != snapshot.Target)
        { SessionAbandoned?.Invoke(); return false; }
        searchMode = true;
        Native.SetWindowLongPtr(handle, -20, (nint)((long)Native.GetWindowLongPtr(handle, -20) & ~0x08000000L));
        if (!Activate()) { EndSearchInput(); return false; }
        searchInput.Focus(); Keyboard.Focus(searchInput); return searchInput.IsKeyboardFocused;
    }

    public bool TryRestoreSourceFocus()
    {
        if (snapshot == null) return false;
        var target = snapshot.Target;
        if (Native.GetIdentity(Native.GetForegroundWindow()) == target) { EndSearchInput(); return true; }
        if (!HasSearchForeground || Native.GetIdentity((nint)target.Window) != target) return false;
        if (!Native.SetForegroundWindow((nint)target.Window) || Native.GetIdentity(Native.GetForegroundWindow()) != target) return false;
        EndSearchInput(); return true;
    }

    private void EndSearchInput()
    {
        searchMode = false; composing = false;
        if (handle != 0) Native.SetWindowLongPtr(handle, -20, (nint)((long)Native.GetWindowLongPtr(handle, -20) | 0x08000000));
    }

    private void CloseMore()
    {
        if (HasSearchForeground && !TryRestoreSourceFocus()) SessionAbandoned?.Invoke();
        EndSearchInput(); more.Visibility = Visibility.Collapsed; query = ""; menuIndex = -1; RenderMenu(); Place();
    }

    private void UpdateQuery(string value)
    {
        if (MenuOpen && query.Length == 0 && value.Length > 0)
            unfilteredScrollOffset = menuScroll.VerticalOffset;
        var restoreScroll = query.Length > 0 && value.Length == 0;

        query = value;
        menuIndex = -1;
        if (!MenuOpen) return;
        Place();

        if (restoreScroll) menuScroll.ScrollToVerticalOffset(unfilteredScrollOffset);
    }

    private void RenderMenu()
    {
        if (snapshot == null) return;
        var dark = settings.Theme == "Dark"; var surface = dark ? ActionUi.DarkSurface : Brushes.White;
        var muted = dark ? ActionUi.DarkMuted : ActionUi.Muted;
        if (searchInput.Text != query)
        { syncingQuery = true; searchInput.Text = query; searchInput.CaretIndex = query.Length; syncingQuery = false; }
        searchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        menuRows.Children.Clear(); menuButtons.Clear();
        var barIds = ActionCatalog.GetBar(settings, snapshot).Select(a => a.Id).ToHashSet();
        var barHasRoom = settings.Layout.Count(e => e.Placement == ActionPlacement.Bar) < ActionCatalog.BarActionLimit;
        var items = UiNames.More(settings, snapshot, query).ToList();
        foreach (var button in row.Children.OfType<Button>().Where(b => b.Tag is ActionItem && b.Visibility == Visibility.Collapsed))
        {
            var item = (ActionItem)button.Tag;
            if (barIds.Contains(item.Id) && ActionCatalog.IsApplicable(item, snapshot)
                && (query.Length == 0 || UiNames.Action(item).Contains(query, StringComparison.OrdinalIgnoreCase) || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase))) items.Add(item);
        }
        var recentShown = false; var allShown = false;
        foreach (var item in items.DistinctBy(a => a.Id))
        {
            var recent = query.Length == 0 && settings.RecentIds.Contains(item.Id);
            if (recent && !recentShown || !recent && !allShown)
            {
                var heading = ActionUi.Text(recent ? UiText.Get("PopupWindow_005") : query.Length == 0 ? UiText.Get("PopupWindow_006") : UiText.Get("PopupWindow_007"), 12, muted);
                heading.Margin = new(10, 8, 0, 4); menuRows.Children.Add(heading);
                if (recent) recentShown = true; else allShown = true;
            }
            var line = new DockPanel(); var pin = ActionUi.Button("", () => Pin(item.Id), "pin");
            pin.Focusable = false; pin.IsTabStop = false; pin.BorderThickness = new(0); pin.Margin = new(0);
            pin.Background = surface; ActionUi.TintButton(pin, dark); AutomationProperties.SetName(pin, UiText.Join(UiText.Get("PopupWindow_008"), UiNames.Action(item)));
            pin.IsEnabled = barHasRoom;
            pin.ToolTip = barHasRoom ? UiText.Get("PopupWindow_009") : UiText.Get("PopupWindow_010",(ActionCatalog.BarActionLimit));
            ToolTipService.SetShowOnDisabled(pin, true);
            DockPanel.SetDock(pin, Dock.Right); line.Children.Add(pin);
            var button = ActionUi.Button(UiNames.Action(item), () => { if (ActionCatalog.IsApplicable(item, snapshot)) Dispatch(item); }, item.Icon); button.Focusable = false; button.IsTabStop = false;
            ActionUi.ReplaceCustomIcon(button, item);
            button.BorderThickness = new(0); button.Margin = new(0); AutomationProperties.SetAutomationId(button, "MoreAction_" + item.Id);
            button.Background = surface; button.HorizontalContentAlignment = HorizontalAlignment.Left; ActionUi.TintButton(button, dark);
            button.IsEnabled = ActionCatalog.IsApplicable(item, snapshot)
                && (item.BuiltIn != TextAction.Search || WebActionPolicy.Search(snapshot.Text, searchTemplate).Success);
            var reason = item.BuiltIn switch
            {
                TextAction.RevealPath => UiText.Get("PopupWindow_011"),
                TextAction.OpenLink => UiText.Get("PopupWindow_012"),
                TextAction.Search => UiText.Get("PopupWindow_013"),
                _ => UiText.Get("PopupWindow_014")
            };
            button.ToolTip = button.IsEnabled ? UiNames.Action(item) : reason; ToolTipService.SetShowOnDisabled(button, true);
            if (button.IsEnabled) menuButtons.Add(button);
            var actionContent = new StackPanel(); actionContent.Children.Add(button);
            if (!button.IsEnabled)
            {
                var unavailable = ActionUi.Text(reason, 12, muted); unavailable.Margin = new(10, 0, 6, 6);
                AutomationProperties.SetAutomationId(unavailable, "MoreActionReason_" + item.Id); actionContent.Children.Add(unavailable);
            }
            line.Children.Add(actionContent); menuRows.Children.Add(line);
        }
        if (items.Count == 0) { var empty = ActionUi.Text(query.Length == 0 ? UiText.Get("PopupWindow_015") : UiText.Get("PopupWindow_016"), 12, muted); empty.Margin = new(10, 18, 10, 18); menuRows.Children.Add(empty); }
        count.Text = UiText.Get("PopupWindow_017",(snapshot.CharacterCount));
        for (var i = 0; i < menuButtons.Count; i++) menuButtons[i].Background = i == menuIndex ? dark ? ActionUi.DarkHover : ActionUi.Pale : surface;
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
        if (key == 0x1B) { if (query.Length == 0) CloseMore(); else UpdateQuery(""); return true; }
        if (key is 0x26 or 0x28) { if (menuButtons.Count > 0) { menuIndex = (menuIndex + (key == 0x28 ? 1 : -1) + menuButtons.Count) % menuButtons.Count; RenderMenu(); } return true; }
        if (key == 0x0D) { if (menuButtons.Count > 0) menuButtons[Math.Max(0, menuIndex)].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return true; }
        var value = query;
        if (key == 0x08) { if (value.Length > 0) value = value[..^1]; }
        else if (key == 0x56 && (Native.GetAsyncKeyState(0x11) & 0x8000) != 0)
        { try { if (Clipboard.ContainsText()) { var text = Clipboard.GetText().Replace("\r", "").Replace("\n", " "); value = text[..Math.Min(128, text.Length)]; } } catch (System.Runtime.InteropServices.ExternalException) { } }
        else if ((Native.GetAsyncKeyState(0x11) & 0x8000) != 0) return false;
        else if (key is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 || key == 0x20) { if (value.Length < 128) value += (char)key; }
        else return false;
        UpdateQuery(value); return true;
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
            menuScroll.MaxHeight = Math.Max(20, Math.Min(260, area.Height / scale - 190));
            if (MenuOpen)
            {
                RenderMenu();
                // Let WPF propagate the new rows' desired size before sizing the native window.
                root.UpdateLayout();
            }
            root.Measure(new Size(Math.Max(1, Width - 14), double.PositiveInfinity)); Height = Math.Min(area.Height / scale, root.DesiredSize.Height + 14);
            var p = PopupPlacement.Place(anchor, area, Width * scale, Height * scale);
            Native.SetWindowPos(handle, new(-1), (int)p.X, (int)p.Y, (int)p.Width, (int)p.Height, 0x10);
        }
        var previous = Native.GetDpiForWindow(handle); SizeAndPlace(); if (Native.GetDpiForWindow(handle) != previous) SizeAndPlace();
    }

    public void Dismiss() { EndSearchInput(); more.Visibility = Visibility.Collapsed; query = ""; Hide(); }
    public bool Contains(int x, int y) => IsVisible && Native.GetWindowRect(handle, out var r) && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    internal bool ShouldDismissForPointer()
    {
        if (!IsVisible || snapshot == null || !Native.GetCursorPos(out var cursor))
            return false;

        var position = (cursor.X, cursor.Y);
        var moved = lastPointer is { } previous && previous != position;
        lastPointer = position;
        if (!moved || MenuOpen || HasSearchForeground || !Native.GetWindowRect(handle, out var bounds))
            return false;

        const double marginDip = 48;
        var dpi = Native.GetDpiForWindow(handle);
        var margin = marginDip * (dpi == 0 ? 96 : dpi) / 96;
        var anchor = snapshot.Bounds ?? new ScreenRect(pointerX, pointerY, 1, 1);

        // Keep the route from the selection to every button inside the dismissal boundary.
        var left = Math.Min(bounds.Left, Math.Min(anchor.X, pointerX));
        var top = Math.Min(bounds.Top, Math.Min(anchor.Y, pointerY));
        var right = Math.Max(bounds.Right, Math.Max(anchor.Right, pointerX));
        var bottom = Math.Max(bounds.Bottom, Math.Max(anchor.Bottom, pointerY));
        return cursor.X < left - margin || cursor.X > right + margin
            || cursor.Y < top - margin || cursor.Y > bottom + margin;
    }
}
