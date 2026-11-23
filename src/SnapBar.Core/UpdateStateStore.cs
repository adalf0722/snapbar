using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace SnapBar.Core;

public sealed record UpdateState
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? LastAttemptUtc { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public DateTimeOffset? RetryAtUtc { get; init; }
    public DateTimeOffset? CandidatesFetchedAtUtc { get; init; }
    public string[] NotifiedVersions { get; init; } = [];
    public UpdateRelease? Latest { get; init; }
    public UpdateRelease? Notification { get; init; }
}

public sealed class UpdateStateStore
{
    public const int MaximumStateBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string path;
    private readonly object sync = new();
    public bool Writable { get; private set; } = true;

    public UpdateStateStore(string? path = null) => this.path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapBar", "updates.json");

    public UpdateState Load()
    {
        lock (sync)
        {
            if (!Writable) return new() { Enabled = false };
            try
            {
                if (Directory.Exists(path)) throw new IOException("Update state path is a directory.");
                var state = File.Exists(path) ? ReadExisting() : new();
                EnsureWritable();
                return state;
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                Writable = false;
                return new() { Enabled = false };
            }
        }
    }

    public bool Save(UpdateState state)
    {
        lock (sync)
        {
            if (!Writable || !IsValid(state, allowExtraNotified: true)) return false;
            var normalized = state with
            {
                NotifiedVersions = state.NotifiedVersions.Distinct(StringComparer.Ordinal)
                    .OrderByDescending(ProductVersion.Parse).Take(64).ToArray()
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions);
            if (bytes.Length > MaximumStateBytes) return false;
            string? temporary = null;
            try
            {
                // Revalidate the destination before replacement, including files changed since Load.
                if (File.Exists(path)) ReadExisting();
                var fullPath = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
                return true;
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                Writable = false;
                return false;
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }

    private UpdateState ReadExisting()
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumStateBytes) throw new JsonException("Update state exceeds the size limit.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        _ = new UTF8Encoding(false, true).GetString(bytes);
        using var document = JsonDocument.Parse(bytes);
        RequireUniqueProperties(document.RootElement);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1
            || !document.RootElement.TryGetProperty("enabled", out var enabled)
            || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException("Unsupported update state.");
        var state = JsonSerializer.Deserialize<UpdateState>(bytes, JsonOptions);
        if (!IsValid(state, allowExtraNotified: false)) throw new JsonException("Invalid update state.");
        return state!;
    }

    private void EnsureWritable()
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            using var existing = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        }
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        using var probe = new FileStream(Path.Combine(directory, ".updates-" + Guid.NewGuid().ToString("N") + ".tmp"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
    }

    private static bool IsValid(UpdateState? state, bool allowExtraNotified) => state != null && state.SchemaVersion == 1
        && Utc(state.LastAttemptUtc) && Utc(state.LastSuccessUtc) && Utc(state.RetryAtUtc) && Utc(state.CandidatesFetchedAtUtc)
        && state.NotifiedVersions != null && (allowExtraNotified || state.NotifiedVersions.Length <= 64)
        && state.NotifiedVersions.All(UpdatePolicy.IsPublicVersion)
        && ValidRelease(state.Latest) && ValidRelease(state.Notification)
        && (state.Notification == null || state.Notification.Metadata is { DesktopChanged: true });

    private static bool Utc(DateTimeOffset? value) => value == null || value.Value.Offset == TimeSpan.Zero;
    private static bool ValidRelease(UpdateRelease? release) => release == null || UpdatePolicy.IsPublicVersion(release.Version)
        && (release.Metadata == null || UpdatePolicy.IsValidMetadata(release.Metadata, release.Version));

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate update state property.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RequireUniqueProperties(item);
    }

    private static bool IsStorageError(Exception exception) => exception is IOException or UnauthorizedAccessException
        or JsonException or ArgumentException or InvalidOperationException or OverflowException or NotSupportedException;
}
