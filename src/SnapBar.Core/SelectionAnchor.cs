namespace SnapBar.Core;

public static class SelectionAnchor
{
    public static ScreenRect Resolve(IEnumerable<ScreenRect> rectangles, bool usePointer, int x, int y, double scale = 1)
    {
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var proximity = 48 * scale;
        ScreenRect? chosen = null;
        var closest = double.PositiveInfinity;
        foreach (var rect in rectangles)
        {
            if (!IsValid(rect)) continue;
            if (!usePointer) { chosen = rect; continue; }
            // A container-sized range must not pull the toolbar to the bottom of the page.
            if (rect.Height > proximity) continue;
            var dx = Math.Max(rect.X - x, Math.Max(0, x - rect.Right));
            var dy = Math.Max(rect.Y - y, Math.Max(0, y - rect.Bottom));
            var distance = dx * dx + dy * dy;
            if (distance > proximity * proximity || distance >= closest) continue;
            closest = distance;
            chosen = rect;
        }

        if (chosen == null) return new(x, y, 1, 1);
        // Keep the line's vertical clearance, but follow the release point horizontally.
        return usePointer ? new(x, chosen.Y, 1, chosen.Height) : chosen;
    }

    private static bool IsValid(ScreenRect rect) => double.IsFinite(rect.X) && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Width) && double.IsFinite(rect.Height) && double.IsFinite(rect.Right)
        && double.IsFinite(rect.Bottom) && rect.Width > 0 && rect.Height > 0;
}
