using System.Runtime.InteropServices;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class SelectionController
{
    private AiPreferences? aiPreferences;
    private IAiTextClient? aiClient;
    private AiPanelWindow? aiWindow;
    internal AiPanelWindow? ActiveAiWindow => aiWindow;
    internal void ConfigureAi(AiPreferences preferences, IAiTextClient? client = null)
    { aiPreferences = preferences; aiClient = client; }

    private async Task OpenAiAsync()
    {
        if (executing || selection == null || disposed || stopping) return;
        if (aiPreferences == null) { Status?.Invoke("AI 服務尚未初始化。"); return; }
        if (!popup.TryRestoreSourceFocus()) { Invalidate(); Report("target-changed"); return; }
        executing = true; var current = selection; var ticket = generation.Next(); popup.Dismiss();
        try
        {
            var validation = await ValidateCustomAsync(current, ticket, false);
            if (!validation.Success) { if (generation.IsCurrent(ticket)) Report(validation.Status); return; }
            aiWindow?.Close();
            if (!Current(current, ticket)) return;
            AiPanelWindow? window = null;
            window = new(current, aiPreferences, text => ApplyAiResultAsync(window!, current, ticket, text), aiClient);
            aiWindow = window;
            window.Closed += (_, _) =>
            {
                if (!ReferenceEquals(aiWindow, window)) return;
                aiWindow = null;
                if (selection?.Id == current.Id) Invalidate();
            };
            RememberBuiltIn(TextAction.Ai); window.Show(); window.Activate();
            Status?.Invoke("AI 面板已開啟，選擇指令後才傳送文字。");
        }
        catch (ExternalException) { Report("clipboard-busy"); }
        finally { executing = false; }
    }

    private async Task<bool> ApplyAiResultAsync(AiPanelWindow window, SelectionSnapshot current, long ticket, string text)
    {
        if (executing || disposed || stopping || !window.TargetValid || !window.HasForeground
            || !generation.IsCurrent(ticket) || selection?.Id != current.Id || !SessionPolicy.CanWrite(current, current.Target)) return false;
        executing = true;
        var sent = false;
        try
        {
            // Ctrl+Enter must not inject while Ctrl is still physically held.
            for (var i = 0; Native.AnyModifierDown() && i < 20; i++) await Task.Delay(25);
            if (Native.AnyModifierDown() || !window.HasForeground || !generation.IsCurrent(ticket) || selection?.Id != current.Id
                || Native.GetIdentity((nint)current.Target.Window) != current.Target) return false;
            if (!Native.SetForegroundWindow((nint)current.Target.Window) || !Current(current, ticket)) return false;
            var validation = await ValidateCustomAsync(current, ticket, true);
            if (!validation.Success) { if (generation.IsCurrent(ticket)) Report(validation.Status); return false; }
            sent = TryPasteResult(current, ticket, text); return sent;
        }
        catch (ExternalException) { Report("clipboard-busy"); return false; }
        finally
        {
            executing = false;
            if (!sent) Invalidate();
            // Return a failed operation to its panel only while the original source is still foreground.
            if (!disposed && !stopping && window.IsVisible && Native.GetIdentity(Native.GetForegroundWindow()) == current.Target
                && !sent) window.Activate();
        }
    }
}
