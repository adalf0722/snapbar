using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class GeneralSettingsTest
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
            foreach (var matchChild in Children<T>(child)) yield return matchChild;
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Children<T>(root).Single(item => AutomationProperties.GetAutomationId(item) == id);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static nint Combination(HotkeySettings value) => (nint)((value.Key << 16) | value.Modifiers);

    internal static async Task Run(Action<string, bool> check, string directory)
    {
        var root = @"Software\SnapBar.Tests\Startup-" + Guid.NewGuid().ToString("N");
        var startup = new StartupRegistration(Environment.ProcessPath!, root, _ => true);
        var path = Path.Combine(directory, "general-settings.json");
        MainWindow? window = null;
        try
        {
            using (var run = Registry.CurrentUser.CreateSubKey(root + @"\Run")) run.SetValue("OtherApp", "preserve");
            check("startup defaults off without creating its Run value", startup.Read().State == StartupState.Off);
            var unlaunchable = new StartupRegistration(Environment.ProcessPath!, root, _ => false);
            try { unlaunchable.SetEnabled(true); check("unlaunchable startup is rejected", false); }
            catch (StartupLaunchException) { check("unlaunchable startup never writes a Run value", startup.Read().State == StartupState.Off); }
            check("startup enables quoted executable and dedicated background argument", startup.SetEnabled(true).State == StartupState.On
                && startup.Command == "\"" + Environment.ProcessPath + "\" --background");
            using (var approval = Registry.CurrentUser.CreateSubKey(root + @"\Explorer\StartupApproved\Run"))
                approval.SetValue("SnapBar", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            check("startup reports Windows disabling and never overwrites approval", startup.SetEnabled(true).State == StartupState.DisabledByWindows);
            using (var approval = Registry.CurrentUser.OpenSubKey(root + @"\Explorer\StartupApproved\Run", true))
            {
                check("system approval bytes remain untouched", ((byte[])approval!.GetValue("SnapBar")!)[0] == 3);
                approval.SetValue("SnapBar", new byte[] { 99 });
                check("unknown startup approval format fails closed", startup.Read().State == StartupState.Unavailable);
                approval.DeleteValue("SnapBar");
            }
            startup.SetEnabled(false);
            using (var run = Registry.CurrentUser.OpenSubKey(root + @"\Run"))
            {
                check("startup disabling removes only owned Run entry", startup.Read().State == StartupState.Off && (string?)run!.GetValue("OtherApp") == "preserve");
            }
            using (var run = Registry.CurrentUser.OpenSubKey(root + @"\Run", true)) run!.SetValue("SnapBar", "\"C:\\old\\SnapBar.App.exe\" --background");
            check("stale startup path is off with a repair hint", startup.Read() == new StartupStatus(StartupState.Off, true));
            startup.SetEnabled(false);

            using (var owner = new HwndSource(new HwndSourceParameters("SnapBar hotkey transaction tests") { WindowStyle = 0, Width = 1, Height = 1 }))
            using (var blocker = new HwndSource(new HwndSourceParameters("SnapBar occupied hotkey") { WindowStyle = 0, Width = 1, Height = 1 }))
            {
                var initial = new HotkeySettings(3, 0x42); var candidate = new HotkeySettings(3, 0x4A);
                using var hotkey = new GlobalHotkey(owner.Handle, initial);
                check("native initial hotkey registers", hotkey.Registered);
                if (!Native.RegisterHotKey(blocker.Handle, 91, 0x4003, (uint)candidate.Key)) throw new InvalidOperationException("Hotkey fixture candidate is occupied");
                try { check("conflict skips persistence and keeps old registration", !hotkey.TryChange(candidate, () => throw new Exception("Must not persist")) && hotkey.Current == initial && hotkey.Registered); }
                finally { Native.UnregisterHotKey(blocker.Handle, 91); }
                try { hotkey.TryChange(candidate, () => throw new IOException("fixture save failure")); check("save failure throws", false); }
                catch (IOException) { check("save failure retains active old hotkey", hotkey.Current == initial && hotkey.Registered); }
                var released = Native.RegisterHotKey(blocker.Handle, 91, 0x4003, (uint)candidate.Key);
                check("save failure releases proposed replacement", released); if (released) Native.UnregisterHotKey(blocker.Handle, 91);
                check("successful save commits new hotkey", hotkey.TryChange(candidate, () => { }) && hotkey.Current == candidate);
                released = Native.RegisterHotKey(blocker.Handle, 91, 0x4003, (uint)initial.Key);
                check("commit releases old registration", released); if (released) Native.UnregisterHotKey(blocker.Handle, 91);
                hotkey.Suppressed = true;
                check("recording suppresses action without releasing ownership", !hotkey.ShouldTrigger(2, Combination(candidate)) && hotkey.Matches(2, Combination(candidate)));
            }
            using (var source = new HwndSource(new HwndSourceParameters("SnapBar recording messages") { WindowStyle = 0, Width = 1, Height = 1 }))
            using (var monitor = new InputMonitor(source, new(3, 0x42), new(3, 0x4A)))
            {
                var actions = 0; HotkeySettings? recorded = null;
                TextAction? action = null;
                monitor.Input += input => { if (input.Kind == "hotkey") { actions++; action = input.Action; } };
                monitor.HotkeyRecorded += value => recorded = value;
                monitor.HotkeySuppressed = true; SendMessage(source.Handle, Native.WM_HOTKEY, 1, Combination(new(3, 0x42)));
                check("old registered combination records without triggering toolbar", recorded == new HotkeySettings(3, 0x42) && actions == 0);
                monitor.HotkeySuppressed = false; SendMessage(source.Handle, Native.WM_HOTKEY, 1, Combination(new(3, 0x42)));
                check("ending recording restores hotkey action", actions == 1);
                SendMessage(source.Handle, Native.WM_HOTKEY, 1, Combination(new(3, 0x4A)));
                check("stale combination messages never trigger a new action", actions == 1);
                monitor.HotkeySuppressed = true;
                SendMessage(source.Handle, Native.WM_HOTKEY, 3, Combination(new(3, 0x4A)));
                check("registered Search shortcut records without dispatching", recorded == new HotkeySettings(3, 0x4A) && actions == 1);
                monitor.HotkeySuppressed = false;
                SendMessage(source.Handle, Native.WM_HOTKEY, 3, Combination(new(3, 0x4A)));
                check("Search shortcut dispatches Search independently of toolbar", actions == 2 && action == TextAction.Search);
                check("Search shortcut can be cleared independently", monitor.TryChangeActionHotkey(TextAction.Search, null, () => { })
                    && !monitor.IsActionHotkeyRegistered(TextAction.Search) && monitor.HotkeyRegistered);
                SendMessage(source.Handle, Native.WM_HOTKEY, 3, Combination(new(3, 0x4A)));
                check("cleared Search registration ignores queued messages", actions == 2);
            }

            window = new MainWindow(new SettingsStore(path), () => { }, () => false, startup);
            window.InitializeBackground(); await Task.Delay(50);
            check("background initializes without showing settings", !window.IsVisible && new WindowInteropHelper(window).Handle != 0);
            window.ShowSettings(); await Task.Delay(80);
            check("background instance can reveal initialized settings", window.IsVisible);
            var page = window.GeneralPage;
            var startupToggle = Find<CheckBox>(page, "StartupToggle");
            startupToggle.IsChecked = true; check("startup UI toggles actual registration on", startup.Read().State == StartupState.On && startupToggle.IsChecked == true);
            startupToggle.IsChecked = false; check("startup UI toggles actual registration off", startup.Read().State == StartupState.Off);
            using (var approval = Registry.CurrentUser.CreateSubKey(root + @"\Explorer\StartupApproved\Run"))
                approval.SetValue("SnapBar", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            startupToggle.IsChecked = true;
            check("disabled startup UI stays off and offers Windows settings", startupToggle.IsChecked == false && Find<Button>(page, "StartupWindowsSettings").Visibility == Visibility.Visible);
            UiAppearanceTest.Snapshot(window, "settings-startup-disabled.png");
            using (var approval = Registry.CurrentUser.OpenSubKey(root + @"\Explorer\StartupApproved\Run", true))
            {
                approval!.SetValue("SnapBar", new byte[] { 99 }); page.RefreshStartup();
                check("unknown startup state disables toggle and exposes retry", !startupToggle.IsEnabled && Find<Button>(page, "StartupRetry").Visibility == Visibility.Visible);
                approval.DeleteValue("SnapBar");
            }
            startup.SetEnabled(false); page.RefreshStartup();

            window.Title = "SnapBar general keyboard fixture"; window.Activate();
            var handle = new WindowInteropHelper(window).Handle;
            Console.WriteLine("WAITING: activate the dedicated SnapBar general keyboard fixture (up to 60 seconds)");
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (Native.GetForegroundWindow() != handle && DateTime.UtcNow < deadline) await Task.Delay(50);
            if (Native.GetForegroundWindow() != handle) throw new InvalidOperationException("Activate the General settings fixture before native recording tests");
            Click(page, "ChangeShortcut");
            try
            {
                keybd_event(0x11, 0, 0, 0); keybd_event(0x12, 0, 0, 0); keybd_event(0x42, 0, 0, 0);
                await Task.Delay(50);
            }
            finally { keybd_event(0x42, 0, 2, 0); keybd_event(0x12, 0, 2, 0); keybd_event(0x11, 0, 2, 0); }
            await Task.Delay(50);
            check("native Ctrl Alt B is recorded through WPF keyboard input", page.IsRecording && Find<Button>(page, "SaveShortcut").IsEnabled
                && AutomationProperties.GetName(Find<StackPanel>(page, "GeneralShortcutKeys")) == "Ctrl + Alt + B");
            keybd_event(0x1B, 0, 0, 0); keybd_event(0x1B, 0, 2, 0); await Task.Delay(50);
            check("native Escape cancels recording without saving", !page.IsRecording && !File.Exists(path));

            Click(page, "ChangeShortcut"); page.Record(new(3, 0x42));
            check("recorded candidate stays a draft before saving", page.IsRecording && window.CurrentHotkey == HotkeySettings.Default && !File.Exists(path));
            UiAppearanceTest.Snapshot(window, "settings-shortcut-recording.png");
            Click(page, "CancelShortcut");
            check("cancel restores previous display without saving", !page.IsRecording && AutomationProperties.GetName(Find<StackPanel>(page, "GeneralShortcutKeys")) == HotkeySettings.Default.Display && !File.Exists(path));
            Click(page, "ChangeShortcut"); page.Record(new(2, 0x56));
            check("reserved combination disables save", !Find<Button>(page, "SaveShortcut").IsEnabled);
            page.Record(new(3, 0x42)); Click(page, "SaveShortcut");
            check("shortcut UI saves and commits immediately", !page.IsRecording && window.CurrentHotkey == new HotkeySettings(3, 0x42)
                && new SettingsStore(path).Load().Settings.Hotkey == new HotkeySettings(3, 0x42));
            Click(page, "ChangeShortcut"); page.Record(new(3, 0x4A));
            var before = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) Click(page, "SaveShortcut");
            check("failed UI save preserves draft old key and disk bytes", page.IsRecording && window.CurrentHotkey == new HotkeySettings(3, 0x42) && before.SequenceEqual(File.ReadAllBytes(path)));
            Click(page, "CancelShortcut");
            using (var blocker = new HwndSource(new HwndSourceParameters("SnapBar UI hotkey conflict") { Width = 1, Height = 1, WindowStyle = 0 }))
            {
                if (!Native.RegisterHotKey(blocker.Handle, 91, 0x4003, 0x4A)) throw new InvalidOperationException("UI conflict fixture key occupied");
                try
                {
                    Click(page, "ChangeShortcut"); page.Record(new(3, 0x4A)); Click(page, "SaveShortcut");
                    check("conflict UI retains old key and explains recovery", page.IsRecording && window.CurrentHotkey == new HotkeySettings(3, 0x42)
                        && Find<TextBlock>(page, "ShortcutStatus").Text.Contains("原快捷鍵仍有效"));
                    UiAppearanceTest.Snapshot(window, "settings-shortcut-conflict.png");
                }
                finally { Native.UnregisterHotKey(blocker.Handle, 91); }
            }
            window.NavigateTo(SettingsCategory.About);
            check("leaving general settings cancels shortcut recording", !page.IsRecording);
            window.NavigateTo(SettingsCategory.General); Click(page, "ChangeShortcut"); page.Record(new(3, 0x4A));
            var other = new Window { Width = 180, Height = 100, ShowInTaskbar = false, Title = "Shortcut deactivation fixture" };
            other.Show(); other.Activate(); await Task.Delay(80); other.Close();
            check("deactivation cancels recording without saving", !page.IsRecording && window.CurrentHotkey == new HotkeySettings(3, 0x42));
            window.PrepareSessionEnd(); window.Close();
            window = new MainWindow(new SettingsStore(path), () => { }, () => false, startup); window.Show(); await Task.Delay(50);
            check("saved shortcut survives a new session and UI display", window.CurrentHotkey == new HotkeySettings(3, 0x42)
                && AutomationProperties.GetName(Find<StackPanel>(window.GeneralPage, "GeneralShortcutKeys")) == "Ctrl + Alt + B");
            Click(window.GeneralPage, "ResetShortcut");
            check("restore default commits and saves default shortcut", window.CurrentHotkey == HotkeySettings.Default
                && new SettingsStore(path).Load().Settings.Hotkey == HotkeySettings.Default);
            var shortcuts = Find<ComboBox>(window.GeneralPage, "ShortcutTarget");
            shortcuts.SelectedIndex = 1;
            Click(window.GeneralPage, "ChangeShortcut"); window.GeneralPage.Record(HotkeySettings.Default);
            Click(window.GeneralPage, "SaveShortcut");
            check("Search shortcut rejects the existing toolbar combination", window.GeneralPage.IsRecording
                && new SettingsStore(path).Load().Settings.SearchHotkey == null);
            var searchBinding = new HotkeySettings(3, 0x4A);
            window.GeneralPage.Record(searchBinding); Click(window.GeneralPage, "SaveShortcut");
            check("Search shortcut saves separately from toolbar shortcut", !window.GeneralPage.IsRecording
                && new SettingsStore(path).Load().Settings.SearchHotkey == searchBinding);
            Click(window.GeneralPage, "ResetShortcut");
            check("clear action shortcut preserves toolbar shortcut", new SettingsStore(path).Load().Settings is
                { SearchHotkey: null } cleared && cleared.Hotkey == HotkeySettings.Default);
            var ocrLanguage = Find<ComboBox>(window.GeneralPage, "OcrLanguagePriority");
            check("OCR defaults to Traditional Chinese priority", ocrLanguage.SelectedIndex == 0);
            ocrLanguage.SelectedIndex = 1;
            check("choosing English priority persists OCR preference", new SettingsStore(path).Load().Settings.OcrPreferEnglish);
            window.PrepareSessionEnd(); window.Close();
            window = new MainWindow(new SettingsStore(path), () => { }, () => false, startup);
            check("OCR priority is restored in a new settings window", Find<ComboBox>(window.GeneralPage, "OcrLanguagePriority").SelectedIndex == 1);
            window.CreateTrayIcon = (_, _, _) => throw new InvalidOperationException("fixture tray unavailable");
            window.InitializeBackground();
            check("background startup reveals settings when tray initialization fails", window.IsVisible
                && Children<TextBlock>(window).Any(text => text.Text.Contains("無法建立系統匣圖示")));
        }
        finally
        {
            if (window != null) { window.PrepareSessionEnd(); window.Close(); }
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }
}
