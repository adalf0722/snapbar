# SnapBar UI Density Implementation Plan

> Execute inline in the current checkout under the user-approved density design; preserve the prior UI work and existing selection/clipboard safety.

**Goal:** Reduce wasted space and make test results understandable without changing executable content.

**Architecture:** Keep existing window composition and shared controls. Add a small App-only comparison view for exact input/output and a display-only whitespace representation. Keep runtime and selection contracts intact.

**Tech Stack:** .NET 10, WPF, existing C# console runners.

## Tasks

- [x] Update `MainWindow.cs`: compact cards for collapsed settings; put latest routine status inside diagnostics, preserve explicit visible notices for configuration errors and initialization failures.
- [x] Update `ActionSettingsWindow.cs`: reduce preview padding/margins, 56-DIP rows, full-opacity actions and readable labels.
- [x] Add `ActionTestComparisonView.cs`: exact input/output summary and collapsed whitespace views; no diff dependency or changes to raw strings.
- [x] Update `ActionEditorWindow.cs`: smaller idle result, stronger secondary test action, captured input/revision for result association, type-aware default icons preserving explicit selections.
- [x] Extend `UiAppearanceTest.cs`: successful comparison with Unicode/whitespace, result invalidation after edit, empty output, icon defaults, existing min-size/footer/cancellation checks; preserve test fixtures and temporary settings.
- [x] Run `scripts/build.ps1` and UI appearance, diagnostics, action-layout, tray-lifecycle runners. Inspect new WPF screenshots and native controls using computer-use.
- [x] Exit the old published app only after checking its open windows, run `scripts/build.ps1 -Publish`, verify protocol smoke and current published app startup, update validation docs and verify UTF-8.

Completed: 348 checks passed (Core 275, appearance 20, layout 13, diagnostics 25, tray lifecycle 15), plus six Probe smoke checks. Published app restarted with persisted automatic detection intact. Existing custom action icons were preserved; new scripts default to terminal. Screenshot review includes ordinary/minimum window sizes and expanded Unicode/whitespace comparison.

## Approved final interaction pass

- [x] On `AdvancedActionSettings.Expanded`, queue the first field's `BringIntoView` after WPF layout; guard collapsed/not-visible controls and keep keyboard focus unchanged.
- [x] Omit an empty Hidden group, retaining populated groups and normal placement editing.
- [x] Verify both changes in UiAppearanceTest at minimum editor size; run actual selection/toolbar interaction checks on disposable fixtures, then publish and update validation evidence.

Final pass: appearance 23, layout 13, diagnostics 25, custom actions 61, menu keyboard 21 and Core 275 checks passed (418 total); six additional Probe smoke checks. Native mouse tests cover popup placement, hover feedback, Copy dispatch and dismissal on source scroll. Clipboard is backed up before the first real Copy and restored at test completion.
