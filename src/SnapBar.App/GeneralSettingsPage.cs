using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class GeneralSettingsPage : ContentControl
{
    internal CheckBox AutomaticToggle { get; }
    internal bool IsRecording { get; private set; }
    internal Action<bool>? RecordingChanged { get; set; }
    private readonly ScrollViewer scroll;
    private readonly CheckBox startupToggle;
    private readonly TextBlock startupMessage, shortcutMessage;
    private readonly Button startupSettings, retryStartup, change, save, cancel, reset;
    private readonly StackPanel keys;
    private readonly StackPanel shortcutDetails;
    private readonly Border capture;
    private readonly Func<HotkeySettings, string?> saveHotkey;
    private readonly StartupRegistration startup;
    private HotkeySettings current, draft;
    private bool updatingStartup;

    internal GeneralSettingsPage(bool automatic, Action toggle, HotkeySettings hotkey,
        Func<HotkeySettings, string?> saveHotkey, StartupRegistration startup)
    {
        this.saveHotkey = saveHotkey; this.startup = startup; current = draft = hotkey;
        ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var panel = new StackPanel();
        var launch = new DockPanel();
        startupToggle = new CheckBox { Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
        Identify(startupToggle, "StartupToggle", "登入 Windows 後自動啟動");
        startupToggle.Checked += (_, _) => ChangeStartup(true); startupToggle.Unchecked += (_, _) => ChangeStartup(false);
        DockPanel.SetDock(startupToggle, Dock.Right); launch.Children.Add(startupToggle);
        var launchTitle = new StackPanel(); launchTitle.Children.Add(ActionUi.Heading("登入 Windows 後自動啟動", 16));
        startupMessage = Description("登入後在系統匣待命。");
        Identify(startupMessage, "StartupStatus", "登入啟動狀態"); launchTitle.Children.Add(startupMessage);
        startupSettings = ActionUi.Button("開啟 Windows 設定", OpenStartupSettings);
        Identify(startupSettings, "StartupWindowsSettings", "開啟 Windows 啟動設定");
        startupSettings.HorizontalAlignment = HorizontalAlignment.Left; launchTitle.Children.Add(startupSettings);
        retryStartup = ActionUi.Button("重試", RefreshStartup); Identify(retryStartup, "StartupRetry", "重新讀取登入啟動狀態");
        retryStartup.HorizontalAlignment = HorizontalAlignment.Left; launchTitle.Children.Add(retryStartup);
        launch.Children.Add(launchTitle); AddCard(panel, launch);

        var detection = new DockPanel();
        AutomaticToggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = automatic,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "自動偵測選取文字" };
        Identify(AutomaticToggle, "AutomaticDetectionToggle", "自動偵測選取文字");
        AutomaticToggle.Click += (_, _) => toggle(); DockPanel.SetDock(AutomaticToggle, Dock.Right); detection.Children.Add(AutomaticToggle);
        var title = new StackPanel(); title.Children.Add(ActionUi.Heading("自動顯示動作列", 16));
        title.Children.Add(Description("拖曳或雙擊選字後，顯示動作列。")); detection.Children.Add(title); AddCard(panel, detection);

        var shortcut = new StackPanel();
        var shortcutRow = new Grid();
        shortcutRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        shortcutRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var shortcutTitle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 20, 0) };
        shortcutTitle.Children.Add(ActionUi.Heading("叫出動作列", 16));
        shortcutTitle.Children.Add(Description("選取文字後，按快捷鍵顯示。")); shortcutRow.Children.Add(shortcutTitle);
        var shortcutControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(shortcutControls, 1); shortcutRow.Children.Add(shortcutControls); shortcut.Children.Add(shortcutRow);
        keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Identify(keys, "GeneralShortcutKeys", hotkey.Display);
        capture = new Border { Child = keys, Padding = new(0, 4, 0, 4), Margin = new(0, 0, 12, 0),
            BorderBrush = SystemColors.HighlightBrush, BorderThickness = new(0), VerticalAlignment = VerticalAlignment.Center };
        Identify(capture, "ShortcutCapture", "快捷鍵組合；更換時在此按下新組合");
        capture.PreviewKeyDown += CaptureKey;
        capture.GotKeyboardFocus += (_, _) => capture.BorderThickness = new(0, 0, 0, 2);
        capture.LostKeyboardFocus += (_, _) => capture.BorderThickness = new(0);
        shortcutControls.Children.Add(capture);
        var buttons = new WrapPanel();
        change = MakeButton("更換", "ChangeShortcut", BeginRecording);
        save = MakeButton("儲存", "SaveShortcut", SaveRecording);
        cancel = MakeButton("取消", "CancelShortcut", CancelRecording);
        reset = MakeButton("恢復預設", "ResetShortcut", () => Apply(HotkeySettings.Default));
        change.Margin = new(0); reset.Margin = new(8, 0, 0, 0);
        shortcutControls.Children.Add(change); shortcutControls.Children.Add(reset);
        foreach (var button in new[] { save, cancel }) { button.Margin = new(0, 10, 8, 0); buttons.Children.Add(button); }
        shortcutDetails = new StackPanel { Margin = new(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        Identify(shortcutDetails, "ShortcutEditor", "快捷鍵編輯");
        shortcutMessage = Description("");
        Identify(shortcutMessage, "ShortcutStatus", "快捷鍵設定狀態"); shortcutDetails.Children.Add(shortcutMessage);
        shortcutDetails.Children.Add(buttons); shortcut.Children.Add(shortcutDetails); AddCard(panel, shortcut);
        scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Identify(scroll, "GeneralSettingsContent", "一般設定內容"); Content = scroll;
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelRecording(); };
        PreviewKeyDown += (_, e) =>
        {
            if (IsRecording && e.Key == Key.Escape) { CancelRecording(); change.Focus(); e.Handled = true; }
        };
        RenderKeys(current); UpdateEditing(); RefreshStartup();
    }

    private static void Identify(DependencyObject control, string id, string name)
    { AutomationProperties.SetAutomationId(control, id); AutomationProperties.SetName(control, name); }
    private static Button MakeButton(string text, string id, Action action)
    { var button = ActionUi.Button(text, action); Identify(button, id, text); return button; }
    private static readonly Brush DescriptionInk = ActionUi.Brush("#4F5968");
    private static TextBlock Description(string text) => ActionUi.Text(text, 14, DescriptionInk);
    private static void AddCard(Panel panel, UIElement content)
    { var card = ActionUi.Card(content); card.Margin = new(0, 0, 0, 14); panel.Children.Add(card); }

    internal void RefreshStartup()
    {
        var status = startup.Read(); updatingStartup = true;
        try { startupToggle.IsChecked = status.State == StartupState.On; startupToggle.IsEnabled = status.State != StartupState.Unavailable; }
        finally { updatingStartup = false; }
        startupSettings.Visibility = status.State == StartupState.DisabledByWindows ? Visibility.Visible : Visibility.Collapsed;
        retryStartup.Visibility = status.State == StartupState.Unavailable ? Visibility.Visible : Visibility.Collapsed;
        startupMessage.Text = status.State switch
        {
            StartupState.On => "登入後在系統匣待命。",
            StartupState.DisabledByWindows => "已由 Windows 停用，請到 Windows 設定開啟。",
            StartupState.Unavailable => "無法讀取登入啟動狀態，請重試。",
            _ => status.DifferentLocation ? "程式位置已變更，開啟以更新登入啟動位置。" : "登入後在系統匣待命。"
        };
    }

    private void ChangeStartup(bool enabled)
    {
        if (updatingStartup) return;
        try
        {
            var status = startup.SetEnabled(enabled); RefreshStartup();
            if (enabled && status.State is not (StartupState.On or StartupState.DisabledByWindows))
                startupMessage.Text = "未能確認登入啟動已開啟，請重試。";
        }
        catch (StartupLaunchException)
        { RefreshStartup(); startupMessage.Text = "此版本無法在登入時獨立啟動，請改用包含執行環境的完整套件。"; }
        catch (Exception e) when (StartupRegistration.IsAccessError(e))
        { RefreshStartup(); startupMessage.Text = "無法變更登入啟動設定，請檢查權限後重試。"; }
    }

    private void OpenStartupSettings()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { startupMessage.Text = "無法開啟 Windows 設定，請手動開啟「應用程式 → 啟動」。"; }
    }

    internal void BeginRecording()
    {
        draft = current; IsRecording = true; save.IsEnabled = false;
        RecordingChanged?.Invoke(true); UpdateEditing();
        shortcutMessage.Foreground = DescriptionInk; shortcutMessage.Text = "按下新組合（Ctrl 或 Alt 加字母、數字、F1–F11），再按儲存。Esc 可取消。";
        capture.Focus();
    }

    private void CaptureKey(object sender, KeyEventArgs e)
    {
        if (!IsRecording) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return;
        e.Handled = true;
        if (key == Key.Escape) { CancelRecording(); change.Focus(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        var nativeModifiers = ((modifiers & ModifierKeys.Control) != 0 ? 2 : 0)
            | ((modifiers & ModifierKeys.Alt) != 0 ? 1 : 0) | ((modifiers & ModifierKeys.Shift) != 0 ? 4 : 0)
            | ((modifiers & ModifierKeys.Windows) != 0 ? 8 : 0);
        Record(new(nativeModifiers, KeyInterop.VirtualKeyFromKey(key)));
    }

    internal void Record(HotkeySettings candidate)
    {
        if (!IsRecording) return;
        save.IsEnabled = candidate.IsValid;
        if (!candidate.IsValid) { Error("請使用 Ctrl 或 Alt 加字母、數字或 F1–F11，避開編輯與系統保留快捷鍵。"); return; }
        draft = candidate; RenderKeys(draft); shortcutMessage.Foreground = DescriptionInk; shortcutMessage.Text = "按儲存套用，或繼續按下其他組合。";
    }

    private void SaveRecording() { if (IsRecording && save.IsEnabled) Apply(draft); }
    private void Apply(HotkeySettings candidate)
    {
        var error = saveHotkey(candidate);
        if (error != null) { Error(error); return; }
        current = candidate; CancelRecording(); RenderKeys(current); UpdateEditing();
        shortcutMessage.Text = ""; shortcutDetails.Visibility = Visibility.Collapsed;
        AutomationProperties.SetHelpText(capture, "已儲存 · " + current.Display);
        change.Focus();
    }
    internal void CancelRecording()
    {
        if (!IsRecording) return;
        IsRecording = false; RecordingChanged?.Invoke(false); RenderKeys(current); UpdateEditing(); shortcutMessage.Text = "";
    }
    private void UpdateEditing()
    {
        change.Visibility = IsRecording ? Visibility.Collapsed : Visibility.Visible;
        save.Visibility = cancel.Visibility = IsRecording ? Visibility.Visible : Visibility.Collapsed;
        reset.Visibility = IsRecording || current == HotkeySettings.Default ? Visibility.Collapsed : Visibility.Visible;
        capture.Focusable = IsRecording;
        shortcutDetails.Visibility = IsRecording ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Error(string text)
    { shortcutMessage.Foreground = Brushes.Firebrick; shortcutMessage.Text = text; shortcutDetails.Visibility = Visibility.Visible; }
    private void RenderKeys(HotkeySettings value)
    {
        keys.Children.Clear(); AutomationProperties.SetName(keys, value.Display);
        foreach (var key in value.Parts)
        {
            if (keys.Children.Count > 0) { var plus = ActionUi.Text("+", 12, ActionUi.Muted); plus.Margin = new(6, 0, 6, 0); keys.Children.Add(plus); }
            keys.Children.Add(new Border { Child = ActionUi.Text(key, 13), Background = ActionUi.Canvas, BorderBrush = ActionUi.Line,
                BorderThickness = new(1, 1, 1, 2), CornerRadius = new(4), Padding = new(8, 5, 8, 5) });
        }
    }
    internal void SetCompact(bool compact) => scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
}
