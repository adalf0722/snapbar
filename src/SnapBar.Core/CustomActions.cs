using System.Text;
using System.Text.Json;

namespace SnapBar.Core;

public enum CustomActionKind { Url, PowerShell, JavaScript }
public enum ActionOutput { None, Clipboard, ReplaceSelection, Hud, Window, Markdown }
public enum ActionPlacement { Bar, More, Hidden }
public sealed record CustomActionDefinition(string Id, string Name, CustomActionKind Kind, string Icon, string Content,
    ActionOutput Output = ActionOutput.Window, int TimeoutSeconds = 10, string? InterpreterPath = null, bool UseSelection = true);
public sealed record ActionLayoutEntry(string Id, ActionPlacement Placement);
public sealed record ActionSettings
{
    public CustomActionDefinition[] CustomActions { get; init; } = [];
    public ActionLayoutEntry[] Layout { get; init; } = [];
    public string[] RecentIds { get; init; } = [];
    public bool ShowLabels { get; init; } = true;
    public string Theme { get; init; } = "Dark";
}
public sealed record ActionItem(string Id, string Name, string Icon, TextAction? BuiltIn = null,
    CustomActionDefinition? Custom = null);

public static class CustomActionPolicy
{
    internal static readonly HashSet<string> Icons = ["copy", "cut", "paste", "search", "link", "folder", "globe", "translate", "code", "terminal", "spark", "document", "clean"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private sealed record Envelope(int Version, CustomActionDefinition Action);
    internal static bool ValidId(string? id) => id is { Length: 39 } && id.StartsWith("custom-", StringComparison.Ordinal)
        && Guid.TryParseExact(id[7..], "N", out _);

    public static string? Validate(CustomActionDefinition action)
    {
        if (action == null || !ValidId(action.Id)) return "動作識別碼無效。";
        if (string.IsNullOrWhiteSpace(action.Name) || action.Name.Length > 32 || action.Name.Any(char.IsControl)) return "名稱須為 1 至 32 個字元，且不可包含控制字元。";
        if (!Enum.IsDefined(action.Kind) || !Enum.IsDefined(action.Output)) return "動作類型或輸出方式無效。";
        if (action.TimeoutSeconds is < 1 or > 300) return "逾時須為 1 至 300 秒。";
        if (string.IsNullOrWhiteSpace(action.Content) || action.Content.Length > 32768) return "內容須為 1 至 32768 個字元。";
        if (action.InterpreterPath != null && (action.InterpreterPath.Length > 32768 || action.InterpreterPath.Any(char.IsControl))) return "解譯器路徑無效。";
        if (action.Kind == CustomActionKind.Url && !action.UseSelection) return "網址動作必須使用選取文字。";
        if (action.Kind == CustomActionKind.Url && !ValidTemplate(action.Content)) return "網址須使用 HTTP 或 HTTPS，並在路徑、查詢或片段包含一次 ***。";
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
        if (Validate(action) is { } error) throw new ArgumentException(error, nameof(action));
        return JsonSerializer.Serialize(new Envelope(2, action with { Icon = Icons.Contains(action.Icon ?? "") ? action.Icon! : "spark" }), JsonOptions);
    }

    public static CustomActionDefinition Import(string json)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > 256 * 1024) throw new ArgumentException("匯入檔案過大。", nameof(json));
        try
        {
            using var document = JsonDocument.Parse(json);
            RejectDuplicates(document.RootElement);
            var envelope = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
            if (envelope?.Version is not (1 or 2) || envelope.Action == null || (envelope.Version == 1 && !envelope.Action.UseSelection))
                throw new ArgumentException("不支援的動作檔案版本。", nameof(json));
            if (Validate(envelope.Action) is { } error) throw new ArgumentException(error, nameof(json));
            return envelope.Action with { Id = "custom-" + Guid.NewGuid().ToString("N"), Icon = Icons.Contains(envelope.Action.Icon ?? "") ? envelope.Action.Icon! : "spark" };
        }
        catch (JsonException error) { throw new ArgumentException("動作檔案格式無效。", nameof(json), error); }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("動作檔案包含重複欄位。");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }
}
