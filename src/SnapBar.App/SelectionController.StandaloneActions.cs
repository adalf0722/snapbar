using System.Runtime.InteropServices;
using System.Windows;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class SelectionController
{
    internal async Task ExecuteStandaloneCustomAsync(string id)
    {
        if (executing || disposed || stopping) return;
        var action = actions.CustomActions.FirstOrDefault(a => a.Id == id);
        if (action is not { UseSelection: false, Kind: not CustomActionKind.Url, Output: not ActionOutput.ReplaceSelection }
            || CustomActionPolicy.Validate(action) != null
            || actions.Layout.Any(e => e.Id == id && !e.IsVisible))
        { Report("invalid-action"); return; }
        Invalidate();
        var ticket = generation.Next();
        var target = Native.GetIdentity(Native.GetForegroundWindow());
        executing = true;
        using var cancellation = new CancellationTokenSource();
        customAction = cancellation;
        var readonlyResult = action.Output is ActionOutput.Window or ActionOutput.Markdown;
        customActionAllowsDetachedResult = readonlyResult;
        bool Current() => !disposed && !stopping && !cancellation.IsCancellationRequested
            && ReferenceEquals(customAction, cancellation)
            && actions.CustomActions.FirstOrDefault(a => a.Id == id) == action
            && !actions.Layout.Any(e => e.Id == id && !e.IsVisible)
            && (readonlyResult || generation.IsCurrent(ticket) && target != null
                && Native.GetIdentity(Native.GetForegroundWindow()) == target);
        try
        {
            if (action.Kind == CustomActionKind.Launch)
            {
                if (Current()) await ExecuteLaunchAsync(action, "", cancellation.Token);
                return;
            }
            Status?.Invoke(UiText.Get("SelectionController_CustomActions_001"));
            var result = await runCustom(action, "", cancellation.Token);
            if (!Current() || result.Status == "cancelled") return;
            var diagnostic = ScriptDiagnostic.Create(action, result);
            if (!diagnostic.IsFailure) RememberAction(id);
            ActionOutputRouter.Route(action.Output, result.Output, diagnostic.IsFailure, !string.IsNullOrWhiteSpace(result.Error),
                new(Current, () => Status?.Invoke(UiText.Get("SelectionController_CustomActions_002")),
                    text => Clipboard.SetText(text, TextDataFormat.UnicodeText), _ => false,
                    (text, allowApply) => customResults.Show(new ActionResultWindow(UiNames.Action(action), text,
                        action.Output == ActionOutput.Markdown, false, hud: action.Output == ActionOutput.Hud,
                        diagnostic: allowApply && !string.IsNullOrWhiteSpace(result.Error) ? diagnostic : null,
                        persistent: action.Output != ActionOutput.Hud)),
                    () => ShowDiagnostic(action, result)));
        }
        catch (OperationCanceledException) { }
        catch (ExternalException) { Report("clipboard-busy"); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or ArgumentException)
        { Status?.Invoke(UiText.Get("SelectionController_CustomActions_004")); }
        finally { customAction = null; customActionAllowsDetachedResult = false; executing = false; }
    }
}
