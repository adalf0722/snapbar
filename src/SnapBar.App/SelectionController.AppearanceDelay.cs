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

    private sealed record SublimeAppearanceSource(nint Focus, CaretIdentity? Caret);

    internal void ConfigureAppearanceDelay(int milliseconds) =>
        appearanceDelayMilliseconds = ToolbarAppearanceDelay.Normalize(milliseconds);

    private AppearanceDeadline StartAppearanceDeadline() => new(Stopwatch.GetTimestamp(), appearanceDelayMilliseconds);

    private async Task<SelectionSnapshot?> ValidateDelayedAppearanceAsync(SelectionSnapshot candidate, Func<bool> current,
        SublimeAppearanceSource? sublimeSource)
    {
        if (!current()) return null;
        if (candidate.Source == ClipboardSelectionAdapter.Source)
        {
            // Presentation verifies the original lease and focus without injecting a copy.
            var guard = await probe.RequestAsync(new("clipboard-validate", SessionId: candidate.Id));
            return current() && ClipboardCompatibilityEnabled && guard.Success
                && guard.ClipboardTarget == new ClipboardCaptureTarget(candidate.Id, candidate.Target,
                    candidate.WriteToken, candidate.WriteCaret)
                && (candidate.WriteCaret == null || NativeCaretReader.ReadIdentity(candidate.Target) == candidate.WriteCaret)
                ? candidate : null;
        }
        if (candidate.Source == SublimeSelectionAdapter.Source)
        {
            return SublimeClipboardEnabled && SublimeCompatibility.Ready() && current()
                && sublimeSource is { Focus: not 0 }
                && NativeClipboardSourceReader.ReadFocusedWindow(candidate.Target) == sublimeSource.Focus
                && (sublimeSource.Caret == null || NativeCaretReader.ReadIdentity(candidate.Target) == sublimeSource.Caret)
                ? candidate : null;
        }
        var validation = await probe.RequestAsync(new("validate", SessionId: candidate.Id));
        return current() && validation.Success && validation.Selection is { } selected
            && selected.Id == candidate.Id && selected.Target == candidate.Target && selected.Text == candidate.Text
            && selected.WriteToken == candidate.WriteToken && selected.WriteCaret == candidate.WriteCaret ? selected : null;
    }
}
