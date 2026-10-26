namespace SnapBar.Core;

public sealed record ExcludedApplication(string ExecutablePath, string DisplayName);

public static class AppExclusionPolicy
{
    public const int MaximumApplications = 128;

    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim().Replace('/', '\\');
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        if (path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.IndexOfAny(['*', '?', '"', '<', '>', '|', '\0']) >= 0)
            return null;
        try
        {
            return Path.IsPathFullyQualified(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(path) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { return null; }
    }

    public static ExcludedApplication Create(string path, string? displayName = null)
    {
        var normalized = NormalizePath(path) ?? throw new ArgumentException("An absolute EXE path is required");
        var name = new string((displayName ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(normalized);
        return new(normalized, name.Length > 128 ? name[..128] : name);
    }

    public static ExcludedApplication[] Normalize(IReadOnlyList<ExcludedApplication>? applications)
    {
        if (applications == null || applications.Count > MaximumApplications)
            throw new ArgumentException("Invalid exclusion list");
        var normalized = new List<ExcludedApplication>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var application in applications)
        {
            if (application == null) throw new ArgumentException("Missing excluded application");
            var entry = Create(application.ExecutablePath, application.DisplayName);
            if (paths.Add(entry.ExecutablePath)) normalized.Add(entry);
        }
        return normalized.ToArray();
    }

    public static bool AllowsAutomaticCapture(IReadOnlyList<ExcludedApplication> applications, string? executablePath)
    {
        if (applications.Count == 0) return true;
        var path = NormalizePath(executablePath);
        return path != null && !applications.Any(app =>
            string.Equals(app.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
    }
}
