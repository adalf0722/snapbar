using System.Runtime.InteropServices;
using System.Windows;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class SelectionController
{
    private ActionSettings actions = ActionCatalog.Normalize(new());
    private readonly Func<CustomActionDefinition, string, CancellationToken, Task<ScriptExecutionResult>> runCustom;
    private CancellationTokenSource? customAction;
    private ActionResultWindow? resultWindow;
    public event Action<ActionSettings>? ActionsChanged;
    public event Action? ManageActionsRequested;
    public void ConfigureActions(ActionSettings value) { actions = ActionCatalog.Normalize(value); Invalidate(); }

    private void RememberBuiltIn(TextAction action)
    { var id = ActionCatalog.GetItems(actions).First(a => a.BuiltIn == action).Id; RememberAction(id); }
    private void RememberAction(string id)
    { actions = ActionCatalog.Remember(actions, id); ActionsChanged?.Invoke(actions); }
    private bool Current(SelectionSnapshot current, long ticket) => !disposed && !stopping && generation.IsCurrent(ticket)
        && selection?.Id == current.Id && Native.GetIdentity(Native.GetForegroundWindow()) == current.Target;

    private async Task<ProbeResponse> ValidateCustomAsync(SelectionSnapshot current, long ticket, bool writes)
    {
        if (!Current(current, ticket)) return new("target-changed");
        var compatibility = current.Source == "sublime-clipboard";
        if (compatibility && !SublimeClipboardEnabled) return new("sublime-disabled");
        var validation = compatibility
            ? await clipboardReader.CaptureAsync(current.Target, () => Current(current, ticket) && SublimeClipboardEnabled,
                SublimeSelectionAdapter.ReadPlan(requireEditable: writes))
            : await probe.RequestAsync(new("validate", SessionId: current.Id));
        if (!Current(current, ticket)) return new("target-changed");
        if (!validation.Success) return validation;
        if (validation.Selection?.Text != current.Text) return new("selection-changed");
        if (writes && !SessionPolicy.CanWrite(validation.Selection, current.Target)) return new("not-editable");
        return validation;
    }

    internal async Task ExecuteCustomAsync(string id)
    {
        if (executing || selection == null || disposed || stopping) return;
        var action = actions.CustomActions.FirstOrDefault(a => a.Id == id);
        if (action == null || actions.Layout.Any(e => e.Id == id && e.Placement == ActionPlacement.Hidden)) { Report("invalid-action"); return; }
        if (CustomActionPolicy.Validate(action) != null) { Report("invalid-action"); return; }
        if (!popup.TryRestoreSourceFocus()) { Invalidate(); Report("target-changed"); return; }
        executing = true; var current = selection; var ticket = generation.Next(); popup.Dismiss();
        using var cancellation = new CancellationTokenSource(); customAction = cancellation; var keepResult = false;
        try
        {
            var writes = action.Kind != CustomActionKind.Url && action.Output == ActionOutput.ReplaceSelection;
            var initial = await ValidateCustomAsync(current, ticket, writes);
            if (!initial.Success) { if (generation.IsCurrent(ticket)) Report(initial.Status); return; }
            if (action.Kind == CustomActionKind.Url)
            {
                var destination = CustomActionPolicy.ResolveUrl(current.Text, action.Content);
                if (!destination.Success) { Report(destination.Status); return; }
                if (!Current(current, ticket)) return;
                try { launchWeb(destination.Uri!); RememberAction(id); Status?.Invoke("已交由預設瀏覽器開啟自訂網址。"); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or ArgumentException) { Report("browser-unavailable"); }
                return;
            }
            Status?.Invoke("正在執行自訂動作…");
            var result = await runCustom(action, action.UseSelection ? current.Text : "", cancellation.Token);
            if (!Current(current, ticket) || cancellation.IsCancellationRequested) return;
            var diagnostic = ScriptDiagnostic.Create(action, result);
            if (diagnostic.IsFailure)
            {
                keepResult = true; ShowDiagnostic(action, result);
                return;
            }
            var final = await ValidateCustomAsync(current, ticket, writes);
            if (!final.Success) { if (generation.IsCurrent(ticket)) Report(final.Status); return; }
            RememberAction(id);
            switch (action.Output)
            {
                case ActionOutput.None: Status?.Invoke("自訂動作已完成。"); break;
                case ActionOutput.Clipboard:
                    if (!Current(current, ticket)) return;
                    Clipboard.SetText(result.Output, TextDataFormat.UnicodeText); Status?.Invoke("已複製自訂動作的結果。"); break;
                case ActionOutput.ReplaceSelection:
                    if (!TryPasteResult(current, ticket, result.Output)) ShowResult(action, result.Output, current, ticket, false);
                    break;
                default:
                    keepResult = action.Output != ActionOutput.Hud || !string.IsNullOrWhiteSpace(result.Error);
                    ShowResult(action, result.Output, current, ticket, SessionPolicy.CanWrite(final.Selection, current.Target),
                        string.IsNullOrWhiteSpace(result.Error) ? null : diagnostic); break;
            }
            if (!string.IsNullOrWhiteSpace(result.Error) && action.Output is ActionOutput.None or ActionOutput.Clipboard or ActionOutput.ReplaceSelection)
            { keepResult = true; ShowDiagnostic(action, result); }
        }
        catch (OperationCanceledException) { }
        catch (ExternalException) { Report("clipboard-busy"); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or ArgumentException)
        { Status?.Invoke("自訂動作未完成，請檢查設定或執行環境。"); }
        finally { customAction = null; executing = false; if (!keepResult) Invalidate(); }
    }

    private void ShowDiagnostic(CustomActionDefinition action, ScriptExecutionResult result)
    {
        var diagnostic = ScriptDiagnostic.Create(action, result);
        resultWindow?.Close();
        resultWindow = new ActionResultWindow(action.Name, "", false, false, diagnostic: diagnostic, diagnosticOnly: true);
        resultWindow.Show(); Status?.Invoke(diagnostic.Summary);
    }

    private void ShowResult(CustomActionDefinition action, string text, SelectionSnapshot current, long ticket, bool editable,
        ScriptDiagnostic? diagnostic = null)
    {
        resultWindow?.Close();
        var window = new ActionResultWindow(action.Name, text, action.Output == ActionOutput.Markdown, editable,
            () => _ = ApplyResultAsync(current, ticket, text), action.Output == ActionOutput.Hud, diagnostic);
        resultWindow = window; window.Closed += (_, _) => { if (resultWindow == window) resultWindow = null; };
        window.Show(); Status?.Invoke("結果已顯示；只有原選取仍有效時才能套用。");
    }

    private async Task ApplyResultAsync(SelectionSnapshot current, long ticket, string text)
    {
        if (executing || !Current(current, ticket)) { resultWindow?.InvalidateTarget(); return; }
        executing = true;
        try
        {
            var validation = await ValidateCustomAsync(current, ticket, true);
            if (!validation.Success) { Report(validation.Status); return; }
            TryPasteResult(current, ticket, text);
        }
        catch (ExternalException) { Report("clipboard-busy"); }
        finally { executing = false; Invalidate(); }
    }

    private bool TryPasteResult(SelectionSnapshot current, long ticket, string text)
    {
        if (!Current(current, ticket) || !SessionPolicy.CanWrite(current, current.Target)) return false;
        if (Native.AnyModifierDown()) { Report("modifier-held"); return false; }
        if (text.Length == 0) { Status?.Invoke("結果為空，已保留原文；可在結果視窗查看。"); return false; }
        Clipboard.SetText(text, TextDataFormat.UnicodeText);
        var sequence = Native.GetClipboardSequenceNumber();
        if (!Clipboard.ContainsText() || Clipboard.GetText(TextDataFormat.UnicodeText) != text
            || Native.GetClipboardSequenceNumber() != sequence) { Report("clipboard-changed"); return false; }
        if (!Current(current, ticket) || Native.AnyModifierDown()
            || Native.GetClipboardSequenceNumber() != sequence) { Report("target-changed"); return false; }
        var sent = current.Source == "sublime-clipboard" ? SublimeSelectionAdapter.SendAction(TextAction.Paste, current.Target) : Native.SendControlKey(0x56);
        if (sent) Status?.Invoke("結果已複製並送出貼上指令（請確認原 App 結果）。"); else Report("input-blocked");
        return sent;
    }
}
