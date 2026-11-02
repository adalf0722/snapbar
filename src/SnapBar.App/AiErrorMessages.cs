namespace SnapBar.App;

internal static class AiErrorMessages
{
    internal static string Describe(string status) => status switch
    {
        "key-required" => "API Key 未設定，請開啟服務設定。",
        "authentication" => "API Key 無效或權限不足，請檢查金鑰與服務權限。",
        "model-unavailable" => "模型不存在或目前金鑰無權限，請確認模型 ID 與官方模型清單。",
        "settings-required" => "請檢查服務網址與模型 ID。",
        "rate-limit" => "服務目前限制請求，請檢查配額或稍後再試。",
        "timeout" => "服務回應逾時。",
        "network" => "無法連上 AI 服務，請檢查連線及網址。",
        "too-large" => "文字或回應超過限制。",
        "incomplete" => "服務回傳的結果不完整。",
        "refused" => "服務未提供可用結果。",
        _ => "服務回應格式無效。"
    };
}
