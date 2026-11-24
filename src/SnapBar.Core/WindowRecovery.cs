namespace SnapBar.Core;

public static class WindowRecovery
{
    // All rectangles use physical screen pixels; only the grab width is specified in DIP.
    public static ScreenRect Recover(ScreenRect window, ScreenRect caption,
        IReadOnlyList<ScreenRect> workAreas, double dpiScale)
    {
        if (!Valid(window) || !Valid(caption) || !double.IsFinite(dpiScale) || dpiScale <= 0) return window;
        var bestDistance = double.PositiveInfinity;
        var bestX = 0d;
        var bestY = 0d;
        foreach (var work in workAreas)
        {
            if (!Valid(work)) continue;
            var grabWidth = Math.Min(96 * dpiScale, Math.Min(caption.Width, work.Width));
            var grabHeight = Math.Min(caption.Height, work.Height);
            var x = Math.Clamp(caption.X, work.X + grabWidth - caption.Width, work.Right - grabWidth);
            var y = Math.Clamp(caption.Y, work.Y, work.Bottom - grabHeight);
            var dx = x - caption.X;
            var dy = y - caption.Y;
            var distance = dx * dx + dy * dy;
            if (distance == 0) return window;
            if (distance >= bestDistance) continue;
            bestDistance = distance; bestX = dx; bestY = dy;
        }
        return window with { X = window.X + bestX, Y = window.Y + bestY };
    }

    private static bool Valid(ScreenRect rect) => double.IsFinite(rect.X) && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom)
        && rect.Width > 0 && rect.Height > 0;
}
