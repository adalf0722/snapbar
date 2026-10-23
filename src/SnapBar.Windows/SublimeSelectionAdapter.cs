using SnapBar.Core;

namespace SnapBar.Windows;

public static class SublimeSelectionAdapter
{
    public const string Source = "sublime-clipboard";

    public static ClipboardReadPlan ReadPlan(bool requireEditable = false) => new(Source,
        SublimeCompatibility.IsTarget, SublimeCompatibility.Ready,
        requireEditable
            ? [new(Native.SendSublimeEditableCopy, true)]
            : [new(Native.SendSublimeEditableCopy, true), new(Native.SendSublimeCopy, null)],
        requireEditable ? "not-editable" : "sublime-no-selection");

    public static bool SendAction(TextAction action, TargetIdentity target) =>
        SublimeCompatibility.IsTarget(target) && SublimeCompatibility.Ready()
        && Native.GetIdentity(Native.GetForegroundWindow()) == target
        && action is TextAction.Cut or TextAction.Paste && Native.SendSublimeAction(action);
}
