using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed record ScriptDiagnostic(string Summary, string Details, bool IsFailure)
{
    public static ScriptDiagnostic Create(CustomActionDefinition action, ScriptExecutionResult result)
    {
        var readableError = action.Kind == CustomActionKind.PowerShell ? ReadPowerShellError(result.Error) : result.Error;
        var isFailure = !result.Success;
        var summary = StatusText(result.Status);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Error))
            summary = "程序已結束，另有診斷訊息。請展開詳細資訊查看。";
        if (action.Kind == CustomActionKind.PowerShell && (result.Status is "ok" or "script-failed")
            && (readableError.Contains("NonInteractive", StringComparison.OrdinalIgnoreCase) || readableError.Contains("非互動") || readableError.Contains("非交互"))
            && (readableError.Contains("Prompt", StringComparison.OrdinalIgnoreCase) || readableError.Contains("提示")
                || readableError.Contains("Read-Host", StringComparison.OrdinalIgnoreCase)))
        {
            isFailure = true;
            summary = "這個指令需要互動輸入，SnapBar 無法代答。請改用不需要確認或輸入的寫法。";
            if (Regex.IsMatch(action.Content, @"(?im)^\s*curl(?:\s|$)", RegexOptions.CultureInvariant)
                && Regex.IsMatch(readableError, @"(?im)^\s*(?:curl|Invoke-WebRequest)\s*:", RegexOptions.CultureInvariant))
                summary = "這個指令需要互動確認，SnapBar 無法代答。Windows PowerShell 的 curl 是別名；請改用 curl.exe。";
        }

        var details = new StringBuilder();
        details.AppendLine($"動作：{action.Name}");
        details.AppendLine($"執行環境：{action.Kind}");
        details.AppendLine($"狀態：{result.Status}");
        details.AppendLine($"結束代碼：{(result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : "未取得")}");
        if (result.Status == "script-timeout") details.AppendLine("程序已逾時；以下保留中止前擷取的內容。");
        if (result.Status == "script-output-too-large") details.AppendLine("輸出超過每個串流 262,144 字元的上限，程序已停止。");
        if (result.OutputTruncated) details.AppendLine("標準輸出已截斷，以下不是完整輸出。");
        if (result.ErrorTruncated) details.AppendLine("標準錯誤已截斷，以下不是完整錯誤內容。");
        if (result.CaptureIncomplete) details.AppendLine("部分串流未能完整讀取；以下僅包含已擷取的內容。");
        details.AppendLine().AppendLine("標準錯誤（stderr）");
        details.AppendLine(readableError.Length > 0 ? readableError : "（無）");
        details.AppendLine().AppendLine("標準輸出（stdout）");
        details.Append(result.Output.Length > 0 ? result.Output : "（無）");
        if (readableError != result.Error)
            details.AppendLine().AppendLine().AppendLine("原始標準錯誤（CLIXML）").Append(result.Error);
        return new(summary, details.ToString(), isFailure);
    }

    private static string ReadPowerShellError(string error)
    {
        const string marker = "#< CLIXML";
        if (!error.StartsWith(marker, StringComparison.Ordinal)) return error;
        try
        {
            using var input = new StringReader(error[marker.Length..].TrimStart());
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 512 * 1024 });
            var document = XDocument.Load(reader);
            XNamespace ns = "http://schemas.microsoft.com/powershell/2004/04";
            if (document.Root?.Name != ns + "Objs") return error;
            var messages = document.Root.Elements(ns + "S").Where(e => (string?)e.Attribute("S") == "Error")
                .Select(e => XmlConvert.DecodeName(e.Value)).ToArray();
            return messages.Length > 0 ? string.Concat(messages) : error;
        }
        catch (XmlException) { return error; }
    }

    public static string StatusText(string status) => status switch
    {
        "ok" => "執行已完成。",
        "cancelled" => "已取消執行。",
        "script-timeout" => "執行逾時，已終止程序。請檢查指令是否等待輸入，或調整進階設定中的逾時秒數。",
        "runtime-unavailable" => "找不到或無法啟動執行環境。請在進階設定指定可用的 .exe 完整路徑。",
        "script-output-too-large" => "輸出超過大小限制，已停止。請減少指令輸出；已擷取的內容可在詳細資訊查看。",
        "invalid-action" => "動作設定無效。請檢查名稱、腳本、執行環境與逾時設定。",
        _ => "腳本執行失敗。請展開詳細資訊查看原始錯誤與發生位置。"
    };
}
