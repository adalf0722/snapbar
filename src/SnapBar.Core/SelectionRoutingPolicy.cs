namespace SnapBar.Core;

public static class SelectionRoutingPolicy
{
    // Failures, empty selections and sensitive fields must never become permission to copy.
    public static bool UseClipboardFallback(ProbeResponse native, bool enabled, bool verifiedTarget) =>
        native.Status == "unsupported" && enabled && verifiedTarget;
}

public static class ClipboardActionPolicy
{
    public static bool IsUnchanged(uint expected, uint current) => expected == current;
}
