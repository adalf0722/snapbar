using SnapBar.Core;

internal static class ToolbarAppearanceDelayTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        check("appearance delay counts time already spent capturing and copying", () =>
            ToolbarAppearanceDelay.Remaining(300, TimeSpan.FromMilliseconds(180)) == TimeSpan.FromMilliseconds(120)
            && ToolbarAppearanceDelay.Remaining(300, TimeSpan.FromMilliseconds(450)) == TimeSpan.Zero);
        check("appearance delay clamps milliseconds and negative elapsed time", () =>
            ToolbarAppearanceDelay.Remaining(0, TimeSpan.Zero) == TimeSpan.Zero
            && ToolbarAppearanceDelay.Remaining(-1, TimeSpan.Zero) == TimeSpan.Zero
            && ToolbarAppearanceDelay.Remaining(int.MaxValue, TimeSpan.FromMilliseconds(-1)) == TimeSpan.FromSeconds(2));
        var directory = Directory.CreateTempSubdirectory("SnapBar.Delay.Tests.");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            var store = new SettingsStore(path);
            check("new and schema 8 settings without appearance delay use 120 milliseconds", () =>
            {
                if (store.Load().Settings.ToolbarAppearanceDelayMilliseconds != 120) return false;
                File.WriteAllText(path, """{"schemaVersion":8,"automaticDetectionEnabled":true}""");
                return store.Load() is { Warning: null, Settings: { AutomaticDetectionEnabled: true, ToolbarAppearanceDelayMilliseconds: 120 } };
            });
            check("appearance delay roundtrips exact values without changing other preferences or schema", () =>
            {
                var preferences = store.Load().Settings with { ToolbarAppearanceDelayMilliseconds = 173,
                    ClipboardCompatibilityEnabled = false, SublimeCompatibilityEnabled = false,
                    AutoCopy = new(true, AutoCopyMode.OnSelection, false), InterfaceLanguage = "en" };
                store.Save(preferences);
                var saved = new SettingsStore(path).Load().Settings;
                return saved.SchemaVersion == 8 && saved.ToolbarAppearanceDelayMilliseconds == 173
                    && saved.AutomaticDetectionEnabled && !saved.ClipboardCompatibilityEnabled
                    && saved.SublimeCompatibilityEnabled == false && saved.AutoCopy == preferences.AutoCopy
                    && saved.InterfaceLanguage == preferences.InterfaceLanguage;
            });
            check("appearance delay normalizes bounds when reading and writing", () =>
            {
                File.WriteAllText(path, """{"schemaVersion":8,"toolbarAppearanceDelayMilliseconds":-1}""");
                var low = store.Load().Settings.ToolbarAppearanceDelayMilliseconds;
                store.Save(new() { ToolbarAppearanceDelayMilliseconds = int.MaxValue });
                var high = store.Load().Settings.ToolbarAppearanceDelayMilliseconds;
                return low == 0 && high == 2000;
            });
            check("wrong appearance delay type preserves the original file", () =>
            {
                foreach (var token in new[] { "\"173\"", "null", "true", "[]", "{}", "173.5", "1e20" })
                {
                    var invalid = "{\"schemaVersion\":8,\"toolbarAppearanceDelayMilliseconds\":" + token + "}";
                    File.WriteAllText(path, invalid);
                    if (store.Load().Warning != "settings-invalid") return false;
                    try { store.Save(new()); return false; }
                    catch (InvalidOperationException) { if (File.ReadAllText(path) != invalid) return false; }
                }
                return true;
            });
            foreach (var (raw, expected) in new[]
            {
                ("2147483648", 2000), ("-2147483649", 0),
                (new string('9', 1024), 2000), ("-" + new string('9', 1024), 0)
            })
                check($"out-of-range JSON delay of {raw.Length} characters clamps to {expected} and preserves other preferences", () =>
                {
                    var json = "{\"schemaVersion\":8,\"automaticDetectionEnabled\":true,\"clipboardCompatibilityEnabled\":false,"
                        + "\"interfaceLanguage\":\"en\",\"toolbarAppearanceDelayMilliseconds\":" + raw + "}";
                    File.WriteAllText(path, json);
                    var result = store.Load();
                    bool Matches(ApplicationSettings settings) => settings.SchemaVersion == 8 && settings.AutomaticDetectionEnabled
                        && !settings.ClipboardCompatibilityEnabled && settings.InterfaceLanguage == "en"
                        && settings.ToolbarAppearanceDelayMilliseconds == expected;
                    if (result.Warning != null || !Matches(result.Settings)) return false;
                    store.Save(result.Settings);
                    return Matches(new SettingsStore(path).Load().Settings);
                });
        }
        finally { directory.Delete(recursive: true); }
    }
}
