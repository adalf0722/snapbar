using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class SelectionController
{
    private bool clipboardCompatibilityEnabled;
    public bool ClipboardCompatibilityEnabled
    {
        get => clipboardCompatibilityEnabled;
        set { clipboardCompatibilityEnabled = value; Invalidate(); }
    }

    private async Task<ProbeResponse> CaptureClipboardAsync(ClipboardCaptureTarget target, long ticket,
        uint? expectedSequence = null)
    {
        bool Current() => ClipboardCompatibilityEnabled && generation.IsCurrent(ticket) && !disposed && !stopping
            && Native.GetIdentity(Native.GetForegroundWindow()) == target.Target;
        if (!Current()) return new("target-changed");
        var guard = await probe.RequestAsync(new("clipboard-validate", SessionId: target.Id, Diagnostics: selectionDiagnostics.Enabled));
        selectionDiagnostics.Record(ticket, "fallback-before", guard);
        if (!guard.Success || !Current()) return guard.Success ? new("target-changed") : guard;
        var writeBinding = guard.ClipboardTarget;
        var plan = ClipboardSelectionAdapter.ReadPlan(target.Target) with
        {
            ValidateSource = async () =>
            {
                var validation = await probe.RequestAsync(new("clipboard-validate", SessionId: target.Id, Diagnostics: selectionDiagnostics.Enabled));
                selectionDiagnostics.Record(ticket, "fallback-before-copy", validation);
                return validation.Status;
            }
        };
        var result = await clipboardReader.CaptureAsync(target.Target, Current, plan, expectedSequence,
            selectionDiagnostics.Enabled ? stage => selectionDiagnostics.Record(ticket, stage, new("ok")) : null);
        selectionDiagnostics.Record(ticket, "fallback-copy", result);
        if (!result.Success || result.Selection == null || !Current()) return result.Success ? new("target-changed") : result;
        guard = await probe.RequestAsync(new("clipboard-validate", SessionId: target.Id, Diagnostics: selectionDiagnostics.Enabled));
        selectionDiagnostics.Record(ticket, "fallback-after", guard);
        if (!guard.Success || !Current()) return guard.Success ? new("target-changed") : guard;
        if (guard.ClipboardTarget != writeBinding || writeBinding?.Id != target.Id || writeBinding.Target != target.Target)
            return new("target-changed");
        var writeToken = writeBinding.WriteToken == target.Id ? writeBinding.WriteToken : null;
        return result with { Selection = result.Selection with { Id = target.Id, Editable = writeToken != null ? true : null,
            WriteToken = writeToken, WriteCaret = writeToken != null ? writeBinding.WriteCaret : null } };
    }

    private static bool WriteSourceCurrent(SelectionSnapshot current) => current.Source != ClipboardSelectionAdapter.Source
        || SessionPolicy.CanWrite(current, current.Target)
        && (current.WriteCaret == null || NativeCaretReader.ReadIdentity(current.Target) == current.WriteCaret);
}
