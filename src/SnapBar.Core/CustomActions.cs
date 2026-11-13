using System.Text;
using System.Text.Json;

namespace SnapBar.Core;

public enum CustomActionKind { Url, PowerShell, JavaScript, Python }
public enum ActionOutput { None, Clipboard, ReplaceSelection, Hud, Window, Markdown }
public enum ActionPlacement { Bar, More, Hidden }
public sealed record CustomActionDefinition(string Id, string Name, CustomActionKind Kind, string Icon, string Content,
    ActionOutput Output = ActionOutput.Window, int TimeoutSeconds = 10, string? InterpreterPath = null, bool UseSelection = true);
public sealed record ActionLayoutEntry(string Id, ActionPlacement Placement);
public sealed record ActionSettings
{
    public CustomActionDefinition[] CustomActions { get; init; } = [];
    public bool GmailSearchRemoved { get; init; }
    public bool PublicIpRemoved { get; init; }
    public ActionLayoutEntry[] Layout { get; init; } = [];
    public string[] RecentIds { get; init; } = [];
    public bool ShowLabels { get; init; } = true;
    public string Theme { get; init; } = "Dark";
}
public sealed record ActionItem(string Id, string Name, string Icon, TextAction? BuiltIn = null,
    CustomActionDefinition? Custom = null);

public static class CustomActionPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private sealed record Envelope(int Version, CustomActionDefinition Action);
    internal static string NormalizeIcon(string? value) =>
        value is { Length: > 0 and <= 128 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ':')
            ? value : "spark";
    internal static bool ValidId(string? id) => id is { Length: 39 } && id.StartsWith("custom-", StringComparison.Ordinal)
        && Guid.TryParseExact(id[7..], "N", out _);

    public static string? Validate(CustomActionDefinition action)
    {
        if (action == null || !ValidId(action.Id)) return "action-id-invalid";
        if (string.IsNullOrWhiteSpace(action.Name) || action.Name.Length > 32 || action.Name.Any(char.IsControl)) return "action-name-invalid";
        if (!Enum.IsDefined(action.Kind) || !Enum.IsDefined(action.Output)) return "action-kind-invalid";
        if (action.TimeoutSeconds is < 1 or > 300) return "action-timeout-invalid";
        if (string.IsNullOrWhiteSpace(action.Content) || action.Content.Length > 32768) return "action-content-invalid";
        if (action.InterpreterPath != null && (action.InterpreterPath.Length > 32768 || action.InterpreterPath.Any(char.IsControl))) return "action-interpreter-invalid";
        if (action.Kind == CustomActionKind.Url && !action.UseSelection) return "action-selection-required";
        if (action.Kind == CustomActionKind.Url && !ValidTemplate(action.Content)) return "action-template-invalid";
        return null;
    }

    private static bool ValidTemplate(string? template)
    {
        if (template == null || template.Length > 2048) return false;
        int index = template.IndexOf("***", StringComparison.Ordinal);
        if (index < 0 || template.LastIndexOf("***", StringComparison.Ordinal) != index) return false;
        int scheme = template.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0) return false;
        int end = template.IndexOfAny(['/', '?', '#'], scheme + 3);
        return end >= 0 && index > end && WebActionPolicy.OpenLink(template.Replace("***", "snapbar", StringComparison.Ordinal)).Success;
    }

    public static WebDestination ResolveUrl(string text, string template)
    {
        if (!ValidTemplate(template)) return new("invalid-action-template");
        if (string.IsNullOrWhiteSpace(text)) return new("no-selection");
        if (text.Length > 32768) return new("web-address-too-long");
        try { _ = new UTF8Encoding(false, true).GetByteCount(text); }
        catch (EncoderFallbackException) { return new("invalid-link"); }
        return WebActionPolicy.OpenLink(template.Replace("***", Uri.EscapeDataString(text), StringComparison.Ordinal));
    }

    public static string Export(CustomActionDefinition action)
    {
        if (Validate(action) is { } error) throw new ProductValidationException(error);
        return JsonSerializer.Serialize(new Envelope(2, action with { Icon = NormalizeIcon(action.Icon) }), JsonOptions);
    }

    public static CustomActionDefinition Import(string json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > 256 * 1024) throw new ProductValidationException("action-import-too-large");
        try
        {
            using var document = JsonDocument.Parse(json);
            RejectDuplicates(document.RootElement);
            var envelope = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
            if (envelope?.Version is not (1 or 2) || envelope.Action == null || (envelope.Version == 1 && !envelope.Action.UseSelection))
                throw new ProductValidationException("action-import-version");
            if (Validate(envelope.Action) is { } error) throw new ProductValidationException(error);
            return envelope.Action with { Id = "custom-" + Guid.NewGuid().ToString("N"), Icon = NormalizeIcon(envelope.Action.Icon) };
        }
        catch (JsonException) { throw new ProductValidationException("action-import-invalid"); }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ProductValidationException("action-import-duplicate-field");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }
}
