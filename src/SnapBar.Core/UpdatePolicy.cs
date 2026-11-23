using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SnapBar.Core;

public sealed record UpdateText(string[] Summaries, string[] Notices);
public sealed record UpdateMetadata(int SchemaVersion, string Version, string SourceCommit,
    bool DesktopChanged, UpdateText Zh, UpdateText En);
public sealed record UpdateRelease(string Version, UpdateMetadata? Metadata);
public sealed record UpdateCandidates(UpdateRelease? Latest, UpdateRelease? Notification);

public static class UpdatePolicy
{
    public const int MaximumMetadataBytes = 32 * 1024;
    private const string Marker = "snapbar-update-v1";

    public static UpdateMetadata? ReadMetadata(string? body, string version)
    {
        if (body == null || !IsPublicVersion(version)) return null;
        var marker = body.IndexOf(Marker, StringComparison.Ordinal);
        if (marker < 0 || body.IndexOf(Marker, marker + Marker.Length, StringComparison.Ordinal) >= 0) return null;
        var match = Regex.Match(body, @"<!--\s*snapbar-update-v1:\s*([A-Za-z0-9+/=]+)\s*-->", RegexOptions.CultureInvariant);
        if (!match.Success || match.Groups[1].Length > ((MaximumMetadataBytes + 2) / 3) * 4) return null;
        try
        {
            var bytes = Convert.FromBase64String(match.Groups[1].Value);
            if (bytes.Length > MaximumMetadataBytes) return null;
            var json = new UTF8Encoding(false, true).GetString(bytes);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!HasProperties(root, "schemaVersion", "version", "sourceCommit", "desktopChanged", "zh", "en")) return null;
            var metadata = new UpdateMetadata(root.GetProperty("schemaVersion").GetInt32(),
                root.GetProperty("version").GetString()!, root.GetProperty("sourceCommit").GetString()!,
                root.GetProperty("desktopChanged").GetBoolean(), ReadText(root.GetProperty("zh")), ReadText(root.GetProperty("en")));
            return IsValidMetadata(metadata, version) ? metadata : null;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException
            or DecoderFallbackException or ArgumentException or OverflowException) { return null; }
    }

    public static UpdateCandidates Select(ProductVersion current, IEnumerable<UpdateRelease> releases, IEnumerable<string>? notified = null)
    {
        var parsed = new List<(UpdateRelease Release, ProductVersion Version)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var release in releases)
        {
            if (release == null || !IsPublicVersion(release.Version)) throw new FormatException("Invalid public update version.");
            if (!seen.Add(release.Version)) throw new FormatException("Duplicate update version.");
            parsed.Add((release with { Metadata = IsValidMetadata(release.Metadata, release.Version) ? release.Metadata : null },
                ProductVersion.Parse(release.Version)));
        }
        if (!current.IsPublic) return new(null, null);
        var notifiedFloor = (notified ?? []).Where(IsPublicVersion).Select(ProductVersion.Parse)
            .Where(version => current.Channel == "Preview" || version.Channel == "Stable")
            .OrderByDescending(version => version).FirstOrDefault();
        var candidates = parsed.Where(item => item.Version.CompareTo(current) > 0
            && (current.Channel == "Preview" || item.Version.Channel == "Stable"))
            .OrderByDescending(item => item.Version).ToArray();
        var latest = candidates.FirstOrDefault().Release;
        var notification = candidates.FirstOrDefault(item => (notifiedFloor == null || item.Version.CompareTo(notifiedFloor) > 0)
            && item.Release.Metadata is { DesktopChanged: true } metadata && IsValidMetadata(metadata, item.Release.Version)).Release;
        return new(latest, notification);
    }

    internal static bool IsPublicVersion(string? version)
    {
        try { return ProductVersion.Parse(version!).IsPublic; }
        catch (FormatException) { return false; }
    }

    internal static bool IsValidMetadata(UpdateMetadata? metadata, string version) => metadata != null
        && metadata.SchemaVersion == 1 && metadata.Version == version && IsPublicVersion(version)
        && metadata.SourceCommit != null && Regex.IsMatch(metadata.SourceCommit, @"\A[0-9a-fA-F]{40}\z", RegexOptions.CultureInvariant)
        && IsValidText(metadata.Zh, 140) && IsValidText(metadata.En, 240)
        && JsonSerializer.SerializeToUtf8Bytes(metadata).Length <= MaximumMetadataBytes;

    private static bool IsValidText(UpdateText? text, int summaryLimit) => text != null
        && text.Summaries is { Length: >= 1 and <= 3 } && text.Notices != null
        && text.Summaries.All(value => ValidPlainText(value, summaryLimit))
        && text.Notices.All(value => ValidPlainText(value, 2000))
        && text.Notices.Sum(value => (long)value.Length) <= 2000;

    private static bool ValidPlainText(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) return false;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i])) return false;
            if (!char.IsSurrogate(value[i])) continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 == value.Length || !char.IsLowSurrogate(value[++i])) return false;
        }
        return true;
    }

    private static UpdateText ReadText(JsonElement element)
    {
        if (!HasProperties(element, "summaries", "notices")) throw new FormatException("Invalid localized update text.");
        return new(element.GetProperty("summaries").EnumerateArray().Select(item => item.GetString()!).ToArray(),
            element.GetProperty("notices").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    private static bool HasProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var properties = element.EnumerateObject().Select(property => property.Name).ToArray();
        return properties.Length == names.Length && properties.Distinct(StringComparer.Ordinal).Count() == names.Length
            && names.All(name => properties.Contains(name, StringComparer.Ordinal));
    }
}
