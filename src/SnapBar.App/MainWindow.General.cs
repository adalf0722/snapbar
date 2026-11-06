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
        TextAction.Translate => preferences.TranslationHotkey,
        TextAction.Ocr => preferences.OcrHotkey,
        _ => preferences.Hotkey
    };

    private string? SaveShortcut(TextAction? action, HotkeySettings? value)
    {
        if (controller == null || exitRequested) return "輸入監聽尚未就緒，請重新啟動 SnapBar。";
        if (value != null && ((action != null && value == preferences.Hotkey)
            || (action != TextAction.Search && value == preferences.SearchHotkey)
            || (action != TextAction.Copy && value == preferences.CopyHotkey)
            || (action != TextAction.Translate && value == preferences.TranslationHotkey)
            || (action != TextAction.Ocr && value == preferences.OcrHotkey)))
            return "SnapBar 已使用這組快捷鍵，請換一組。";
        if (action == null) return SaveHotkey(value!);
        var updated = action switch
        {
            TextAction.Search => preferences with { SearchHotkey = value },
            TextAction.Copy => preferences with { CopyHotkey = value },
            TextAction.Translate => preferences with { TranslationHotkey = value },
            TextAction.Ocr => preferences with { OcrHotkey = value },
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        try
        {
            if (!controller.TryChangeActionHotkey(action.Value, value, () => settingsStore.Save(updated)))
                return "這組快捷鍵無法使用，請換一組。原設定已保留。";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return "無法儲存快捷鍵，請檢查設定檔權限。原快捷鍵設定已保留。"; }
        preferences = updated;
        return null;
    }

    private string? SaveHotkey(HotkeySettings value)
    {
        if (controller == null || exitRequested) return "輸入監聽尚未就緒，請重新啟動 SnapBar。";
        if (!value.IsValid) return "這組快捷鍵不支援，請換一組。";
        var updated = preferences with { Hotkey = value };
        try
        {
            if (!controller.TryChangeHotkey(value, () => settingsStore.Save(updated)))
                return controller.HotkeyRegistered ? "這組快捷鍵無法使用，請換一組。原快捷鍵仍有效。"
                    : "這組快捷鍵無法使用，請換一組。目前快捷鍵仍不可用。";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return "無法儲存快捷鍵，請檢查設定檔權限。原快捷鍵設定已保留。"; }
        preferences = updated; UpdateAutomaticControls();
        if (hotkeyNotice) { notice.Visibility = System.Windows.Visibility.Collapsed; hotkeyNotice = false; }
        AddStatus("快捷鍵已設定 · " + value.Display);
        return null;
    }

    internal void InitializeBackground()
    {
        new WindowInteropHelper(this).EnsureHandle();
        if (tray == null) ShowSettings();
    }
}
