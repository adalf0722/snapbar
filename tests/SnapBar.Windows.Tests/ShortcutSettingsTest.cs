using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class ShortcutSettingsTest
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Children<T>(child)) yield return descendant;
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Children<T>(root).Single(item => AutomationProperties.GetAutomationId(item) == id);
    private static void Click(DependencyObject root, string id)
    {
        var button = Find<Button>(root, id); button.Focus(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    internal static async Task Run(MainWindow window, string path, Action<string, bool> check)
    {
        var page = window.ShortcutsPage;
        async Task<ShortcutEditorWindow> Open(TextAction? action = null)
        {
            window.NavigateTo(SettingsCategory.Shortcuts); page.Edit(action);
            var editor = page.Editor!; editor.Activate();
            var until = DateTime.UtcNow.AddSeconds(60);
            while (!editor.IsActive && DateTime.UtcNow < until) await Task.Delay(25);
            if (!editor.IsActive) throw new InvalidOperationException("Activate the shortcut editor fixture");
            if (Find<Border>(editor, "ShortcutCapture").IsEnabled) Find<Border>(editor, "ShortcutCapture").Focus();
            editor.BeginRecording(); return editor;
        }
        void Record(ShortcutEditorWindow editor, HotkeySettings key)
        { Find<Border>(editor, "ShortcutCapture").Focus(); editor.BeginRecording(); editor.Record(key); }
        window.NavigateTo(SettingsCategory.General);
        check("general settings no longer contains shortcut controls", !Children<FrameworkElement>(window.GeneralPage)
            .Any(item => AutomationProperties.GetAutomationId(item).StartsWith("Shortcut")));
        window.NavigateTo(SettingsCategory.Shortcuts); window.UpdateLayout();
        check("shortcut page shows six independent rows without action pickers or expanders", Children<Grid>(page)
            .Count(row => AutomationProperties.GetAutomationId(row).StartsWith("ShortcutRow_")) == 6
            && !Children<ComboBox>(page).Any() && !Children<Expander>(page).Any());
        UiAppearanceTest.Snapshot(window, "settings-shortcuts-list.png");
        var editor = await Open();
        try
        {
            keybd_event(0x11, 0, 0, 0); keybd_event(0x12, 0, 0, 0); keybd_event(0x42, 0, 0, 0);
            await Task.Delay(50);
        }
        finally { keybd_event(0x42, 0, 2, 0); keybd_event(0x12, 0, 2, 0); keybd_event(0x11, 0, 2, 0); }
        await Task.Delay(50);
        check("native Ctrl Alt B records in the dedicated editor", Find<Button>(editor, "SaveShortcut").IsEnabled
            && AutomationProperties.GetName(Find<StackPanel>(editor, "ShortcutDraftKeys")) == "Ctrl + Alt + B");
        keybd_event(0x1B, 0, 0, 0); keybd_event(0x1B, 0, 2, 0); await Task.Delay(50);
        check("Escape closes the editor without saving", page.Editor == null && !File.Exists(path));
        editor = await Open(); Record(editor, new(3, 0x42));
        UiAppearanceTest.Snapshot(editor, "settings-shortcut-recording.png");
        Click(editor, "CancelShortcut");
        check("cancel preserves the saved key and list display", window.CurrentHotkey == HotkeySettings.Default
            && AutomationProperties.GetName(Find<StackPanel>(page, "ShortcutKeys_Toolbar")) == HotkeySettings.Default.Display && !File.Exists(path));
        editor = await Open(); Record(editor, new(2, 0x56));
        check("reserved combination disables editor Save", !Find<Button>(editor, "SaveShortcut").IsEnabled);
        Record(editor, new(3, 0x42)); Click(editor, "SaveShortcut");
        check("successful editor Save closes and refreshes the list", page.Editor == null && window.CurrentHotkey == new HotkeySettings(3, 0x42)
            && new SettingsStore(path).Load().Settings.Hotkey == new HotkeySettings(3, 0x42)
            && AutomationProperties.GetName(Find<StackPanel>(page, "ShortcutKeys_Toolbar")) == "Ctrl + Alt + B");
        editor = await Open(); Record(editor, new(3, 0x4A));
        var before = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) Click(editor, "SaveShortcut");
        check("failed key save retains draft old registration and disk bytes", page.Editor == editor && window.CurrentHotkey == new HotkeySettings(3, 0x42)
            && before.SequenceEqual(File.ReadAllBytes(path)));
        check("disk errors keep focus on Save rather than starting key capture", !editor.IsRecording
            && ReferenceEquals(Keyboard.FocusedElement, Find<Button>(editor, "SaveShortcut")));
        editor.Close();
        using (var blocker = new HwndSource(new HwndSourceParameters("SnapBar UI hotkey conflict") { Width = 1, Height = 1, WindowStyle = 0 }))
        {
            if (!Native.RegisterHotKey(blocker.Handle, 91, 0x4003, 0x4A)) throw new InvalidOperationException("Fixture key occupied");
            try
            {
                editor = await Open(); Record(editor, new(3, 0x4A)); Click(editor, "SaveShortcut");
                check("conflict retains the editor and explains recovery", page.Editor == editor && window.CurrentHotkey == new HotkeySettings(3, 0x42)
                    && Find<TextBlock>(editor, "ShortcutStatus").Text == UiText.Get("MainWindow_General_006"));
                check("external shortcut conflict automatically focuses and arms the recorder", editor.IsRecording
                    && ReferenceEquals(Keyboard.FocusedElement, Find<Border>(editor, "ShortcutCapture")));
                UiAppearanceTest.Snapshot(editor, "settings-shortcut-conflict.png");
                editor.Record(new(3, 0x4B));
                check("external conflict accepts a replacement without another click", AutomationProperties.GetName(Find<StackPanel>(editor, "ShortcutDraftKeys")) == "Ctrl + Alt + K"
                    && Find<TextBlock>(editor, "ShortcutStatus").Text == UiText.Get("Shortcuts_Ready"));
            }
            finally { Native.UnregisterHotKey(blocker.Handle, 91); }
        }
        window.NavigateTo(SettingsCategory.About);
        check("leaving the shortcut page discards its editor", page.Editor == null && window.CurrentHotkey == new HotkeySettings(3, 0x42));
        editor = await Open(); Record(editor, new(3, 0x4A));
        var other = new Window { Width = 180, Height = 100, ShowInTaskbar = false, Title = "Shortcut deactivation fixture" };
        other.Show(); other.Activate(); await Task.Delay(80);
        check("deactivation stops native recording without saving", !editor.IsRecording && window.CurrentHotkey == new HotkeySettings(3, 0x42));
        other.Close(); editor.Close();
        editor = await Open(); Click(editor, "ResetShortcut");
        check("restore default commits and refreshes the toolbar binding", window.CurrentHotkey == HotkeySettings.Default
            && new SettingsStore(path).Load().Settings.Hotkey == HotkeySettings.Default);
        editor = await Open(TextAction.Search); Record(editor, HotkeySettings.Default); Click(editor, "SaveShortcut");
        check("Search rejects the toolbar combination", page.Editor == editor && new SettingsStore(path).Load().Settings.SearchHotkey == null);
        var actionKey = new HotkeySettings(3, 0x4A);
        Record(editor, actionKey); Click(editor, "SaveShortcut");
        check("Search saves independently", new SettingsStore(path).Load().Settings.SearchHotkey == actionKey);
        editor = await Open(TextAction.Search); Click(editor, "ResetShortcut");
        editor = await Open(TextAction.Lookup); Record(editor, actionKey); Click(editor, "SaveShortcut");
        check("dictionary shortcut saves independently", new SettingsStore(path).Load().Settings.LookupHotkey == actionKey);
        editor = await Open(TextAction.Lookup); Click(editor, "ResetShortcut");
        editor = await Open(TextAction.Ai);
        var target = Find<ComboBox>(editor, "ShortcutItem");
        check("AI requires an explicitly chosen target", !Find<Button>(editor, "SaveShortcut").IsEnabled);
        before = File.ReadAllBytes(path);
        check("AI recorder is available before choosing a command", Find<Border>(editor, "ShortcutCapture").IsEnabled
            && Find<TextBlock>(editor, "ShortcutStatus").Text == UiText.Get("Shortcuts_NeedBoth"));
        Record(editor, actionKey);
        check("AI records the shortcut first and asks only for the missing command", !Find<Button>(editor, "SaveShortcut").IsEnabled
            && AutomationProperties.GetName(Find<StackPanel>(editor, "ShortcutDraftKeys")) == actionKey.Display
            && Find<TextBlock>(editor, "ShortcutStatus").Text == UiText.Get("Shortcuts_NeedTarget"));
        UiAppearanceTest.Snapshot(editor, "settings-shortcut-ai-key-first.png");
        target.SelectedIndex = 1;
        check("choosing or changing a command retains the recorded shortcut", Find<Button>(editor, "SaveShortcut").IsEnabled
            && AutomationProperties.GetName(Find<StackPanel>(editor, "ShortcutDraftKeys")) == actionKey.Display);
        target.SelectedItem = target.Items.Cast<ShortcutsSettingsPage.ShortcutChoice>().Single(item => item.Id == "summary");
        check("target and key remain drafts until Save", before.SequenceEqual(File.ReadAllBytes(path)));
        UiAppearanceTest.Snapshot(editor, "settings-shortcut-ai-editor.png");
        Click(editor, "CancelShortcut");
        check("cancel discards both target and key", new SettingsStore(path).Load().Settings is { AiHotkey: null, AiHotkeyPromptId: null });
        editor = await Open(TextAction.Ai); target = Find<ComboBox>(editor, "ShortcutItem");
        target.SelectedItem = target.Items.Cast<ShortcutsSettingsPage.ShortcutChoice>().Single(item => item.Id == "summary");
        check("command-first setup asks only for the missing shortcut", !Find<Button>(editor, "SaveShortcut").IsEnabled
            && Find<TextBlock>(editor, "ShortcutStatus").Text == UiText.Get("Shortcuts_NeedKey"));
        Record(editor, actionKey); Click(editor, "SaveShortcut");
        check("AI target and key save together", new SettingsStore(path).Load().Settings is { AiHotkeyPromptId: "summary" } ai
            && ai.AiHotkey == actionKey);
        editor = await Open(TextAction.Ai); Click(editor, "ResetShortcut");
        check("removing AI binding clears key and target while preserving the command", page.Editor == null
            && new SettingsStore(path).Load().Settings is { AiHotkey: null, AiHotkeyPromptId: null }
            && window.AiPreferences.Settings.Prompts.Any(prompt => prompt.Id == "summary"));
        editor = await Open(TextAction.Ai); target = Find<ComboBox>(editor, "ShortcutItem");
        check("removed AI binding reopens with no selected command", ((ShortcutsSettingsPage.ShortcutChoice)target.SelectedItem).Id == "");
        target.SelectedItem = target.Items.Cast<ShortcutsSettingsPage.ShortcutChoice>().Single(item => item.Id == "summary");
        Record(editor, actionKey); Click(editor, "SaveShortcut");
        editor = await Open(TextAction.Custom); target = Find<ComboBox>(editor, "ShortcutItem");
        Record(editor, new(3, 0x4B));
        check("custom action records the shortcut before a target without enabling Save", Find<Border>(editor, "ShortcutCapture").IsEnabled
            && !Find<Button>(editor, "SaveShortcut").IsEnabled
            && AutomationProperties.GetName(Find<StackPanel>(editor, "ShortcutDraftKeys")) == "Ctrl + Alt + K"
            && Find<TextBlock>(editor, "ShortcutStatus").Text == UiText.Get("Shortcuts_NeedTarget"));
        UiAppearanceTest.Snapshot(editor, "settings-shortcut-custom-key-first.png");
        target.SelectedIndex = 1;
        var customId = ((ShortcutsSettingsPage.ShortcutChoice)target.SelectedItem).Id;
        Record(editor, actionKey); Click(editor, "SaveShortcut");
        check("custom conflict saves neither target nor key", page.Editor == editor && new SettingsStore(path).Load().Settings is
            { CustomHotkey: null, CustomHotkeyActionId: null });
        check("internal conflict automatically arms capture and preserves the selected action", editor.IsRecording
            && ReferenceEquals(Keyboard.FocusedElement, Find<Border>(editor, "ShortcutCapture"))
            && ((ShortcutsSettingsPage.ShortcutChoice)target.SelectedItem).Id == customId);
        editor.Record(new(3, 0x4B)); Click(editor, "SaveShortcut");
        check("custom shortcut saves a stable target ID", new SettingsStore(path).Load().Settings.CustomHotkeyActionId == customId);
        UiAppearanceTest.Snapshot(window, "settings-shortcuts-configured.png");
        editor = await Open(TextAction.Ai); target = Find<ComboBox>(editor, "ShortcutItem"); target.SelectedIndex = 1;
        before = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) Click(editor, "SaveShortcut");
        check("failed target save preserves the saved target and key", page.Editor == editor && before.SequenceEqual(File.ReadAllBytes(path))
            && new SettingsStore(path).Load().Settings.AiHotkeyPromptId == "summary");
        editor.Close(); editor = await Open(TextAction.Ai); target = Find<ComboBox>(editor, "ShortcutItem");
        check("reopening resumes the saved target rather than the discarded draft", ((ShortcutsSettingsPage.ShortcutChoice)target.SelectedItem).Id == "summary");
        window.AiPreferences.DeletePrompt("summary", out _); editor.RefreshLanguage();
        check("deleted target never silently selects another command", !Find<Button>(editor, "SaveShortcut").IsEnabled
            && ((ShortcutsSettingsPage.ShortcutChoice)target.SelectedItem).Id == "");
        Click(editor, "ResetShortcut");
        editor = await Open(TextAction.Custom);
        var customDefinition = new SettingsStore(path).Load().Settings.Actions.CustomActions.Single(action => action.Id == customId);
        check("removal action explicitly names the binding", Equals(Find<Button>(editor, "ResetShortcut").Content, UiText.Get("Shortcuts_RemoveBinding")));
        before = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) Click(editor, "ResetShortcut");
        check("failed removal keeps both binding fields and the editor", page.Editor == editor && before.SequenceEqual(File.ReadAllBytes(path))
            && new SettingsStore(path).Load().Settings.CustomHotkeyActionId == customId);
        Click(editor, "ResetShortcut");
        var removed = new SettingsStore(path).Load().Settings;
        check("custom removal clears the complete binding but retains its action definition", page.Editor == null
            && removed is { CustomHotkey: null, CustomHotkeyActionId: null } && removed.Actions.CustomActions.Contains(customDefinition)
            && AutomationProperties.GetName(Find<StackPanel>(page, "ShortcutKeys_Custom")) == UiText.Get("Shortcuts_NotSet"));
        UiAppearanceTest.Snapshot(window, "settings-shortcuts-after-removal.png");
        editor = await Open(TextAction.Custom); target = Find<ComboBox>(editor, "ShortcutItem");
        check("removed custom binding reopens without a selected action", ((ShortcutsSettingsPage.ShortcutChoice)target.SelectedItem).Id == "");
        editor.Close();
        var managed = false;
        var empty = new ShortcutEditorWindow(TextAction.Custom, null, _ => null, _ => [], (_, _, _) => null, _ => managed = true, false) { Owner = window };
        empty.Show(); await Task.Delay(30);
        check("empty target list offers an enabled creation path", Find<Button>(empty, "ShortcutManageTarget").IsVisible
            && !Find<Button>(empty, "SaveShortcut").IsEnabled);
        Click(empty, "ShortcutManageTarget"); check("creation link closes the editor before opening management", managed && !empty.IsVisible);
        window.NavigateTo(SettingsCategory.General);
    }
}
