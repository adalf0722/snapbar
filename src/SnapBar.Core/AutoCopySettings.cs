namespace SnapBar.Core;

[Flags]
public enum SelectionModifiers { None = 0, Control = 1, Shift = 2, Alt = 4, Windows = 8 }

public enum AutoCopyMode { RequireControl, OnSelection }

public sealed record AutoCopySettings(bool Enabled = false, AutoCopyMode Mode = AutoCopyMode.RequireControl,
    bool ShowFeedback = true)
{
    public static AutoCopySettings Normalize(AutoCopySettings? value) => value == null ? new()
        : Enum.IsDefined(value.Mode) ? value : value with { Mode = AutoCopyMode.RequireControl };

    public bool ShouldCopy(SelectionModifiers modifiers) => Enabled
        && (modifiers & ~SelectionModifiers.Control) == 0
        && (Mode == AutoCopyMode.RequireControl ? modifiers == SelectionModifiers.Control
            : Mode == AutoCopyMode.OnSelection && modifiers == SelectionModifiers.None);
}
