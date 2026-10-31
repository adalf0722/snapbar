# Whitespace Preview Implementation Plan

**Goal:** Present whitespace inspection as a lightweight view of the existing test result while keeping line boundaries accurate.

**Architecture:** Retain raw output in the editor and refresh only its preview when the checkbox changes. Reuse the comparison's visible-whitespace renderer, which handles CR/LF/CRLF explicitly. Keep selection input controlled by the existing setting.

**Tech Stack:** .NET 10, WPF, custom console runners.

- [x] Update `src/SnapBar.App/ActionTestComparisonView.cs`: move count/status text into the collapsed body, rename the header to input/output comparison, disable soft wrapping, enable horizontal scrolling, and preserve CR-only line boundaries in the renderer.
- [x] Update `src/SnapBar.App/ActionEditorWindow.cs`: add Show whitespace beside the result heading, store the raw successful output, render symbols only when checked, and clear/reset display state with the existing preview lifecycle. Never trigger execution or alter action settings from this checkbox.
- [x] Extend `tests/SnapBar.Windows.Tests/UiAppearanceTest.cs`: verify no second execution on toggle, exact raw output restoration, long-line layout, CR/LF/CRLF, reset behavior, failure/URL visibility, and selection-disabled results. Render collapsed, expanded, and minimum-size snapshots.
- [x] Run `scripts/build.ps1`, `--ui-appearance`, and the existing `--action-diagnostics` runner. Inspect the snapshots and record actual counts and limitations in `docs/p0-validation.md`.
- [x] Publish App and Probe to `artifacts/whitespace-review`, run probe smoke checks, verify strict UTF-8 Markdown and `git diff --check`, and deliver the executable path. Preserve all unrelated uncommitted changes.
