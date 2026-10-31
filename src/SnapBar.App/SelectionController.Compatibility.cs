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
        var guard = await probe.RequestAsync(new("clipboard-validate", SessionId: target.Id));
        if (!guard.Success || !Current()) return guard.Success ? new("target-changed") : guard;
        var plan = ClipboardSelectionAdapter.ReadPlan(target.Target) with
        { ValidateSource = async () => (await probe.RequestAsync(new("clipboard-validate", SessionId: target.Id))).Status };
        var result = await clipboardReader.CaptureAsync(target.Target, Current, plan, expectedSequence);
        if (!result.Success || result.Selection == null || !Current()) return result.Success ? new("target-changed") : result;
        guard = await probe.RequestAsync(new("clipboard-validate", SessionId: target.Id));
        if (!guard.Success || !Current()) return guard.Success ? new("target-changed") : guard;
        return result with { Selection = result.Selection with { Id = target.Id } };
    }
}
