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
                foreach (var binding in new[] { (TextAction.Lookup, 11, 0x4B), (TextAction.Ai, 13, 0x4D), (TextAction.Custom, 15, 0x4E) })
                {
                    var key = new HotkeySettings(3, binding.Item3);
                    check(binding.Item1 + " shortcut registers independently", monitor.TryChangeActionHotkey(binding.Item1, key, () => { }));
                    var count = actions;
                    monitor.HotkeySuppressed = true;
                    SendMessage(source.Handle, Native.WM_HOTKEY, binding.Item2 + 1, Combination(key));
                    check(binding.Item1 + " shortcut records while suppressed", recorded == key && actions == count);
                    monitor.HotkeySuppressed = false;
                    SendMessage(source.Handle, Native.WM_HOTKEY, binding.Item2 + 1, Combination(key));
                    check(binding.Item1 + " shortcut dispatches the correct action", actions == count + 1 && action == binding.Item1);
                    monitor.TryChangeActionHotkey(binding.Item1, null, () => { });
                    SendMessage(source.Handle, Native.WM_HOTKEY, binding.Item2 + 1, Combination(key));
                    check(binding.Item1 + " cleared shortcut ignores queued messages", actions == count + 1);
                }
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

            await ShortcutSettingsTest.Run(window, path, check);
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
