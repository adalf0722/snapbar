# AI Panel Visual Integration Implementation Plan

> **For agentic workers:** Execute the approved tasks in this session. Steps use checkbox syntax for tracking.

**Goal:** Apply the approved visual integration without changing AI requests or selection safety.

**Architecture:** Keep changes in panel composition and panel-local styles. Share existing theme tokens and ActionPlacementTab visuals. Reader typography belongs in AiMarkdownView.

**Tech Stack:** .NET 10, WPF, existing console/desktop checks.

## Task 1: Panel appearance

Files: `src/SnapBar.App/AiPanelWindow.cs`, `src/SnapBar.App/AiMarkdownView.cs`, `src/SnapBar.App/Themes/Controls.xaml`.

- [ ] Add a panel tab button style with the existing ActionPlacementTab treatment, using `Tag` to represent selected state. Add a row-management style whose opacity changes on parent-row hover, keyboard focus and open context menu; preserve layout and tab focus.
- [ ] Apply 24-DIP outer inset, a canvas rail and white reader, a 24-DIP title, compact provider/settings control, shared vector icons and a single bounded result metadata row. Use 15-DIP reader text, 24-DIP paragraph line height and 20-DIP text insets.
- [ ] Align footer status/actions and use accent/white contrast for readonly result copy, secondary original copy, and accent editable replacement. Keep request/session/controller code unchanged.

## Task 2: Native verification

File: `tests/SnapBar.Windows.Tests/AiPanelTest.cs`.

- [ ] Keep existing behavior coverage and regular/minimum/long-instruction snapshots. Add focused assertions for keyboard-reachable row management and readonly-copy/editable-replace emphasis; do not mirror every visual property in tests.
- [ ] Run `pwsh -NoProfile -File scripts/check.ps1`, then `pwsh -NoProfile -Command "& './scripts/test-desktop.ps1' -Suite @('Release','Layout')"` after normal package exit. Require all suites to pass and inspect generated panel images.

## Task 3: Delivery

Files: this plan and `docs/p0-validation.md`.

- [ ] Record evidence and limits, verify UTF-8 without BOM, commit only task-owned changes, publish using the successful report with `scripts/build.ps1 -Publish -DesktopReport <report>`, and launch the updated package without changing user AI settings.
