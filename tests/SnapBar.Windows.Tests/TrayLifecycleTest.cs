using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class TrayLifecycleTest
{
    private static IEnumerable<T> Children<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
            foreach (var descendant in Children<T>(child)) yield return descendant;
    }

    private static void ToggleViaAutomation(MainWindow window, string id)
    {
        var control = Children<CheckBox>(window).Single(c => AutomationProperties.GetAutomationId(c) == id);
        var peer = new CheckBoxAutomationPeer(control);
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
    }

    public static int Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Tray.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
        bool sublimeReady = false, setupFails = false; int setupCalls = 0;
        void SetupSublime()
        {
            setupCalls++;
            if (setupFails) throw new InvalidOperationException("fixture setup failure");
            sublimeReady = true;
        }
        int passed = 0, failed = 0;
        void Check(string name, bool condition)
        {
            Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
            if (condition) passed++; else failed++;
        }
        app.Startup += async (_, _) =>
        {
            try
            {
                await GeneralSettingsTest.Run(Check, directory);
                window = new MainWindow(new SettingsStore(path), SetupSublime, () => sublimeReady);
                window.Show();
                await Task.Delay(100);
                Check("first launch defaults to manual mode", !window.AutomaticDetectionEnabled);
                Check("first launch enables automatic compatibility", window.ClipboardCompatibilityEnabled);
                Check("loading settings never installs Sublime bindings", setupCalls == 0);
                ToggleViaAutomation(window, "ClipboardCompatibilityToggle");
                Check("compatibility can be disabled and saved", !window.ClipboardCompatibilityEnabled
                    && !new SettingsStore(path).Load().Settings.ClipboardCompatibilityEnabled);
                ToggleViaAutomation(window, "ClipboardCompatibilityToggle");
                Check("UIA toggle reenables controller and saves actual state", window.ClipboardCompatibilityEnabled
                    && new SettingsStore(path).Load().Settings.ClipboardCompatibilityEnabled);
                ToggleViaAutomation(window, "SublimeCompatibilityToggle");
                Check("UIA toggle explicitly configures and enables Sublime support", setupCalls == 1 && window.SublimeCompatibilityEnabled
                    && new SettingsStore(path).Load().Settings.SublimeCompatibilityEnabled == true);
                ToggleViaAutomation(window, "SublimeCompatibilityToggle");
                Check("UIA toggle disables Sublime in controller and on disk", !window.SublimeCompatibilityEnabled
                    && new SettingsStore(path).Load().Settings.SublimeCompatibilityEnabled == false);
                ToggleViaAutomation(window, "SublimeCompatibilityToggle");
                Check("reenabling valid Sublime support does not reinstall bindings", setupCalls == 1 && window.SublimeCompatibilityEnabled);
                ToggleViaAutomation(window, "SublimeCompatibilityToggle");
                sublimeReady = false; setupFails = true;
                ToggleViaAutomation(window, "SublimeCompatibilityToggle");
                Check("failed UIA setup rolls checkbox controller and saved preference back without recursive setup",
                    setupCalls == 2 && !window.SublimeCompatibilityEnabled
                    && Children<CheckBox>(window).Single(c => AutomationProperties.GetAutomationId(c) == "SublimeCompatibilityToggle").IsChecked == false
                    && new SettingsStore(path).Load().Settings.SublimeCompatibilityEnabled == false);
                setupFails = false; ToggleViaAutomation(window, "SublimeCompatibilityToggle");
                Check("Sublime can be enabled after failed setup rollback", setupCalls == 3 && window.SublimeCompatibilityEnabled);
                ToggleViaAutomation(window, "ClipboardCompatibilityToggle");
                Check("UIA global disable also disables active Sublime support", !window.ClipboardCompatibilityEnabled && !window.SublimeCompatibilityEnabled);
                window.ToggleAutomatic();
                Check("window toggle updates controller and saved preference", window.AutomaticDetectionEnabled
                    && new SettingsStore(path).Load().Settings.AutomaticDetectionEnabled);
                var excludedPath = Environment.ProcessPath!;
                Check("adding excluded app updates and saves the full executable path",
                    window.AddExcludedApplication(excludedPath, "Test runner") && window.ExcludedApplications.Count == 1
                    && new SettingsStore(path).Load().Settings.ExcludedApplications.Length == 1);
                var beforeDuplicate = File.ReadAllBytes(path);
                Check("duplicate excluded path changes neither list nor saved file",
                    !window.AddExcludedApplication(excludedPath.ToUpperInvariant(), "Duplicate")
                    && window.ExcludedApplications.Count == 1 && beforeDuplicate.SequenceEqual(File.ReadAllBytes(path)));
                const string searchTemplate = "https://example.com/search?q={query}";
                Check("valid search template is saved", window.SetSearchUrlTemplate(searchTemplate)
                    && new SettingsStore(path).Load().Settings.SearchUrlTemplate == searchTemplate);
                var beforeInvalid = File.ReadAllBytes(path);
                Check("invalid search template changes neither preference nor file", !window.SetSearchUrlTemplate("file:///test")
                    && beforeInvalid.SequenceEqual(File.ReadAllBytes(path)));
                window.Close();
                Check("normal close hides settings without ending application", !window.IsVisible && !app.Dispatcher.HasShutdownStarted);
                window.ShowSettings();
                Check("settings reopen using the same window and automatic state", window.IsVisible && window.AutomaticDetectionEnabled);
                window.ToggleAutomatic();
                Check("pause is saved and automatic detection stops", !window.AutomaticDetectionEnabled
                    && !new SettingsStore(path).Load().Settings.AutomaticDetectionEnabled);
                window.PrepareSessionEnd(); window.Close();
                var store = new SettingsStore(path);
                var restored = store.Load().Settings;
                store.Save(restored with { AutomaticDetectionEnabled = true, SublimeCompatibilityEnabled = false });
                window = new MainWindow(new SettingsStore(path), SetupSublime, () => sublimeReady); window.Show();
                Check("new window session loads the persisted automatic preference", window.AutomaticDetectionEnabled);
                Check("new window session remembers disabled compatibility", !window.ClipboardCompatibilityEnabled);
                Check("explicitly disabled Sublime support stays disabled even if legacy bindings exist",
                    new SettingsStore(path).Load().Settings.SublimeCompatibilityEnabled == false);
                window.SetClipboardCompatibility(true);
                Check("reenabling compatibility saves the new choice", window.ClipboardCompatibilityEnabled
                    && new SettingsStore(path).Load().Settings.ClipboardCompatibilityEnabled);
                Check("excluded applications survive a new window session", window.ExcludedApplications.Count == 1);
                Check("search template survives another settings change and new session",
                    new SettingsStore(path).Load().Settings.SearchUrlTemplate == searchTemplate);
                Check("default search engine can be restored", window.SetSearchUrlTemplate(WebActionPolicy.DefaultSearchTemplate)
                    && new SettingsStore(path).Load().Settings.SearchUrlTemplate == WebActionPolicy.DefaultSearchTemplate);
                Check("removing an excluded app is saved immediately", window.RemoveExcludedApplication(excludedPath)
                    && window.ExcludedApplications.Count == 0 && new SettingsStore(path).Load().Settings.ExcludedApplications.Length == 0);
                using var owner = new HwndSource(new HwndSourceParameters("SnapBar lifecycle cleanup")
                    { Width = 1, Height = 1, WindowStyle = 0 });
                bool released = false;
                window.Closed += (_, _) =>
                {
                    // The controller's Closed handler runs first and must release the actual hotkey.
                    released = Native.RegisterHotKey(owner.Handle, 77, 0x4000 | 1 | 2, 0x56);
                    if (released) Native.UnregisterHotKey(owner.Handle, 77);
                };
                await window.ExitAsync();
                Check("explicit exit closes settings and releases global hotkey", !window.IsVisible && released);
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e.Message); failed++; }
            finally
            {
                if (window?.IsVisible == true) { window.PrepareSessionEnd(); window.Close(); }
                foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
                Console.WriteLine($"{passed} passed, {failed} failed");
                app.Shutdown();
            }
        };
        app.Run();
        return failed == 0 ? 0 : 1;
    }
}
