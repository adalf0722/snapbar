namespace SnapBar.Core;

public static class ClipboardWritePolicy
{
    public static bool CanBindCaret(bool verifiedApplication, bool sourceAccepted, long focus, long point,
        CaretIdentity? caret, int? role, int? state, double clientX, double clientY, double margin,
        double? horizontalMargin = null)
    {
        if (!verifiedApplication || !sourceAccepted || focus == 0 || point != focus || caret?.Window != focus
            || role != 7 || state == null || (state.Value & (0x20000000 | 0x40 | 0x8000 | 0x10000 | 0x1)) != 0)
            return false;
        var bounds = caret.ClientBounds;
        var horizontal = horizontalMargin ?? margin;
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width)
            || !double.IsFinite(bounds.Height) || bounds.Width is <= 0 or > 64 || bounds.Height is <= 0 or > 256
            || !double.IsFinite(margin) || margin is < 0 or > 64
            || !double.IsFinite(horizontal) || horizontal is < 0 or > 64) return false;
        return clientX >= bounds.X - horizontal && clientX <= bounds.Right + horizontal
            && clientY >= bounds.Y - margin && clientY <= bounds.Bottom + margin;
    }
}
