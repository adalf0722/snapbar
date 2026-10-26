using System.IO;
using System.Windows;
using System.Windows.Interop;
using SnapBar.App;
using SnapBar.Core;
using SnapBar.Windows;

internal static class TrayLifecycleTest
{
    public static int Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Tray.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
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
                window = new MainWindow(new SettingsStore(path));
                window.Show();
                await Task.Delay(100);
                Check("first launch defaults to manual mode", !window.AutomaticDetectionEnabled);
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
                store.Save(restored with { AutomaticDetectionEnabled = true });
                window = new MainWindow(new SettingsStore(path)); window.Show();
                Check("new window session loads the persisted automatic preference", window.AutomaticDetectionEnabled);
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
