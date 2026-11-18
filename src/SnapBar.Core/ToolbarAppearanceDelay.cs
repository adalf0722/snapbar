namespace SnapBar.Core;

public static class ToolbarAppearanceDelay
{
    public const int DefaultMilliseconds = 120;
    public const int MaximumMilliseconds = 2000;

    public static int Normalize(int milliseconds) => Math.Clamp(milliseconds, 0, MaximumMilliseconds);

    public static TimeSpan Remaining(int milliseconds, TimeSpan elapsed) =>
        TimeSpan.FromMilliseconds(Math.Max(0, Normalize(milliseconds) - Math.Max(0, elapsed.TotalMilliseconds)));
}
