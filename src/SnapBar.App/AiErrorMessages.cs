namespace SnapBar.App;

internal static class AiErrorMessages
{
    internal static string Describe(string status) => status switch
    {
        "key-required" => UiText.Get("AiErrorMessages_000"),
        "authentication" => UiText.Get("AiErrorMessages_001"),
        "model-unavailable" => UiText.Get("AiErrorMessages_002"),
        "settings-required" => UiText.Get("AiErrorMessages_003"),
        "rate-limit" => UiText.Get("AiErrorMessages_004"),
        "timeout" => UiText.Get("AiErrorMessages_005"),
        "network" => UiText.Get("AiErrorMessages_006"),
        "too-large" => UiText.Get("AiErrorMessages_007"),
        "incomplete" => UiText.Get("AiErrorMessages_008"),
        "refused" => UiText.Get("AiErrorMessages_009"),
        _ => UiText.Get("AiErrorMessages_010")
    };
}
