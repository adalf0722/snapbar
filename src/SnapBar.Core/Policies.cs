namespace SnapBar.Core;

public static class SessionPolicy
{
    public static bool CanWrite(SelectionSnapshot? selection, TargetIdentity current) =>
        selection is { Editable: true, RangeCount: 1, Text.Length: > 0 } && selection.Source != "clipboard" && selection.Target == current;

    public static bool? ResolveEditability(bool? readOnly, bool enabled, bool isFocusedProvider, bool valueReadOnly)
    {
        if (!enabled || readOnly == true || valueReadOnly) return false;
        return readOnly == false && isFocusedProvider ? true : null;
    }
}

public sealed class GenerationGate
{
    private long generation;
    public long Next() => Interlocked.Increment(ref generation);
    public bool IsCurrent(long value) => value == Interlocked.Read(ref generation);
}

public static class PopupPlacement
{
    public static ScreenRect Place(ScreenRect anchor, ScreenRect workArea, double width, double height)
    {
        width = Math.Clamp(width, 0, workArea.Width);
        height = Math.Clamp(height, 0, workArea.Height);
        var x = Math.Clamp(anchor.X, workArea.X, workArea.Right - width);
        var y = anchor.Bottom + 8;
        if (y + height > workArea.Bottom) y = anchor.Y - height - 8;
        y = Math.Clamp(y, workArea.Y, workArea.Bottom - height);
        return new(x, y, width, height);
    }
}
