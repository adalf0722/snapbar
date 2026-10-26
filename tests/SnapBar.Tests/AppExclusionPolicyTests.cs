using SnapBar.Core;

internal static class AppExclusionPolicyTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        var entry = AppExclusionPolicy.Create(@"D:\Apps\Editor\editor.exe", "Editor");
        bool Allowed(string? path) => AppExclusionPolicy.AllowsAutomaticCapture([entry], path);
        bool Invalid(Action work)
        { try { work(); return false; } catch (ArgumentException) { return true; } }
        check("excluded executable exact path suppresses automatic capture", () => !Allowed(entry.ExecutablePath));
        check("exclusions match Windows paths without case sensitivity", () => !Allowed(@"d:\apps\EDITOR\EDITOR.EXE"));
        check("same executable name in another installation remains allowed", () => Allowed(@"D:\Other\editor.exe"));
        check("path prefix never excludes a sibling executable", () => Allowed(@"D:\Apps\Editor\editor-helper.exe"));
        check("excluded folder is not treated as a recursive exclusion", () => Allowed(@"D:\Apps\Editor\Child\editor.exe"));
        check("unknown executable path stops automatic capture when exclusions exist", () => !Allowed(null));
        check("empty list does not require executable metadata", () => AppExclusionPolicy.AllowsAutomaticCapture([], null));
        check("slash and dot segments normalize before comparison", () => !Allowed("D:/Apps/Editor/../Editor/editor.exe"));
        check("extended DOS path matches its normal Windows path", () => !Allowed(@"\\?\D:\Apps\Editor\editor.exe"));
        check("extended UNC path matches a canonical network executable", () =>
        {
            var network = AppExclusionPolicy.Create(@"\\server\share\Editor.exe");
            return !AppExclusionPolicy.AllowsAutomaticCapture([network], @"\\?\UNC\SERVER\share\EDITOR.EXE");
        });
        foreach (var invalid in new[] { "editor.exe", @"D:editor.exe", @"D:\Apps\*.exe", @"D:\Apps\note.txt", @"\\.\device.exe" })
            check("invalid or ambiguous executable path is rejected: " + invalid, () =>
                AppExclusionPolicy.NormalizePath(invalid) == null && Invalid(() => AppExclusionPolicy.Create(invalid)));
        check("duplicate exclusions collapse while preserving first display name", () =>
        {
            var normalized = AppExclusionPolicy.Normalize([entry, new(@"d:\apps\editor\editor.exe", "Duplicate")]);
            return normalized.Length == 1 && normalized[0] == entry;
        });
        check("empty display name falls back to executable name", () =>
            AppExclusionPolicy.Create(entry.ExecutablePath).DisplayName == "editor");
        check("display names are bounded and control characters removed", () =>
        {
            var name = AppExclusionPolicy.Create(entry.ExecutablePath, "\n" + new string('x', 200) + "\t").DisplayName;
            return name.Length == 128 && !name.Any(char.IsControl);
        });
        check("null exclusion list is rejected", () => Invalid(() => AppExclusionPolicy.Normalize(null)));
        check("null exclusion entry is rejected", () => Invalid(() => AppExclusionPolicy.Normalize([null!])));
        check("exclusion list has a finite application limit", () => Invalid(() =>
            AppExclusionPolicy.Normalize(Enumerable.Repeat(entry, AppExclusionPolicy.MaximumApplications + 1).ToArray())));
    }
}
