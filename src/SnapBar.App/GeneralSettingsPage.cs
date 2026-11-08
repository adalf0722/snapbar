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
    private readonly Func<TextAction?, HotkeySettings?, string?> saveHotkey;
    private readonly Func<TextAction?, HotkeySettings?> readHotkey;
    private readonly Func<TextAction, bool> isActionRegistered;
    private readonly ComboBox shortcutTarget;
    private readonly StartupRegistration startup;
    private HotkeySettings? current, draft;
    private TextAction? SelectedAction => shortcutTarget.SelectedIndex switch
    { 1 => TextAction.Search, 2 => TextAction.Copy, 3 => TextAction.Ocr, _ => null };
    private bool updatingStartup;
    private ComboBox? languageSelector;
    private TextBlock? languageStatus;
    private bool updatingLanguage;

    internal void ConfigureLanguage(string preference, Func<string, bool> saveLanguage)
    {
        var row = new StackPanel();
        var heading = new DockPanel();
        languageSelector = new ComboBox { ItemsSource = new[] { UiText.Get("Language_OptionSystem"),
                UiText.Get("Language_OptionTraditionalChinese"), UiText.Get("Language_OptionEnglish") },
            SelectedIndex = LanguageIndex(preference), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 200, Margin = new(16, 0, 0, 0) };
        Identify(languageSelector, "InterfaceLanguageSelector", UiText.Get("Language_Label"));
        languageStatus = Description(""); Identify(languageStatus, "InterfaceLanguageStatus", UiText.Get("Language_Status"));
        languageStatus.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(languageSelector, Dock.Right); heading.Children.Add(languageSelector);
        heading.Children.Add(ActionUi.Heading(UiText.Get("Language_Label"), 16)); row.Children.Add(heading); row.Children.Add(languageStatus);
        var card = ActionUi.Card(row); card.Margin = new(0, 0, 0, 14);
        ((StackPanel)scroll.Content).Children.Insert(0, card);
        languageSelector.SelectionChanged += (_, _) =>
        {
            if (updatingLanguage || UiText.IsRefreshing) return;
            var proposed = languageSelector.SelectedIndex switch { 1 => "zh-Hant", 2 => "en", _ => "system" };
            if (saveLanguage(proposed)) { preference = proposed; languageStatus.Text = ""; languageStatus.Visibility = Visibility.Collapsed; return; }
            updatingLanguage = true;
            try { languageSelector.SelectedIndex = LanguageIndex(preference); languageStatus.Text = UiText.Get("Language_SaveFailed"); languageStatus.Visibility = Visibility.Visible; }
            finally { updatingLanguage = false; }
        };
    }

    private static int LanguageIndex(string preference) => preference switch { "zh-Hant" => 1, "en" => 2, _ => 0 };

    internal GeneralSettingsPage(bool automatic, Action toggle, HotkeySettings hotkey,
        Func<TextAction?, HotkeySettings?, string?> saveHotkey, Func<TextAction?, HotkeySettings?> readHotkey,
        Func<TextAction, bool> isActionRegistered, StartupRegistration startup,
        bool ocrPreferEnglish, Action<bool> setOcrLanguagePriority)
    {
        this.saveHotkey = saveHotkey; this.readHotkey = readHotkey; this.isActionRegistered = isActionRegistered;
        this.startup = startup; current = draft = hotkey;
        ActionUi.LoadStyles(this);
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var panel = new StackPanel();
        var launch = new DockPanel();
        startupToggle = new CheckBox { Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
        Identify(startupToggle, "StartupToggle", UiText.Get("GeneralSettingsPage_000"));
        startupToggle.Checked += (_, _) => ChangeStartup(true); startupToggle.Unchecked += (_, _) => ChangeStartup(false);
        DockPanel.SetDock(startupToggle, Dock.Right); launch.Children.Add(startupToggle);
        var launchTitle = new StackPanel(); launchTitle.Children.Add(ActionUi.Heading(UiText.Get("GeneralSettingsPage_001"), 16));
        startupMessage = Description(UiText.Get("GeneralSettingsPage_002"));
        Identify(startupMessage, "StartupStatus", UiText.Get("GeneralSettingsPage_003")); launchTitle.Children.Add(startupMessage);
        startupSettings = ActionUi.Button(UiText.Get("GeneralSettingsPage_004"), OpenStartupSettings);
        Identify(startupSettings, "StartupWindowsSettings", UiText.Get("GeneralSettingsPage_005"));
        startupSettings.HorizontalAlignment = HorizontalAlignment.Left; launchTitle.Children.Add(startupSettings);
        retryStartup = ActionUi.Button(UiText.Get("GeneralSettingsPage_006"), RefreshStartup); Identify(retryStartup, "StartupRetry", UiText.Get("GeneralSettingsPage_007"));
        retryStartup.HorizontalAlignment = HorizontalAlignment.Left; launchTitle.Children.Add(retryStartup);
        launch.Children.Add(launchTitle); AddCard(panel, launch);

        var detection = new DockPanel();
        AutomaticToggle = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = automatic,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = UiText.Get("GeneralSettingsPage_008") };
        Identify(AutomaticToggle, "AutomaticDetectionToggle", UiText.Get("GeneralSettingsPage_009"));
        AutomaticToggle.Click += (_, _) => toggle(); DockPanel.SetDock(AutomaticToggle, Dock.Right); detection.Children.Add(AutomaticToggle);
        var title = new StackPanel(); title.Children.Add(ActionUi.Heading(UiText.Get("GeneralSettingsPage_010"), 16));
        title.Children.Add(Description(UiText.Get("GeneralSettingsPage_011"))); detection.Children.Add(title); AddCard(panel, detection);

        var shortcut = new StackPanel();
        shortcutTarget = new ComboBox { ItemsSource = new[] { UiText.Get("GeneralSettingsPage_012"), UiText.Get("GeneralSettingsPage_013"), UiText.Get("GeneralSettingsPage_014"), UiText.Get("GeneralSettingsPage_015") },
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 10) };
        Identify(shortcutTarget, "ShortcutTarget", UiText.Get("GeneralSettingsPage_016"));
        shortcut.Children.Add(shortcutTarget);
        var shortcutRow = new Grid();
        shortcutRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        shortcutRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var shortcutTitle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 20, 0) };
        shortcutTitle.Children.Add(ActionUi.Heading(UiText.Get("GeneralSettingsPage_017"), 16));
        shortcutTitle.Children.Add(Description(UiText.Get("GeneralSettingsPage_018"))); shortcutRow.Children.Add(shortcutTitle);
        var shortcutControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(shortcutControls, 1); shortcutRow.Children.Add(shortcutControls); shortcut.Children.Add(shortcutRow);
        keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Identify(keys, "GeneralShortcutKeys", hotkey.Display);
        capture = new Border { Child = keys, Padding = new(0, 4, 0, 4), Margin = new(0, 0, 12, 0),
            BorderBrush = SystemColors.HighlightBrush, BorderThickness = new(0), VerticalAlignment = VerticalAlignment.Center };
        Identify(capture, "ShortcutCapture", UiText.Get("GeneralSettingsPage_019"));
        capture.PreviewKeyDown += CaptureKey;
        capture.GotKeyboardFocus += (_, _) => capture.BorderThickness = new(0, 0, 0, 2);
        capture.LostKeyboardFocus += (_, _) => capture.BorderThickness = new(0);
        shortcutControls.Children.Add(capture);
        var buttons = new WrapPanel();
        change = MakeButton(UiText.Get("GeneralSettingsPage_020"), "ChangeShortcut", BeginRecording);
        save = MakeButton(UiText.Get("GeneralSettingsPage_021"), "SaveShortcut", SaveRecording);
        cancel = MakeButton(UiText.Get("GeneralSettingsPage_022"), "CancelShortcut", CancelRecording);
        reset = MakeButton(UiText.Get("GeneralSettingsPage_023"), "ResetShortcut", () => Apply(SelectedAction == null ? HotkeySettings.Default : null));
        change.Margin = new(0); reset.Margin = new(8, 0, 0, 0);
        shortcutControls.Children.Add(change); shortcutControls.Children.Add(reset);
        foreach (var button in new[] { save, cancel }) { button.Margin = new(0, 10, 8, 0); buttons.Children.Add(button); }
        shortcutDetails = new StackPanel { Margin = new(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        Identify(shortcutDetails, "ShortcutEditor", UiText.Get("GeneralSettingsPage_024"));
        shortcutMessage = Description("");
        Identify(shortcutMessage, "ShortcutStatus", UiText.Get("GeneralSettingsPage_025")); shortcutDetails.Children.Add(shortcutMessage);
        shortcutDetails.Children.Add(buttons); shortcut.Children.Add(shortcutDetails); AddCard(panel, shortcut);
        var ocr = new DockPanel();
        var ocrLanguage = new ComboBox { ItemsSource = new[] { UiText.Get("GeneralSettingsPage_026"), UiText.Get("GeneralSettingsPage_027") },
            SelectedIndex = ocrPreferEnglish ? 1 : 0, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = UiText.Get("GeneralSettingsPage_028") };
        Identify(ocrLanguage, "OcrLanguagePriority", UiText.Get("GeneralSettingsPage_029"));
        ocrLanguage.SelectionChanged += (_, _) => { if (!UiText.IsRefreshing) setOcrLanguagePriority(ocrLanguage.SelectedIndex == 1); };
        DockPanel.SetDock(ocrLanguage, Dock.Right);
        ocr.Children.Add(ocrLanguage);
        var ocrTitle = ActionUi.Heading(UiText.Get("GeneralSettingsPage_030"), 16);
        ocrTitle.Margin = new(0, 0, 12, 0);
        ocrTitle.VerticalAlignment = VerticalAlignment.Center;
        ocr.Children.Add(ocrTitle);
        AddCard(panel, ocr);
        scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Identify(scroll, "GeneralSettingsContent", UiText.Get("GeneralSettingsPage_031")); Content = scroll;
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelRecording(); };
        shortcutTarget.SelectionChanged += (_, _) => { if (!UiText.IsRefreshing) RefreshShortcut(); };
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
            StartupState.On => UiText.Get("GeneralSettingsPage_032"),
            StartupState.DisabledByWindows => UiText.Get("GeneralSettingsPage_033"),
            StartupState.Unavailable => UiText.Get("GeneralSettingsPage_034"),
            _ => status.DifferentLocation ? UiText.Get("GeneralSettingsPage_035") : UiText.Get("GeneralSettingsPage_036")
        };
    }

    private void ChangeStartup(bool enabled)
    {
        if (updatingStartup) return;
        try
        {
            var status = startup.SetEnabled(enabled); RefreshStartup();
            if (enabled && status.State is not (StartupState.On or StartupState.DisabledByWindows))
                startupMessage.Text = UiText.Get("GeneralSettingsPage_037");
        }
        catch (StartupLaunchException)
        { RefreshStartup(); startupMessage.Text = UiText.Get("GeneralSettingsPage_038"); }
        catch (Exception e) when (StartupRegistration.IsAccessError(e))
        { RefreshStartup(); startupMessage.Text = UiText.Get("GeneralSettingsPage_039"); }
    }

    private void OpenStartupSettings()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { startupMessage.Text = UiText.Get("GeneralSettingsPage_040"); }
    }

    internal void BeginRecording()
    {
        draft = current; IsRecording = true; save.IsEnabled = false;
        RecordingChanged?.Invoke(true); UpdateEditing();
        shortcutMessage.Foreground = DescriptionInk; shortcutMessage.Text = UiText.Get("GeneralSettingsPage_041");
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
        if (!candidate.IsValid) { Error(UiText.Get("GeneralSettingsPage_042")); return; }
        draft = candidate; RenderKeys(draft); shortcutMessage.Foreground = DescriptionInk; shortcutMessage.Text = UiText.Get("GeneralSettingsPage_043");
    }

    private void SaveRecording() { if (IsRecording && save.IsEnabled) Apply(draft); }
    private void Apply(HotkeySettings? candidate)
    {
        var error = saveHotkey(SelectedAction, candidate);
        if (error != null) { Error(error); return; }
        current = candidate; CancelRecording(); RenderKeys(current); UpdateEditing();
        shortcutMessage.Text = ""; shortcutDetails.Visibility = Visibility.Collapsed;
        AutomationProperties.SetHelpText(capture, UiText.Join(UiText.Get("GeneralSettingsPage_044"), (current?.Display ?? UiText.Get("GeneralSettingsPage_045"))));
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
        shortcutTarget.IsEnabled = !IsRecording;
        reset.Content = SelectedAction == null ? UiText.Get("GeneralSettingsPage_046") : UiText.Get("GeneralSettingsPage_047");
        AutomationProperties.SetName(reset, (string)reset.Content);
        reset.Visibility = IsRecording || current == null || (SelectedAction == null && current == HotkeySettings.Default)
            ? Visibility.Collapsed : Visibility.Visible;
        capture.Focusable = IsRecording;
        shortcutDetails.Visibility = IsRecording ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Error(string text)
    { shortcutMessage.Foreground = Brushes.Firebrick; shortcutMessage.Text = text; shortcutDetails.Visibility = Visibility.Visible; }
    internal void RefreshShortcut()
    {
        if (IsRecording) return;
        current = readHotkey(SelectedAction);
        RenderKeys(current);
        UpdateEditing();
        if (current != null && SelectedAction is { } action && !isActionRegistered(action))
            Error(UiText.Get("GeneralSettingsPage_048"));
    }

    private void RenderKeys(HotkeySettings? value)
    {
        keys.Children.Clear(); AutomationProperties.SetName(keys, value?.Display ?? UiText.Get("GeneralSettingsPage_049"));
        if (value == null) { keys.Children.Add(Description(UiText.Get("GeneralSettingsPage_050"))); return; }
        foreach (var key in value.Parts)
        {
            if (keys.Children.Count > 0) { var plus = ActionUi.Text("+", 12, ActionUi.Muted); plus.Margin = new(6, 0, 6, 0); keys.Children.Add(plus); }
            keys.Children.Add(new Border { Child = ActionUi.Text(key, 13), Background = ActionUi.Canvas, BorderBrush = ActionUi.Line,
                BorderThickness = new(1, 1, 1, 2), CornerRadius = new(4), Padding = new(8, 5, 8, 5) });
        }
    }
    internal void SetCompact(bool compact)
    {
        foreach (var card in ((StackPanel)scroll.Content).Children.OfType<Border>())
        {
            card.Padding = compact ? new(12, 8, 12, 8) : new(20);
            card.Margin = compact ? new(0, 0, 0, 8) : new(0, 0, 0, 14);
        }
    }
}
