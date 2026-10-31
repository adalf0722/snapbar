using System.Text;
using SnapBar.Core;

internal static class SettingsStoreTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Settings.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        void Seed(string content) => File.WriteAllText(path, content, new UTF8Encoding(false));
        bool RejectsSave(SettingsStore store)
        {
            var before = File.ReadAllBytes(path);
            try { store.Save(new(AutomaticDetectionEnabled: true)); return false; }
            catch (InvalidOperationException) { return before.SequenceEqual(File.ReadAllBytes(path)); }
        }
        try
        {
            check("missing settings use manual defaults without creating a file", () =>
            {
                var result = new SettingsStore(path).Load();
                return result.Warning == null && result.Settings == new ApplicationSettings() && !File.Exists(path);
            });
            check("automatic preference survives a new store instance", () =>
            {
                var store = new SettingsStore(path); store.Load(); store.Save(new(AutomaticDetectionEnabled: true));
                var result = new SettingsStore(path).Load();
                return result.Warning == null && result.Settings.AutomaticDetectionEnabled;
            });
            check("settings JSON is UTF-8 without BOM and contains version, preference, exclusions and search template", () =>
            {
                var bytes = File.ReadAllBytes(path);
                var text = new UTF8Encoding(false, true).GetString(bytes);
                using var json = System.Text.Json.JsonDocument.Parse(text);
                return !bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 })
                    && json.RootElement.EnumerateObject().Count() == 7
                    && json.RootElement.GetProperty("schemaVersion").GetInt32() == 6;
            });
            check("pause preference replaces previous saved state", () =>
            {
                var store = new SettingsStore(path); store.Load(); store.Save(new());
                return !new SettingsStore(path).Load().Settings.AutomaticDetectionEnabled;
            });
            foreach (var invalid in new[] { "{bad", "null", "{\"automaticDetectionEnabled\":\"yes\"}" })
                check("invalid settings preserve original bytes: " + invalid, () =>
                {
                    Seed(invalid);
                    var store = new SettingsStore(path); var result = store.Load();
                    return result.Warning == "settings-invalid" && !result.Settings.AutomaticDetectionEnabled && RejectsSave(store);
                });
            check("future settings version is preserved and cannot be downgraded", () =>
            {
                Seed("{\"schemaVersion\":7,\"automaticDetectionEnabled\":true}");
                var store = new SettingsStore(path); var result = store.Load();
                return result.Warning == "settings-version" && !result.Settings.AutomaticDetectionEnabled && RejectsSave(store);
            });
            check("oversized settings are rejected before deserializing", () =>
            {
                Seed(new string(' ', 64 * 1024 + 1));
                var store = new SettingsStore(path);
                return store.Load().Warning == "settings-too-large" && RejectsSave(store);
            });
            check("invalid UTF-8 cannot silently change settings", () =>
            {
                File.WriteAllBytes(path, [0xFF, 0xFE, 0x41]);
                var store = new SettingsStore(path);
                return store.Load().Warning == "settings-invalid" && RejectsSave(store);
            });
            check("failed replacement preserves saved settings and removes temporary file", () =>
            {
                Seed("{\"schemaVersion\":1,\"automaticDetectionEnabled\":false}");
                var store = new SettingsStore(path); store.Load();
                var before = File.ReadAllBytes(path);
                using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try { store.Save(new(AutomaticDetectionEnabled: true)); return false; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { return before.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(directory, "*.tmp").Length == 0; }
            });
            check("successful writes leave no temporary files", () =>
            {
                var store = new SettingsStore(path); store.Load(); store.Save(new());
                return Directory.GetFiles(directory, "*.tmp").Length == 0;
            });
            check("v1 settings migrate in memory while preserving automatic preference and original file", () =>
            {
                Seed("{\"schemaVersion\":1,\"automaticDetectionEnabled\":true}");
                var before = File.ReadAllBytes(path);
                var store = new SettingsStore(path); var loaded = store.Load();
                if (loaded.Warning != null || loaded.Settings.SchemaVersion != 6
                    || !loaded.Settings.AutomaticDetectionEnabled || loaded.Settings.ExcludedApplications.Length != 0
                    || !before.SequenceEqual(File.ReadAllBytes(path))) return false;
                store.Save(loaded.Settings);
                return File.ReadAllText(path).Contains("\"schemaVersion\": 6");
            });
            check("excluded apps and Unicode names survive a new store instance", () =>
            {
                var store = new SettingsStore(path); store.Load();
                var entry = AppExclusionPolicy.Create(@"D:\Apps\Editor\editor.exe", "測試編輯器");
                store.Save(new() { ExcludedApplications = [entry] });
                var loaded = new SettingsStore(path).Load();
                return loaded.Warning == null && loaded.Settings.ExcludedApplications.SequenceEqual(new[] { entry });
            });
            foreach (var list in new[] { "null", "[null]", "[{\"executablePath\":\"editor.exe\",\"displayName\":\"Editor\"}]" })
                check("invalid exclusion list preserves original settings: " + list, () =>
                {
                    Seed("{\"schemaVersion\":2,\"excludedApplications\":" + list + "}");
                    var store = new SettingsStore(path);
                    return store.Load().Warning == "settings-invalid" && RejectsSave(store);
                });
            check("oversized serialized settings never replace a readable original", () =>
            {
                Seed("{\"schemaVersion\":2,\"automaticDetectionEnabled\":false}");
                var store = new SettingsStore(path); store.Load();
                var before = File.ReadAllBytes(path);
                var apps = Enumerable.Range(0, 128).Select(i => AppExclusionPolicy.Create(
                    @"D:\Apps\" + new string('x', 450) + "\\" + i + ".exe", new string('n', 128))).ToArray();
                try { store.Save(new() { ExcludedApplications = apps }); return false; }
                catch (InvalidOperationException)
                { return before.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(directory, "*.tmp").Length == 0; }
            });
            check("v2 migration preserves exclusion list and supplies default search engine", () =>
            {
                Seed("{\"schemaVersion\":2,\"automaticDetectionEnabled\":true,\"excludedApplications\":[{\"executablePath\":\"D:/Apps/Editor/editor.exe\",\"displayName\":\"Editor\"}]}");
                var loaded = new SettingsStore(path).Load();
                return loaded.Warning == null && loaded.Settings.SchemaVersion == 6
                    && loaded.Settings.AutomaticDetectionEnabled && loaded.Settings.ExcludedApplications.Length == 1
                    && loaded.Settings.SearchUrlTemplate == WebActionPolicy.DefaultSearchTemplate;
            });
            check("custom search template survives saving other preferences and restart", () =>
            {
                var store = new SettingsStore(path); var settings = store.Load().Settings;
                const string template = "https://example.com/search?term={query}";
                store.Save(settings with { SearchUrlTemplate = template, AutomaticDetectionEnabled = false });
                var loaded = new SettingsStore(path).Load();
                return loaded.Warning == null && loaded.Settings.SearchUrlTemplate == template
                    && !loaded.Settings.AutomaticDetectionEnabled && loaded.Settings.ExcludedApplications.Length == 1;
            });
            foreach (var template in new[] { "null", "\"file:///tmp?q={query}\"", "\"https://example.com/\"" })
                check("invalid stored search template preserves original file: " + template, () =>
                {
                    Seed("{\"schemaVersion\":3,\"searchUrlTemplate\":" + template + "}");
                    var store = new SettingsStore(path);
                    return store.Load().Warning == "settings-invalid" && RejectsSave(store);
                });
            check("compatibility defaults enabled with a distinct unset legacy Sublime preference", () =>
            {
                Seed("{\"schemaVersion\":5,\"automaticDetectionEnabled\":true}");
                var before = File.ReadAllBytes(path); var loaded = new SettingsStore(path).Load();
                return loaded.Warning == null && loaded.Settings.ClipboardCompatibilityEnabled
                    && loaded.Settings.SublimeCompatibilityEnabled == null && before.SequenceEqual(File.ReadAllBytes(path));
            });
            check("disabled compatibility and Sublime choices survive other preference changes and restart", () =>
            {
                var store = new SettingsStore(path); var settings = store.Load().Settings;
                store.Save(settings with { ClipboardCompatibilityEnabled = false, SublimeCompatibilityEnabled = false });
                store = new SettingsStore(path); settings = store.Load().Settings;
                store.Save(settings with { AutomaticDetectionEnabled = false });
                var restored = new SettingsStore(path).Load().Settings;
                return !restored.ClipboardCompatibilityEnabled && restored.SublimeCompatibilityEnabled == false;
            });
            check("enabled Sublime choice survives restart", () =>
            {
                var store = new SettingsStore(path); var settings = store.Load().Settings;
                store.Save(settings with { ClipboardCompatibilityEnabled = true, SublimeCompatibilityEnabled = true });
                var restored = new SettingsStore(path).Load().Settings;
                return restored.ClipboardCompatibilityEnabled && restored.SublimeCompatibilityEnabled == true;
            });
            check("clipboard metadata distinguishes actual selections from empty-line copying", () =>
                ClipboardSelectionPolicy.IsSelectionMetadata(null)
                && ClipboardSelectionPolicy.IsSelectionMetadata("{\"isFromEmptySelection\":false}")
                && !ClipboardSelectionPolicy.IsSelectionMetadata("{\"isFromEmptySelection\":true}")
                && !ClipboardSelectionPolicy.IsSelectionMetadata("{\"isFromEmptySelection\":false,\"isFromEmptySelection\":true}")
                && !ClipboardSelectionPolicy.IsSelectionMetadata("{}")
                && !ClipboardSelectionPolicy.IsSelectionMetadata("invalid"));
        }
        finally
        {
            // Only files inside this uniquely created test directory are removed, non-recursively.
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
