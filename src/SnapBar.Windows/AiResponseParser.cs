using System.Text;
using System.Text.Json;
using SnapBar.Core;

namespace SnapBar.Windows;

internal static class AiResponseParser
{
    private const int MaximumText = 262144;
    internal static bool IsModelUnavailable(JsonElement root) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
        && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
        && code.GetString() == "model_not_found";

    public static AiTextResult Parse(AiProviderProfile profile, JsonElement root) =>
        IsModelUnavailable(root) ? Failure("model-unavailable") :
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
        ? Failure("invalid-response") : profile.Protocol switch
    {
        AiProtocol.Responses => ParseResponses(root),
        AiProtocol.ChatCompletions => ParseChat(root, profile.Id == "minimax"),
        AiProtocol.Messages => ParseMessages(root),
        _ => Failure("settings-required")
    };

    private static AiTextResult ParseMessages(JsonElement root)
    {
        if (String(root, "type") != "message") return Failure("invalid-response");
        switch (String(root, "stop_reason"))
        {
            case "end_turn": break;
            case "max_tokens": return Failure("incomplete");
            case "refusal": return Failure("refused");
            default: return Failure("invalid-response");
        }
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return Failure("invalid-response");
        var text = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            var type = String(part, "type");
            if (type is "thinking" or "redacted_thinking") continue;
            if (type != "text") return Failure("invalid-response");
            var value = String(part, "text");
            if (value == null) return Failure("invalid-response");
            if (text.Length + value.Length > MaximumText) return Failure("too-large");
            text.Append(value);
        }
        return Result(text.ToString());
    }

    private static string? FinalMiniMaxText(string text)
    {
        var remaining = text.AsSpan().TrimStart();
        while (remaining.StartsWith("<think>", StringComparison.Ordinal))
        {
            var end = remaining.IndexOf("</think>", StringComparison.Ordinal);
            if (end < 0) return null;
            remaining = remaining[(end + "</think>".Length)..].TrimStart();
        }
        return remaining.ToString();
    }

    private static AiTextResult ParseResponses(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return Failure("invalid-response");
        var status = String(root, "status");
        if (status == "incomplete") return Failure("incomplete");
        if (status == "failed") return Failure("invalid-response");
        if (status != "completed" || !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return Failure("invalid-response");
        var text = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (String(item, "type") != "message") continue;
            if (item.TryGetProperty("status", out var messageStatus))
            {
                if (messageStatus.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return Failure("invalid-response");
                if (messageStatus.GetString() != "completed") return Failure("incomplete");
            }
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return Failure("invalid-response");
            foreach (var part in content.EnumerateArray())
            {
                var type = String(part, "type");
                if (type == "refusal") return Failure("refused");
                if (type != "output_text") continue;
                var value = String(part, "text");
                if (value == null) return Failure("invalid-response");
                if (text.Length + value.Length > MaximumText) return Failure("too-large");
                text.Append(value);
            }
        }
        return Result(text.ToString());
    }

    private static AiTextResult ParseChat(JsonElement root, bool minimax)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return Failure("invalid-response");
        var choice = choices[0];
        var reason = String(choice, "finish_reason");
        if (reason == "content_filter") return Failure("refused");
        if (reason == "length") return Failure("incomplete");
        if (reason != "stop") return Failure("invalid-response");
        if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return Failure("invalid-response");
        if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null)
        {
            if (refusal.ValueKind != JsonValueKind.String) return Failure("invalid-response");
            if (!string.IsNullOrEmpty(refusal.GetString())) return Failure("refused");
        }
        if (message.TryGetProperty("tool_calls", out var tools) && tools.ValueKind != JsonValueKind.Null) return Failure("invalid-response");
        var text = String(message, "content");
        if (text == null) return Failure("invalid-response");
        if (text.Length > MaximumText) return Failure("too-large");
        if (minimax) text = FinalMiniMaxText(text);
        return text == null ? Failure("invalid-response") : Result(text);
    }
    private static string? String(JsonElement node, string property) => node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static AiTextResult Result(string text) => text.Length > MaximumText ? Failure("too-large")
        : string.IsNullOrWhiteSpace(text) ? Failure("invalid-response") : new(true, text);
    private static AiTextResult Failure(string status) => new(false, Status: status);
}
