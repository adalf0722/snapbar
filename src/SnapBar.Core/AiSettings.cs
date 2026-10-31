using System.Text;
using System.Text.Json;

namespace SnapBar.Core;

public enum AiProtocol { Responses, ChatCompletions }
public sealed record AiProviderProfile(string Id, string Name, AiProtocol Protocol, string Endpoint, string Model);
public sealed record AiPrompt(string Id, string Name, string Instruction, string Category, string Keywords = "", string Icon = "spark");
public sealed record AiSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string SelectedProfileId { get; init; } = "openai";
    public AiProviderProfile[] Profiles { get; init; } =
    [new("openai", "OpenAI", AiProtocol.Responses, "https://api.openai.com/v1/responses", ""),
     new("compatible", "自訂相容服務", AiProtocol.ChatCompletions, "http://localhost:11434/v1/chat/completions", "")];
    public AiPrompt[] Prompts { get; init; } =
    [new("grammar", "修正文法拼字", "Correct grammar and spelling while preserving meaning. Return only the corrected text.", "寫作", "grammar spelling correct"),
     new("improve", "改善寫作", "Improve clarity and flow while preserving meaning. Return only the revised text.", "寫作", "improve writing rewrite"),
     new("shorten", "縮短", "Make the text more concise while preserving key information. Return only the revised text.", "寫作", "shorten concise"),
     new("summary", "摘要", "Summarize the key ideas in the source language. Return only the summary.", "閱讀", "summary summarize"),
     new("translate-en", "翻成英文", "Translate the text into natural English. Return only the translation.", "翻譯", "translate english")];
    public string[] PinnedIds { get; init; } = ["improve", "shorten", "translate-en"];
    public string[] RecentIds { get; init; } = [];
}
public sealed record AiSettingsLoadResult(AiSettings Settings, string? Warning = null);
public sealed record AiTextResult(bool Success, string Text = "", string Status = "ok");
public interface IAiTextClient
{
    Task<AiTextResult> ExecuteAsync(AiProviderProfile profile, string apiKey, string sourceText, string instruction, CancellationToken cancellationToken);
}

public static class AiPolicy
{
    private static bool Text(string? value, int minimum, int maximum) => value != null && value.Length >= minimum && value.Length <= maximum
        && (minimum == 0 || !string.IsNullOrWhiteSpace(value));

    public static string? ValidateProfile(AiProviderProfile profile, bool requireModel = true)
    {
        if (profile == null || !Text(profile.Id, 1, 64) || !Text(profile.Name, 1, 64) || !Enum.IsDefined(profile.Protocol))
            return "Invalid provider profile.";
        if (!Text(profile.Endpoint, 1, 2048) || profile.Endpoint.Any(char.IsWhiteSpace) || profile.Endpoint.Contains('\\')
            || !Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || profile.Endpoint.Contains('?') || profile.Endpoint.Contains('#') || string.IsNullOrEmpty(uri.Host))
            return "Use a full HTTPS endpoint or loopback HTTP endpoint without credentials, query, or fragment.";
        if (!Text(profile.Model, 0, 128) || (requireModel && string.IsNullOrWhiteSpace(profile.Model)))
            return "Enter a model name (up to 128 characters).";
        return null;
    }

    public static AiSettings Normalize(AiSettings settings)
    {
        if (settings == null || settings.SchemaVersion != 1 || settings.Profiles == null || settings.Profiles.Length is < 1 or > 8
            || settings.Prompts == null || settings.Prompts.Length > 128)
            throw new ArgumentException("Invalid AI settings.");
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in settings.Profiles)
            if (ValidateProfile(profile, false) != null || !profileIds.Add(profile.Id)) throw new ArgumentException("Invalid provider profile.");
        if (settings.SelectedProfileId == null || !profileIds.Contains(settings.SelectedProfileId)) throw new ArgumentException("Unknown provider profile.");
        var promptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prompt in settings.Prompts)
            if (prompt == null || !Text(prompt.Id, 1, 64) || !promptIds.Add(prompt.Id) || !Text(prompt.Name, 1, 64)
                || !Text(prompt.Instruction, 1, 8192) || !Text(prompt.Category, 0, 32) || !Text(prompt.Keywords, 0, 256)
                || !Text(prompt.Icon, 1, 64)) throw new ArgumentException("Invalid AI prompt.");
        string[] Filter(string[]? ids, int maximum) => (ids ?? []).Where(id => id != null && promptIds.Contains(id)).Distinct(StringComparer.Ordinal).Take(maximum).ToArray();
        return settings with { Profiles = [.. settings.Profiles], Prompts = [.. settings.Prompts], PinnedIds = Filter(settings.PinnedIds, 128), RecentIds = Filter(settings.RecentIds, 10) };
    }

    public static IReadOnlyList<AiPrompt> Search(AiSettings settings, string query, string category = "")
    {
        settings = Normalize(settings);
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var orderedIds = settings.PinnedIds.Concat(settings.RecentIds).Concat(settings.Prompts.Select(p => p.Id)).Distinct(StringComparer.Ordinal);
        var byId = settings.Prompts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        return orderedIds.Select(id => byId[id]).Where(p => words.Length > 0
            ? words.All(word => (p.Name + "\n" + p.Instruction + "\n" + p.Keywords).Contains(word, StringComparison.OrdinalIgnoreCase))
            : string.IsNullOrEmpty(category) || p.Category == category).ToArray();
    }
}

public sealed class AiSettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private const int MaximumBytes = 512 * 1024;
    private readonly string filePath = Path.GetFullPath(path);
    private bool writable;

    public AiSettingsLoadResult Load()
    {
        writable = false;
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumBytes) return new(new(), "settings-too-large");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return new(new(), "settings-invalid");
            var json = new UTF8Encoding(false, true).GetString(bytes);
            var settings = JsonSerializer.Deserialize<AiSettings>(json, JsonOptions);
            if (settings == null) return new(new(), "settings-invalid");
            if (settings.SchemaVersion != 1) return new(new(), "settings-version");
            settings = AiPolicy.Normalize(settings);
            writable = true;
            return new(settings);
        }
        catch (FileNotFoundException) { writable = true; return new(new()); }
        catch (DirectoryNotFoundException) { writable = true; return new(new()); }
        catch (Exception e) when (e is JsonException or ArgumentException) { return new(new(), "settings-invalid"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(new(), "settings-unavailable"); }
    }

    public void Save(AiSettings settings)
    {
        if (!writable) throw new InvalidOperationException("Existing AI settings must be preserved.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(AiPolicy.Normalize(settings), JsonOptions);
        if (bytes.Length > MaximumBytes) throw new InvalidOperationException("AI settings exceed 512 KiB.");
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".ai-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, filePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
