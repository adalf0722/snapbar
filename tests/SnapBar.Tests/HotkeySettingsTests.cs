using System.Text;
using SnapBar.Core;

internal static class HotkeySettingsTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        check("default hotkey is valid and renders its full combination", () => HotkeySettings.Default.IsValid && HotkeySettings.Default.Display == "Ctrl + Alt + V");
        foreach (var invalid in new[] { new HotkeySettings(0, 0x56), new(4, 0x56), new(10, 0x56), new(2, 0x56), new(1, 0x73), new(3, 0x7B), new(3, 0x20) })
            check("unsafe hotkey rejected: " + invalid, () => !invalid.IsValid);
        check("editing restrictions match complete combinations", () => new HotkeySettings(3, 0x43).IsValid && new HotkeySettings(6, 0x56).IsValid && new HotkeySettings(1, 0x7A).IsValid);
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Hotkey." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "settings.json");
        void Seed(string text) => File.WriteAllText(path, text, new UTF8Encoding(false));
        try
        {
            for (var version = 1; version <= 6; version++)
            {
                var schema = version;
                check("legacy schema supplies hotkey without rewriting: " + schema, () =>
                {
                    Seed("{\"schemaVersion\":" + schema + ",\"automaticDetectionEnabled\":true,\"clipboardCompatibilityEnabled\":false,\"sublimeCompatibilityEnabled\":false}");
                    var before = File.ReadAllBytes(path); var loaded = new SettingsStore(path).Load();
                    return loaded.Warning == null && loaded.Settings.SchemaVersion == 7 && loaded.Settings.Hotkey == HotkeySettings.Default
                        && loaded.Settings.AutomaticDetectionEnabled && !loaded.Settings.ClipboardCompatibilityEnabled
                        && loaded.Settings.SublimeCompatibilityEnabled == false && before.SequenceEqual(File.ReadAllBytes(path));
                });
            }
            foreach (var malformed in new[] { "null", "[]", "\"x\"", "{}", "{\"modifiers\":\"yes\",\"key\":86}", "{\"modifiers\":2,\"key\":86}", "{\"modifiers\":3,\"key\":999999999999}" })
                check("damaged shortcut preserves other preferences and original bytes: " + malformed, () =>
                {
                    Seed("{\"schemaVersion\":7,\"automaticDetectionEnabled\":true,\"clipboardCompatibilityEnabled\":false,\"hotkey\":" + malformed + "}");
                    var before = File.ReadAllBytes(path); var store = new SettingsStore(path); var loaded = store.Load();
                    if (loaded.Warning != "hotkey-invalid" || !loaded.Settings.AutomaticDetectionEnabled || loaded.Settings.ClipboardCompatibilityEnabled
                        || loaded.Settings.Hotkey != HotkeySettings.Default || !before.SequenceEqual(File.ReadAllBytes(path))) return false;
                    var backups = Directory.GetFiles(directory, "*.bak").Length;
                    store.Save(loaded.Settings with { Hotkey = new(3, 0x42) });
                    return Directory.GetFiles(directory, "*.bak").Length == backups + 1
                        && Directory.GetFiles(directory, "*.bak").Any(file => before.SequenceEqual(File.ReadAllBytes(file)))
                        && new SettingsStore(path).Load().Settings.Hotkey == new HotkeySettings(3, 0x42);
                });
            check("unwritable settings preserve damaged original when backup cannot be created", () =>
            {
                Seed("{\"schemaVersion\":7,\"hotkey\":null}"); var store = new SettingsStore(path); var loaded = store.Load();
                var before = File.ReadAllBytes(path);
                using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try { store.Save(loaded.Settings); return false; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return before.SequenceEqual(File.ReadAllBytes(path)); }
            });
        }
        finally { foreach (var file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }
}
