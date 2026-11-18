using System.Diagnostics;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

public sealed partial class SelectionController
{
    private int appearanceDelayMilliseconds = ToolbarAppearanceDelay.DefaultMilliseconds;
    private TargetIdentity? pendingAppearanceTarget;

    private readonly record struct AppearanceDeadline(long Timestamp, int Milliseconds)
    {
        internal TimeSpan Remaining => ToolbarAppearanceDelay.Remaining(Milliseconds, Stopwatch.GetElapsedTime(Timestamp));
    }

    internal void ConfigureAppearanceDelay(int milliseconds) =>
        appearanceDelayMilliseconds = ToolbarAppearanceDelay.Normalize(milliseconds);

    private AppearanceDeadline StartAppearanceDeadline() => new(Stopwatch.GetTimestamp(), appearanceDelayMilliseconds);

    private async Task<SelectionSnapshot?> ValidateDelayedAppearanceAsync(SelectionSnapshot candidate, Func<bool> current,
        long ticket, uint clipboardSequence, nint sublimeFocus)
    {
        if (!current()) return null;
        ProbeResponse validation;
        if (candidate.Source == ClipboardSelectionAdapter.Source)
        {
            // The original probe lease binds the focused candidate before and after the guarded read.
            validation = await CaptureClipboardAsync(new(candidate.Id, candidate.Target), ticket, clipboardSequence);
        }
        else if (candidate.Source == SublimeSelectionAdapter.Source)
        {
            // The clipboard reader assigns a new ID per read. Bind it to the same native
            // source and editable-copy result instead of accepting matching text alone.
            bool FocusCurrent() => current() && SublimeClipboardEnabled
                && sublimeFocus != 0 && NativeClipboardSourceReader.ReadFocusedWindow(candidate.Target) == sublimeFocus;
            validation = await clipboardReader.CaptureAsync(candidate.Target, FocusCurrent,
                SublimeSelectionAdapter.ReadPlan(), clipboardSequence);
            return FocusCurrent() && validation.Success && validation.Selection is { } reread
                && reread.Target == candidate.Target && reread.Editable == candidate.Editable && reread.Text == candidate.Text
                ? candidate : null;
        }
        else validation = await probe.RequestAsync(new("validate", SessionId: candidate.Id));
        return current() && validation.Success && validation.Selection is { } selected
            && selected.Id == candidate.Id && selected.Target == candidate.Target && selected.Text == candidate.Text
            && selected.WriteToken == candidate.WriteToken && selected.WriteCaret == candidate.WriteCaret ? selected : null;
    }
}
