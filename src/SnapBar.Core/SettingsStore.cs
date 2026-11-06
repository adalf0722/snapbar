using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapBar.Core;

public sealed record ApplicationSettings(int SchemaVersion = 7, bool AutomaticDetectionEnabled = false)
{
    public HotkeySettings Hotkey { get; init; } = HotkeySettings.Default;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HotkeySettings? SearchHotkey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HotkeySettings? CopyHotkey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HotkeySettings? TranslationHotkey { get; init; }
    public bool ClipboardCompatibilityEnabled { get; init; } = true;
    public bool? SublimeCompatibilityEnabled { get; init; }
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
    private bool backupBeforeSave;
    public string FilePath { get; } = Path.GetFullPath(path);

    public SettingsLoadResult Load()
    {
        writable = false;
        backupBeforeSave = false;
        try
        {
            // File.ReadAllText must not allocate an arbitrarily large user-edited file.
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 64 * 1024) return new(new(), "settings-too-large");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(reader.ReadToEnd(), JsonOptions);
            if (settings == null) return new(new(), "settings-invalid");
            if (settings.SchemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7)) return new(new(), "settings-version");
            if (!WebActionPolicy.IsValidSearchTemplate(settings.SearchUrlTemplate)) return new(new(), "settings-invalid");
            backupBeforeSave = settings.Hotkey?.IsValid != true;
            settings = settings with { SchemaVersion = 7, Hotkey = backupBeforeSave ? HotkeySettings.Default : settings.Hotkey!,
                ExcludedApplications = AppExclusionPolicy.Normalize(settings.ExcludedApplications), Actions = ActionCatalog.Normalize(settings.Actions) };
            settings = NormalizeActionHotkeys(settings);
            writable = true;
            return new(settings, backupBeforeSave ? "hotkey-invalid" : null);
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
        if (!writable || settings.SchemaVersion != 7)
            throw new InvalidOperationException("Existing settings must be preserved");
        if (!WebActionPolicy.IsValidSearchTemplate(settings.SearchUrlTemplate))
            throw new InvalidOperationException("Invalid search template");
        if (settings.Hotkey?.IsValid != true) throw new InvalidOperationException("Invalid hotkey");
        if (NormalizeActionHotkeys(settings) != settings) throw new InvalidOperationException("Invalid action hotkey");
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
            if (backupBeforeSave && File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".hotkey-recovery-" + Guid.NewGuid().ToString("N") + ".bak");
            File.Move(temporary, FilePath, overwrite: true);
            backupBeforeSave = false;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static ApplicationSettings NormalizeActionHotkeys(ApplicationSettings settings)
    {
        var search = settings.SearchHotkey is { IsValid: true } && settings.SearchHotkey != settings.Hotkey
            ? settings.SearchHotkey : null;
        var copy = settings.CopyHotkey is { IsValid: true } && settings.CopyHotkey != settings.Hotkey && settings.CopyHotkey != search
            ? settings.CopyHotkey : null;
        var translation = settings.TranslationHotkey is { IsValid: true } && settings.TranslationHotkey != settings.Hotkey
            && settings.TranslationHotkey != search && settings.TranslationHotkey != copy ? settings.TranslationHotkey : null;
        return settings with { SearchHotkey = search, CopyHotkey = copy, TranslationHotkey = translation };
    }
}
