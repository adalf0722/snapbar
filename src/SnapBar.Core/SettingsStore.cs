using System.Text;
using System.Text.Json;

namespace SnapBar.Core;

public sealed record ApplicationSettings(int SchemaVersion = 4, bool AutomaticDetectionEnabled = false)
{
    public ExcludedApplication[] ExcludedApplications { get; init; } = [];
    public ActionSettings Actions { get; init; } = new();
    public string SearchUrlTemplate { get; init; } = WebActionPolicy.DefaultSearchTemplate;
}
public sealed record SettingsLoadResult(ApplicationSettings Settings, string? Warning = null);

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private bool writable;
    public string FilePath { get; } = Path.GetFullPath(path);

    public SettingsLoadResult Load()
    {
        writable = false;
        try
        {
            // File.ReadAllText must not allocate an arbitrarily large user-edited file.
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 64 * 1024) return new(new(), "settings-too-large");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(reader.ReadToEnd(), JsonOptions);
            if (settings == null) return new(new(), "settings-invalid");
            if (settings.SchemaVersion is not (1 or 2 or 3 or 4)) return new(new(), "settings-version");
            if (!WebActionPolicy.IsValidSearchTemplate(settings.SearchUrlTemplate)) return new(new(), "settings-invalid");
            settings = settings with { SchemaVersion = 4,
                ExcludedApplications = AppExclusionPolicy.Normalize(settings.ExcludedApplications), Actions = ActionCatalog.Normalize(settings.Actions) };
            writable = true;
            return new(settings);
        }
        catch (FileNotFoundException) { writable = true; return new(new()); }
        catch (DirectoryNotFoundException) { writable = true; return new(new()); }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or ArgumentException)
        { return new(new(), "settings-invalid"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return new(new(), "settings-unavailable"); }
    }

    public void Save(ApplicationSettings settings)
    {
        if (!writable || settings.SchemaVersion != 4)
            throw new InvalidOperationException("Existing settings must be preserved");
        if (!WebActionPolicy.IsValidSearchTemplate(settings.SearchUrlTemplate))
            throw new InvalidOperationException("Invalid search template");
        settings = settings with { ExcludedApplications = AppExclusionPolicy.Normalize(settings.ExcludedApplications), Actions = ActionCatalog.Normalize(settings.Actions) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        if (bytes.Length > 64 * 1024) throw new InvalidOperationException("Settings exceed 64 KiB");
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
