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
        var genericCompatibility = current.Source == ClipboardSelectionAdapter.Source;
        if (compatibility && !SublimeClipboardEnabled) return new("sublime-disabled");
        if (genericCompatibility && !ClipboardCompatibilityEnabled) return new("compatibility-disabled");
        if (genericCompatibility && writes) return new("not-editable");
        var validation = compatibility
            ? await clipboardReader.CaptureAsync(current.Target, () => Current(current, ticket) && SublimeClipboardEnabled,
                SublimeSelectionAdapter.ReadPlan(requireEditable: writes))
            : genericCompatibility ? await CaptureClipboardAsync(new(current.Id, current.Target), ticket)
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
        using var cancellation = new CancellationTokenSource(); customAction = cancellation; var keepResult = false; var clipboardLease = false;
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
                try { launchWeb(destination.Uri!); RememberAction(id); Status?.Invoke(UiText.Get("SelectionController_CustomActions_000")); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException or ArgumentException) { Report("browser-unavailable"); }
                return;
            }
            if (current.Source == ClipboardSelectionAdapter.Source)
            {
                var lease = await probe.RequestAsync(new("clipboard-extend", SessionId: current.Id, LeaseSeconds: action.TimeoutSeconds + 10));
                clipboardLease = lease.Success;
                if (!Current(current, ticket)) return;
                if (!lease.Success) { Report(lease.Status); return; }
            }
            Status?.Invoke(UiText.Get("SelectionController_CustomActions_001"));
            var result = await runCustom(action, action.UseSelection ? current.Text : "", cancellation.Token);
            if (!Current(current, ticket) || cancellation.IsCancellationRequested) return;
            var diagnostic = ScriptDiagnostic.Create(action, result);
            SelectionSnapshot? validatedSelection = current;
            if (diagnostic.IsFailure)
            {
                keepResult = ActionOutputRouter.Route(action.Output, result.Output, true, false, CreateOutputHost());
                return;
            }
            var final = await ValidateCustomAsync(current, ticket, writes);
            if (!final.Success) { if (generation.IsCurrent(ticket)) Report(final.Status); return; }
            validatedSelection = final.Selection;
            RememberAction(id);
            keepResult = ActionOutputRouter.Route(action.Output, result.Output, false, !string.IsNullOrWhiteSpace(result.Error), CreateOutputHost());

            ActionOutputHost CreateOutputHost() => new(
                () => Current(current, ticket) && !cancellation.IsCancellationRequested,
                () => Status?.Invoke(UiText.Get("SelectionController_CustomActions_002")),
                text => { Clipboard.SetText(text, TextDataFormat.UnicodeText); Status?.Invoke(UiText.Get("SelectionController_CustomActions_003")); },
                text => TryPasteResult(current, ticket, text),
                (text, allowApply) => ShowResult(action, text, current, ticket,
                    allowApply && SessionPolicy.CanWrite(validatedSelection, current.Target),
                    allowApply && !string.IsNullOrWhiteSpace(result.Error) ? diagnostic : null),
                () => ShowDiagnostic(action, result));
        }
        catch (OperationCanceledException) { }
        catch (ExternalException) { Report("clipboard-busy"); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or ArgumentException)
        { Status?.Invoke(UiText.Get("SelectionController_CustomActions_004")); }
        finally
        {
            if (clipboardLease) await probe.RequestAsync(new("clipboard-release", SessionId: current.Id));
            customAction = null; executing = false; if (!keepResult) Invalidate();
        }
    }

    private void ShowDiagnostic(CustomActionDefinition action, ScriptExecutionResult result)
    {
        var diagnostic = ScriptDiagnostic.Create(action, result);
        resultWindow?.Close();
        resultWindow = new ActionResultWindow(UiNames.Action(action), "", false, false, diagnostic: diagnostic, diagnosticOnly: true);
        resultWindow.Show(); Status?.Invoke(diagnostic.Summary);
    }

    private void ShowResult(CustomActionDefinition action, string text, SelectionSnapshot current, long ticket, bool editable,
        ScriptDiagnostic? diagnostic = null)
    {
        resultWindow?.Close();
        var window = new ActionResultWindow(UiNames.Action(action), text, action.Output == ActionOutput.Markdown, editable,
            () => _ = ApplyResultAsync(current, ticket, text), action.Output == ActionOutput.Hud, diagnostic);
        resultWindow = window; window.Closed += (_, _) => { if (resultWindow == window) resultWindow = null; };
        window.Show(); Status?.Invoke(UiText.Get("SelectionController_CustomActions_005"));
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
        if (text.Length == 0) { Status?.Invoke(UiText.Get("SelectionController_CustomActions_006")); return false; }
        Clipboard.SetText(text, TextDataFormat.UnicodeText);
        var sequence = Native.GetClipboardSequenceNumber();
        if (!Clipboard.ContainsText() || Clipboard.GetText(TextDataFormat.UnicodeText) != text
            || Native.GetClipboardSequenceNumber() != sequence) { Report("clipboard-changed"); return false; }
        if (!Current(current, ticket) || Native.AnyModifierDown()
            || Native.GetClipboardSequenceNumber() != sequence) { Report("target-changed"); return false; }
        var sent = current.Source == "sublime-clipboard" ? SublimeSelectionAdapter.SendAction(TextAction.Paste, current.Target) : Native.SendControlKey(0x56);
        if (sent) Status?.Invoke(UiText.Get("SelectionController_CustomActions_007")); else Report("input-blocked");
        return sent;
    }
}
