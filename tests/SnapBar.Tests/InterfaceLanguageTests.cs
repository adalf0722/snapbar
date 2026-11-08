using System.Text;
using SnapBar.Core;

internal static class InterfaceLanguageTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        check("system resolves traditional Chinese UI variants", () => new[] { "zh-TW", "zh-HK", "zh-MO", "zh-Hant" }
            .All(culture => InterfaceLanguage.Resolve("system", culture) == "zh-Hant"));
        check("unsupported system UI languages fall back to English", () => InterfaceLanguage.Resolve("system", "fr-FR") == "en"
            && InterfaceLanguage.Resolve("system", "zh-CN") == "en");
        check("explicit language wins over system", () => InterfaceLanguage.Resolve("en", "zh-TW") == "en"
            && InterfaceLanguage.Resolve("zh-Hant", "en-US") == "zh-Hant");
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Language.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            check("new installations follow system", () => new SettingsStore(path).Load().Settings.InterfaceLanguage == "system");
            check("legacy JSON without explicit schema preserves Traditional Chinese", () =>
            {
                File.WriteAllText(path, "{\"automaticDetectionEnabled\":true}", new UTF8Encoding(false));
                var loaded = new SettingsStore(path).Load();
                return loaded.Warning == null && loaded.Settings.InterfaceLanguage == "zh-Hant" && loaded.Settings.AutomaticDetectionEnabled;
            });
            foreach (var version in Enumerable.Range(1, 7))
                check($"schema {version} language migration retains preferences", () =>
                {
                    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = version,
                        automaticDetectionEnabled = true, clipboardCompatibilityEnabled = false,
                        hotkey = new { modifiers = 3, key = 74 }, actions = new { showLabels = false } }), new UTF8Encoding(false));
                    var settings = new SettingsStore(path).Load().Settings;
                    return settings.SchemaVersion == 8 && settings.InterfaceLanguage == "zh-Hant"
                        && settings.AutomaticDetectionEnabled && !settings.ClipboardCompatibilityEnabled
                        && settings.Hotkey == new HotkeySettings(3, 74) && !settings.Actions.ShowLabels;
                });
            check("schema 8 language roundtrip preserves unrelated settings", () =>
            {
                var store = new SettingsStore(path); var settings = store.Load().Settings;
                store.Save(settings with { InterfaceLanguage = "en" });
                var loaded = new SettingsStore(path).Load().Settings;
                return loaded.InterfaceLanguage == "en" && loaded.Hotkey == settings.Hotkey
                    && loaded.Actions.ShowLabels == settings.Actions.ShowLabels && loaded.Actions.CustomActions.SequenceEqual(settings.Actions.CustomActions);
            });
            check("unknown language falls back independently", () =>
            {
                File.WriteAllText(path, "{\"schemaVersion\":8,\"interfaceLanguage\":\"xx\",\"automaticDetectionEnabled\":true}", new UTF8Encoding(false));
                var loaded = new SettingsStore(path).Load();
                return loaded.Warning == null && loaded.Settings.InterfaceLanguage == "system" && loaded.Settings.AutomaticDetectionEnabled;
            });
            check("schema 8 without a language uses system rather than a legacy default", () =>
            {
                File.WriteAllText(path, "{\"schemaVersion\":8,\"automaticDetectionEnabled\":true}", new UTF8Encoding(false));
                var loaded = new SettingsStore(path).Load();
                return loaded.Warning == null && loaded.Settings.InterfaceLanguage == "system" && loaded.Settings.AutomaticDetectionEnabled;
            });
            check("failed language replacement preserves the saved language and original bytes", () =>
            {
                var store = new SettingsStore(path); var settings = store.Load().Settings;
                store.Save(settings with { InterfaceLanguage = "zh-Hant" });
                var before = File.ReadAllBytes(path);
                using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try { store.Save(settings with { InterfaceLanguage = "en" }); return false; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { return before.SequenceEqual(File.ReadAllBytes(path)) && new SettingsStore(path).Load().Settings.InterfaceLanguage == "zh-Hant"; }
            });
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }
}
