using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed record ShortcutSaveError(string Message, bool RetryKeys = false);

internal sealed class ShortcutEditorWindow : Window
{
    internal TextAction? Action { get; }
    internal bool IsRecording { get; private set; }
    internal Action<bool>? EditingChanged { get; set; }
    private readonly Func<TextAction?, HotkeySettings?, string?, ShortcutSaveError?> saveShortcut;
    private readonly Func<TextAction, IReadOnlyList<ShortcutsSettingsPage.ShortcutChoice>> choices;
    private readonly string? originalTarget;
    private HotkeySettings? draft;
    private bool validDraft, refreshing;
    private string? error;
    private readonly StackPanel keys;
    private readonly Border capture;
    private readonly TextBlock message, targetHint;
    private readonly ComboBox? target;
    private readonly Button save, manage;

    internal ShortcutEditorWindow(TextAction? action, HotkeySettings? key, Func<TextAction, string?> readTarget,
        Func<TextAction, IReadOnlyList<ShortcutsSettingsPage.ShortcutChoice>> choices,
        Func<TextAction?, HotkeySettings?, string?, ShortcutSaveError?> saveShortcut, Action<TextAction> manageTarget, bool unavailable)
    {
        Action = action; draft = key; validDraft = key != null;
        error = unavailable ? UiText.Get("GeneralSettingsPage_048") : null;
        this.choices = choices; this.saveShortcut = saveShortcut;
        originalTarget = action is TextAction.Ai or TextAction.Custom ? readTarget(action.Value) : null;
        ActionUi.Setup(this, UiText.Get("Shortcuts_EditTitle", ShortcutsSettingsPage.ActionName(action)), 500, 340);
        MinWidth = 420; MinHeight = 260; MaxWidth = 600;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "ShortcutEditorWindow");
        var body = new StackPanel { Margin = new(24) };
        body.Children.Add(ActionUi.Heading(ShortcutsSettingsPage.ActionName(action), 20));
        var help = ActionUi.Text(UiText.Get(action switch {
            TextAction.Ai => "Shortcuts_AiHelp", TextAction.Custom => "Shortcuts_CustomHelp",
            TextAction.Lookup => "Shortcuts_LookupHelp", TextAction.Ocr => "Shortcuts_OcrHelp", _ => "Shortcuts_KeyHelp"
        }), 13, ActionUi.Muted);
        help.Margin = new(0, 6, 0, 0); body.Children.Add(help);
        if (action is TextAction.Ai or TextAction.Custom)
        {
            var label = ActionUi.Text(UiText.Get(action == TextAction.Ai ? "GeneralSettings_ShortcutPrompt" : "GeneralSettings_ShortcutCustomItem"), 14);
            label.Margin = new(0, 18, 0, 0);
            body.Children.Add(label);
            target = new ComboBox { DisplayMemberPath = nameof(ShortcutsSettingsPage.ShortcutChoice.Name), Margin = new(0, 6, 0, 0) };
            AutomationProperties.SetAutomationId(target, "ShortcutItem");
            AutomationProperties.SetName(target, label.Text); AutomationProperties.SetLabeledBy(target, label);
            body.Children.Add(target);
        }
        targetHint = ActionUi.Text("", 13, ActionUi.Muted); targetHint.Margin = new(0, 8, 0, 0); targetHint.Visibility = Visibility.Collapsed;
        body.Children.Add(targetHint);
        manage = ActionUi.Button(UiText.Get("Shortcuts_CreateItem"), () => { Close(); manageTarget(action!.Value); });
        manage.Style = (Style)FindResource("AiSettingsLink"); manage.HorizontalAlignment = HorizontalAlignment.Left;
        manage.Content = UiText.Get("Shortcuts_CreateItem"); manage.Foreground = ActionUi.Accent; manage.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(manage, "ShortcutManageTarget"); body.Children.Add(manage);
        var keyLabel = ActionUi.Text(UiText.Get("Shortcuts_KeyLabel"), 14); keyLabel.Margin = new(0, 16, 0, 6); body.Children.Add(keyLabel);
        keys = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(keys, "ShortcutDraftKeys");
        capture = new Border { Child = keys, Background = ActionUi.Canvas, BorderBrush = ActionUi.Line,
            BorderThickness = new(1), CornerRadius = new(8), Padding = new(12), MinHeight = 64,
            Focusable = true, FocusVisualStyle = null, Cursor = Cursors.Hand };
        AutomationProperties.SetAutomationId(capture, "ShortcutCapture");
        AutomationProperties.SetName(capture, UiText.Get("GeneralSettingsPage_019")); AutomationProperties.SetLabeledBy(capture, keyLabel);
        capture.MouseLeftButtonDown += (_, e) => { capture.Focus(); BeginRecording(); e.Handled = true; };
        capture.GotKeyboardFocus += (_, _) => BeginRecording();
        capture.LostKeyboardFocus += (_, _) => StopRecording();
        capture.PreviewKeyDown += CaptureKey;
        body.Children.Add(capture);
        message = ActionUi.Text("", 13, ActionUi.Muted);
        message.Margin = new(0, 10, 0, 0); AutomationProperties.SetAutomationId(message, "ShortcutStatus"); body.Children.Add(message);
        var footer = new DockPanel { Margin = new(0, 22, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button(UiText.Get("GeneralSettingsPage_022"), "CancelShortcut", Close); cancel.IsCancel = true;
        save = Button(UiText.Get("GeneralSettingsPage_021"), "SaveShortcut", () => Apply(draft, TargetId), primary: true); save.IsDefault = true;
        save.Margin = new(8, 0, 0, 0); buttons.Children.Add(cancel); buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        var reset = Button(UiText.Get(action == null ? "GeneralSettingsPage_046" : "Shortcuts_RemoveBinding"), "ResetShortcut",
            () => Apply(action == null ? HotkeySettings.Default : null, null));
        reset.Style = (Style)FindResource("AiSettingsLink"); reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Content = UiText.Get(action == null ? "GeneralSettingsPage_046" : "Shortcuts_RemoveBinding"); reset.Foreground = ActionUi.Accent;
        reset.Visibility = action == null ? key == HotkeySettings.Default ? Visibility.Collapsed : Visibility.Visible
            : key == null && originalTarget == null ? Visibility.Collapsed : Visibility.Visible;
        footer.Children.Add(reset); body.Children.Add(footer);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        WindowWorkArea.Attach(this);
        if (target != null)
        {
            RefreshChoices(originalTarget);
            target.SelectionChanged += (_, _) => { if (!refreshing && !UiText.IsRefreshing) { error = null; UpdateState(); } };
            target.DropDownClosed += (_, _) => capture.Focus();
        }
        UpdateState();
        Loaded += (_, _) => capture.Focus();
        Activated += (_, _) => { EditingChanged?.Invoke(true); if (capture.IsKeyboardFocusWithin) BeginRecording(); };
        Deactivated += (_, _) => { StopRecording(); EditingChanged?.Invoke(false); };
        Closed += (_, _) => { StopRecording(); EditingChanged?.Invoke(false); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    private string? TargetId => (target?.SelectedItem as ShortcutsSettingsPage.ShortcutChoice)?.Id;
    private bool TargetReady => target == null || !string.IsNullOrEmpty(TargetId);
    private static Button Button(string label, string id, Action click, bool primary = false)
    {
        var button = ActionUi.Button(label, click, primary: primary);
        AutomationProperties.SetAutomationId(button, id); return button;
    }

    private void RefreshChoices(string? selectedId)
    {
        if (target == null || Action is not { } action) return;
        var available = choices(action);
        var selected = available.FirstOrDefault(item => item.Id == selectedId);
        var placeholder = new ShortcutsSettingsPage.ShortcutChoice("", UiText.Get(selectedId == null || selectedId == ""
            ? "GeneralSettings_ShortcutChooseItem" : "Shortcuts_TargetUnavailable"));
        refreshing = true;
        try { target.ItemsSource = new[] { placeholder }.Concat(available).ToArray(); target.SelectedItem = selected ?? placeholder; }
        finally { refreshing = false; }
        var empty = available.Count == 0;
        targetHint.Text = empty ? UiText.Get("Shortcuts_NoItems") : "";
        targetHint.Visibility = manage.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateState()
    {
        save.IsEnabled = TargetReady && validDraft && draft != null;
        RenderKeys(keys, draft);
        if (draft == null)
        {
            var prompt = UiText.Get(IsRecording ? "Shortcuts_RecordingPrompt" : "Shortcuts_ClickToRecord");
            keys.Children.Clear(); keys.Children.Add(ActionUi.Text(prompt, 14, IsRecording ? ActionUi.Accent : ActionUi.Muted));
            AutomationProperties.SetName(keys, prompt);
        }
        message.Foreground = error == null ? ActionUi.Muted : Brushes.Firebrick;
        message.Text = error != null ? UiText.Current(error) : UiText.Get(
            validDraft && draft != null ? TargetReady ? "Shortcuts_Ready" : "Shortcuts_NeedTarget"
            : TargetReady ? "Shortcuts_NeedKey" : "Shortcuts_NeedBoth");
    }

    internal void BeginRecording()
    {
        if (!IsActive) return;
        IsRecording = true; capture.BorderBrush = ActionUi.Accent;
        UpdateState();
    }

    private void StopRecording() { IsRecording = false; capture.BorderBrush = ActionUi.Line; UpdateState(); }

    internal void Record(HotkeySettings candidate)
    {
        if (!IsRecording) return;
        validDraft = candidate.IsValid;
        if (!validDraft) { Error(UiText.Get("GeneralSettingsPage_042")); UpdateState(); return; }
        draft = candidate; error = null; UpdateState();
    }

    private void CaptureKey(object sender, KeyEventArgs e)
    {
        if (!IsRecording) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return;
        e.Handled = true;
        if (key == Key.Escape) { Close(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        Record(new(((modifiers & ModifierKeys.Control) != 0 ? 2 : 0) | ((modifiers & ModifierKeys.Alt) != 0 ? 1 : 0)
            | ((modifiers & ModifierKeys.Shift) != 0 ? 4 : 0) | ((modifiers & ModifierKeys.Windows) != 0 ? 8 : 0), KeyInterop.VirtualKeyFromKey(key)));
    }

    private void Apply(HotkeySettings? key, string? targetId)
    {
        var error = saveShortcut(Action, key, targetId);
        if (error != null)
        {
            Error(error.Message);
            if (error.RetryKeys && IsActive)
            {
                capture.Focus();
                BeginRecording();
            }
            return;
        }
        Close();
    }
    private void Error(string text) { error = text; UpdateState(); }

    internal void RefreshLanguage()
    {
        UiText.Refresh(this); RefreshChoices(TargetId); UpdateState();
    }

    internal static void RenderKeys(StackPanel host, HotkeySettings? key)
    {
        host.Children.Clear(); AutomationProperties.SetName(host, key?.Display ?? UiText.Get("Shortcuts_NotSet"));
        if (key == null) { host.Children.Add(ActionUi.Text(UiText.Get("Shortcuts_NotSet"), 13, ActionUi.Muted)); return; }
        foreach (var part in key.Parts)
        {
            if (host.Children.Count > 0) { var plus = ActionUi.Text("+", 12, ActionUi.Muted); plus.Margin = new(5, 0, 5, 0); host.Children.Add(plus); }
            host.Children.Add(new Border { Child = ActionUi.Text(part, 13), Background = ActionUi.Canvas, BorderBrush = ActionUi.Line,
                BorderThickness = new(1, 1, 1, 2), CornerRadius = new(4), Padding = new(8, 5, 8, 5) });
        }
    }
}
