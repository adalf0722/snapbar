using System.IO;
using System.Windows.Interop;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class MainWindow
{
    internal Func<Action, Action, Action, TrayIcon> CreateTrayIcon { get; set; } = (show, toggle, exit) => new(show, toggle, exit);
    internal GeneralSettingsPage GeneralPage => generalPage;
    internal HotkeySettings CurrentHotkey => preferences.Hotkey;

    private HotkeySettings? ReadShortcut(TextAction? action) => action switch
    {
        TextAction.Search => preferences.SearchHotkey,
        TextAction.Copy => preferences.CopyHotkey,
        TextAction.Ocr => preferences.OcrHotkey,
        _ => preferences.Hotkey
    };

    private string? SaveShortcut(TextAction? action, HotkeySettings? value)
    {
        if (controller == null || exitRequested) return UiText.Get("MainWindow_General_000");
        if (value != null && ((action != null && value == preferences.Hotkey)
            || (action != TextAction.Search && value == preferences.SearchHotkey)
            || (action != TextAction.Copy && value == preferences.CopyHotkey)
            || (action != TextAction.Ocr && value == preferences.OcrHotkey)))
            return UiText.Get("MainWindow_General_001");
        if (action == null) return SaveHotkey(value!);
        var updated = action switch
        {
            TextAction.Search => preferences with { SearchHotkey = value },
            TextAction.Copy => preferences with { CopyHotkey = value },
            TextAction.Ocr => preferences with { OcrHotkey = value },
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        try
        {
            if (!controller.TryChangeActionHotkey(action.Value, value, () => settingsStore.Save(updated)))
                return UiText.Get("MainWindow_General_002");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UiText.Get("MainWindow_General_003"); }
        preferences = updated;
        return null;
    }

    private string? SaveHotkey(HotkeySettings value)
    {
        if (controller == null || exitRequested) return UiText.Get("MainWindow_General_004");
        if (!value.IsValid) return UiText.Get("MainWindow_General_005");
        var updated = preferences with { Hotkey = value };
        try
        {
            if (!controller.TryChangeHotkey(value, () => settingsStore.Save(updated)))
                return controller.HotkeyRegistered ? UiText.Get("MainWindow_General_006")
                    : UiText.Get("MainWindow_General_007");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UiText.Get("MainWindow_General_008"); }
        preferences = updated; UpdateAutomaticControls();
        if (hotkeyNotice) { notice.Visibility = System.Windows.Visibility.Collapsed; hotkeyNotice = false; }
        AddStatus(UiText.Join(UiText.Get("MainWindow_General_009"), value.Display));
        return null;
    }

    internal void InitializeBackground()
    {
        new WindowInteropHelper(this).EnsureHandle();
        if (tray == null) ShowSettings();
    }
}
