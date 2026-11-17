using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class SelectionController
{
    private const int AutoCopyModifierWaitMilliseconds = 2000;
    private AutoCopySettings autoCopy = new();
    private TargetIdentity? autoCopyTarget;
    private CopyFeedbackWindow? copyFeedback;

    internal void ConfigureAutoCopy(AutoCopySettings settings)
    {
        autoCopy = AutoCopySettings.Normalize(settings);
        Invalidate();
    }

    private bool WriteSelectionClipboard(SelectionSnapshot current, long ticket, uint sequence) =>
        SelectionClipboardWriter.TryWrite(monitor.OwnerWindow, current.Text, sequence,
            () => generation.IsCurrent(ticket) && !disposed && !stopping
                && Native.GetIdentity(Native.GetForegroundWindow()) == current.Target);

    private async Task<bool> CopySelectionAutomaticallyAsync(SelectionSnapshot current, long ticket,
        uint expectedSequence)
    {
        bool Current() => autoCopy.Enabled && generation.IsCurrent(ticket) && !disposed && !stopping
            && Native.GetIdentity(Native.GetForegroundWindow()) == current.Target;
        if (!Current() || current.Text.Length == 0 || Native.GetClipboardSequenceNumber() != expectedSequence) return false;
        ProbeResponse validation;
        if (current.Source == "sublime-clipboard")
        {
            if (!SublimeClipboardEnabled) return false;
            validation = await clipboardReader.CaptureAsync(current.Target, Current,
                SublimeSelectionAdapter.ReadPlan(), expectedSequence,
                stage => selectionDiagnostics.Record(ticket, "sublime-validate-" + stage, new("ok")));
        }
        else if (current.Source == ClipboardSelectionAdapter.Source)
        {
            if (!ClipboardCompatibilityEnabled) return false;
            validation = await CaptureClipboardAsync(new(current.Id, current.Target), ticket, expectedSequence);
        }
        else validation = await probe.RequestAsync(new("validate", SessionId: current.Id));
        selectionDiagnostics.Record(ticket, "auto-copy-validation", validation);
        if (!Current() || !validation.Success || validation.Selection is not { } verified
            || verified.Target != current.Target || verified.Text != current.Text) return false;
        var copied = WriteSelectionClipboard(current, ticket, validation.ClipboardSequence ?? expectedSequence);
        selectionDiagnostics.Record(ticket, "auto-copy-write", new(copied ? "ok" : "rejected"));
        return copied;
    }

    private void ShowAutoCopyFeedback(SelectionSnapshot current, int x, int y)
    {
        if (!autoCopy.ShowFeedback) return;
        copyFeedback ??= new();
        ScreenRect? toolbar = null;
        if (popup.IsVisible && Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(popup).Handle, out var rect))
            toolbar = new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        copyFeedback.Present(x, y, current.Bounds, toolbar);
    }
}
