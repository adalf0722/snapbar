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
        if (result.ProductErrorCode is "Runtime_DefaultMissing" or "Runtime_PathInvalid" or "Runtime_PythonMissing")
            summary = UiText.Get(result.ProductErrorCode, result.ProductErrorArgument);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Error))
            summary = UiText.Get("ScriptDiagnostic_000");
        if (action.Kind == CustomActionKind.Python && result.Status == "script-failed"
            && Regex.IsMatch(readableError, @"(?m)^ModuleNotFoundError: No module named ", RegexOptions.CultureInvariant))
            summary = UiText.Get("ScriptDiagnostic_PythonModuleMissing");
        if (action.Kind == CustomActionKind.PowerShell && (result.Status is "ok" or "script-failed")
            && (readableError.Contains("NonInteractive", StringComparison.OrdinalIgnoreCase) || readableError.Contains("非互動") || readableError.Contains("非交互"))
            && (readableError.Contains("Prompt", StringComparison.OrdinalIgnoreCase) || readableError.Contains("提示")
                || readableError.Contains("Read-Host", StringComparison.OrdinalIgnoreCase)))
        {
            isFailure = true;
            summary = UiText.Get("ScriptDiagnostic_001");
            if (Regex.IsMatch(action.Content, @"(?im)^\s*curl(?:\s|$)", RegexOptions.CultureInvariant)
                && Regex.IsMatch(readableError, @"(?im)^\s*(?:curl|Invoke-WebRequest)\s*:", RegexOptions.CultureInvariant))
                summary = UiText.Get("ScriptDiagnostic_002");
        }

        var details = new StringBuilder();
        details.AppendLine(UiText.Get("ScriptDiagnostic_003",(UiNames.Action(action))));
        details.AppendLine(UiText.Get("ScriptDiagnostic_004",(action.Kind)));
        details.AppendLine(UiText.Get("ScriptDiagnostic_005",(result.Status)));
        details.AppendLine(UiText.Get("ScriptDiagnostic_006",((result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : UiText.Get("ScriptDiagnostic_024")))));
        if (result.Status == "script-timeout") details.AppendLine(UiText.Get("ScriptDiagnostic_007"));
        if (result.Status == "script-output-too-large") details.AppendLine(UiText.Get("ScriptDiagnostic_008"));
        if (result.OutputTruncated) details.AppendLine(UiText.Get("ScriptDiagnostic_009"));
        if (result.ErrorTruncated) details.AppendLine(UiText.Get("ScriptDiagnostic_010"));
        if (result.CaptureIncomplete) details.AppendLine(UiText.Get("ScriptDiagnostic_011"));
        details.AppendLine().AppendLine(UiText.Get("ScriptDiagnostic_012"));
        details.AppendLine(readableError.Length > 0 ? readableError : UiText.Get("ScriptDiagnostic_013"));
        details.AppendLine().AppendLine(UiText.Get("ScriptDiagnostic_014"));
        details.Append(result.Output.Length > 0 ? result.Output : UiText.Get("ScriptDiagnostic_015"));
        if (readableError != result.Error)
            details.AppendLine().AppendLine().AppendLine(UiText.Get("ScriptDiagnostic_016")).Append(result.Error);
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
        "ok" => UiText.Get("ScriptDiagnostic_017"),
        "cancelled" => UiText.Get("ScriptDiagnostic_018"),
        "script-timeout" => UiText.Get("ScriptDiagnostic_019"),
        "runtime-unavailable" => UiText.Get("ScriptDiagnostic_020"),
        "script-output-too-large" => UiText.Get("ScriptDiagnostic_021"),
        "invalid-action" => UiText.Get("ScriptDiagnostic_022"),
        _ => UiText.Get("ScriptDiagnostic_023")
    };
}
