using System.Text;
using System.Text.RegularExpressions;

namespace SnapBar.Core;

public sealed record PathDestination(string Status, string? Path = null)
{
    public bool Success => Status == "ok" && Path != null;
}

public static class PathActionPolicy
{
    public const int MaximumLength = 32760;

    // Lexical only: displaying a toolbar must never query a drive or network share.
    public static PathDestination Resolve(string? text, string? userProfile = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumLength || text.Any(char.IsControl)) return new("invalid-path");
        var path = text.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"') path = path[1..^1];
        path = path.Replace('/', '\\');
        if (path.Equals("~", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            path = Profile(userProfile) + path[1..];
        else if (path.Equals("%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("%USERPROFILE%\\", StringComparison.OrdinalIgnoreCase))
            path = Profile(userProfile) + path[13..];
        if (path.Length > MaximumLength || Regex.IsMatch(path, "%[^%]+%")
            || path.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0
            || path.Any(char.IsControl) || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return new("invalid-path");
        try { _ = new UTF8Encoding(false, true).GetByteCount(path); }
        catch (EncoderFallbackException) { return new("invalid-path"); }

        bool drive = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';
        bool unc = path.StartsWith(@"\\", StringComparison.Ordinal);
        if (!drive && !unc || path[(drive ? 2 : 0)..].Contains(':')) return new("invalid-path");
        var parts = path[(drive ? 3 : 2)..].Split('\\');
        if (unc && (parts.Length < 2 || !ValidSegment(parts[0]) || !ValidSegment(parts[1])
            || parts[0].Any(char.IsWhiteSpace) || parts[0] is "." or ".." || parts[1] is "." or "..")) return new("invalid-path");
        if (parts.Skip(unc ? 2 : 0).Any(part => part.Length != 0 && part is not ("." or "..") && !ValidSegment(part)))
            return new("invalid-path");
        try
        {
            path = System.IO.Path.GetFullPath(path);
            var root = System.IO.Path.GetPathRoot(path)!;
            if (path.Length > root.Length) path = path.TrimEnd('\\');
            return new("ok", path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { return new("invalid-path"); }
    }

    private static string Profile(string? profile) => (profile
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd('\\', '/');

    private static bool ValidSegment(string part)
    {
        if (part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.')) return false;
        var name = part.Split('.')[0].ToUpperInvariant();
        return name is not ("CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" or "CONIN$" or "CONOUT$")
            && !Regex.IsMatch(name, @"^(COM|LPT)[1-9¹²³]$");
    }
}
