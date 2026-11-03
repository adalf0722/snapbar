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
