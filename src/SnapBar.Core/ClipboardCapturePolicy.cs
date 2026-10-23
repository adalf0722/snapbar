namespace SnapBar.Core;

public static class ClipboardCapturePolicy
{
    // Win32 permits populated clipboards without an owner window. Fresh, stable data
    // and unchanged foreground/input are required for the ownerless fallback.
    public static string Evaluate(bool changed, bool stable, bool hasOwner, int ownerProcess,
        int targetProcess, bool foregroundMatches, bool inputCurrent)
    {
        if (!changed) return "pending";
        if (!stable) return "clipboard-changed";
        if (hasOwner && ownerProcess != targetProcess) return "clipboard-other-owner";
        if (!inputCurrent) return "clipboard-input-changed";
        if (!foregroundMatches) return "target-changed";
        return "ok";
    }
}
