namespace SnapBar.Core;

public sealed record AiModelSuggestion(string Id, string Name);
public sealed record AiProviderDefinition(AiProviderProfile Profile, AiModelSuggestion[] Models, string? KeyUrl, string? ModelsUrl, string VerifiedOn);

public static class AiProviderCatalog
{
    // Documentation verification only; suggestions do not imply account access or live API testing.
    private const string VerifiedOn = "2026-11-03";
    private static readonly AiProviderDefinition[] defaults =
    [
        new(new("openai", "OpenAI", AiProtocol.Responses, "https://api.openai.com/v1/responses", ""),
            [new("gpt-5.4-mini", "GPT-5.4 Mini")], "https://platform.openai.com/api-keys", "https://developers.openai.com/api/docs/models/gpt-5.4-mini", VerifiedOn),
        new(new("gemini", "Gemini", AiProtocol.ChatCompletions, "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", ""),
            [new("gemini-3.8-flash", "Gemini 3.8 Flash")], "https://aistudio.google.com/apikey", "https://aistudio.google.com/docs/models", VerifiedOn),
        new(new("claude", "Claude", AiProtocol.Messages, "https://api.anthropic.com/v1/messages", ""),
            [new("claude-sonnet-5-5", "Claude Sonnet 5.5")], "https://platform.claude.com/settings/keys", "https://platform.claude.com/docs/en/models/overview", VerifiedOn),
        new(new("deepseek", "DeepSeek", AiProtocol.ChatCompletions, "https://api.deepseek.com/chat/completions", ""),
            [new("deepseek-flash", "DeepSeek Flash")], "https://platform.deepseek.com/api_keys", "https://api-docs.deepseek.com/", VerifiedOn),
        new(new("groq", "Groq", AiProtocol.ChatCompletions, "https://api.groq.com/openai/v1/chat/completions", ""),
            [new("llama-3.3-70b-versatile", "Llama 3.3 70B")], "https://console.groq.com/keys", "https://console.groq.com/docs/models", VerifiedOn),
        new(new("openrouter", "OpenRouter", AiProtocol.ChatCompletions, "https://openrouter.ai/api/v1/chat/completions", ""),
            [new("openai/gpt-5.4-mini", "OpenAI GPT-5.4 Mini")], "https://openrouter.ai/settings/keys", "https://openrouter.ai/openai/gpt-5.4-mini", VerifiedOn),
        new(new("xai", "xAI", AiProtocol.ChatCompletions, "https://api.x.ai/v1/chat/completions", ""),
            [new("grok-4.7", "Grok 4.7")], "https://console.x.ai/", "https://docs.x.ai/developers/models", VerifiedOn),
        new(new("minimax", "MiniMax", AiProtocol.ChatCompletions, "https://api.minimax.io/v1/chat/completions", ""),
            [new("MiniMax-M3.1-Flash", "MiniMax M3.1 Flash")], "https://platform.minimax.io/console/keys", "https://platform.minimax.io/docs/guides/models-intro", VerifiedOn),
        new(new("compatible", "自訂相容服務", AiProtocol.ChatCompletions, "http://localhost:11434/v1/chat/completions", ""),
            [], null, null, VerifiedOn)
    ];

    private static AiProviderDefinition Copy(AiProviderDefinition definition) => definition with
    {
        Profile = definition.Profile with { },
        Models = definition.Models.Select(model => model with { }).ToArray()
    };

    public static IReadOnlyList<AiProviderDefinition> Definitions => Array.AsReadOnly(defaults.Select(Copy).ToArray());
    public static AiProviderDefinition? Find(string id) => defaults.FirstOrDefault(d => d.Profile.Id == id) is { } definition ? Copy(definition) : null;
    public static AiProviderProfile[] CreateProfiles() => defaults.Select(d => d.Profile with { }).ToArray();

    public static AiSettings MergeMissing(AiSettings settings)
    {
        var valid = AiPolicy.Normalize(settings);
        var known = valid.Profiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var missing = CreateProfiles().Where(p => !known.Contains(p.Id));
        return AiPolicy.Normalize(valid with { Profiles = [.. valid.Profiles, .. missing] });
    }
}
