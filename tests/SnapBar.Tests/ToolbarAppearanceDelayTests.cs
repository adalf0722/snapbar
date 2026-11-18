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
                const string invalid = """{"schemaVersion":8,"toolbarAppearanceDelayMilliseconds":"173"}""";
                File.WriteAllText(path, invalid);
                if (store.Load().Warning != "settings-invalid") return false;
                try { store.Save(new()); return false; }
                catch (InvalidOperationException) { return File.ReadAllText(path) == invalid; }
            });
        }
        finally { directory.Delete(recursive: true); }
    }
}
