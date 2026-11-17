using SnapBar.Core;

internal static class AutoCopySettingsTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        check("auto-copy defaults off with Ctrl gating and confirmation", () => new AutoCopySettings() is
            { Enabled: false, Mode: AutoCopyMode.RequireControl, ShowFeedback: true });
        foreach (var mode in Enum.GetValues<AutoCopyMode>())
        foreach (var mask in Enumerable.Range(0, 32))
        {
            var modifiers = (SelectionModifiers)mask;
            check($"auto-copy {mode} gates modifier mask {mask}", () =>
            {
                var expected = mode == AutoCopyMode.RequireControl ? modifiers == SelectionModifiers.Control
                    : modifiers == SelectionModifiers.None;
                return new AutoCopySettings(true, mode).ShouldCopy(modifiers) == expected
                    && !new AutoCopySettings(false, mode).ShouldCopy(modifiers);
            });
        }
        check("unknown runtime auto-copy modes reject gestures", () =>
            !new AutoCopySettings(true, (AutoCopyMode)42).ShouldCopy(SelectionModifiers.None)
            && !new AutoCopySettings(true, (AutoCopyMode)42).ShouldCopy(SelectionModifiers.Control));
    }
}
