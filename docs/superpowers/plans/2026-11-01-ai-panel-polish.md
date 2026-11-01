# AI Panel Visual Integration Implementation Plan

> **For agentic workers:** Execute the approved tasks in this session. Steps use checkbox syntax for tracking.

**Goal:** Apply the approved visual integration without changing AI requests or selection safety.

**Architecture:** Keep changes in panel composition and panel-local styles. Share existing theme tokens and ActionPlacementTab visuals. Reader typography belongs in AiMarkdownView.

**Tech Stack:** .NET 10, WPF, existing console/desktop checks.

## Task 1: Panel appearance

Files: `src/SnapBar.App/AiPanelWindow.cs`, `src/SnapBar.App/AiMarkdownView.cs`, `src/SnapBar.App/Themes/Controls.xaml`.

- [x] Add a panel tab button style with the existing ActionPlacementTab treatment, using `Tag` to represent selected state. Add a row-management style whose opacity changes on parent-row hover, keyboard focus and open context menu; preserve layout and tab focus.
- [x] Apply 24-DIP outer inset, a canvas rail and white reader, a 24-DIP title, compact provider/settings control, shared vector icons and a single bounded result metadata row. Use 15-DIP reader text, 24-DIP paragraph line height and 20-DIP text insets.
- [x] Align footer status/actions and use accent/white contrast for readonly result copy, secondary original copy, and accent editable replacement. Keep request/session/controller code unchanged.

## Task 2: Native verification

File: `tests/SnapBar.Windows.Tests/AiPanelTest.cs`.

- [x] Keep existing behavior coverage and regular/minimum/long-instruction snapshots. Add focused assertions for keyboard-reachable row management and readonly-copy/editable-replace emphasis; do not mirror every visual property in tests.
- [x] Run `pwsh -NoProfile -File scripts/check.ps1`, then `pwsh -NoProfile -Command "& './scripts/test-desktop.ps1' -Suite @('Release','Layout')"` after normal package exit. Require all suites to pass and inspect generated panel images.

## Task 3: Delivery

Files: this plan and `docs/p0-validation.md`.

- [x] Record evidence and limits, verify UTF-8 without BOM, commit only task-owned changes, publish using the successful report with `scripts/build.ps1 -Publish -DesktopReport <report>`, and launch the updated package without changing user AI settings.

Final desktop report: `artifacts/checks/desktop/20261101T004222963Z-3a975a9064d245efaa2e1831d7d984be/report.json`. Publication: `artifacts/checks/build/20261101T004531328Z-01681e5d77ee47d3a7dd9b2138abcf6b/report.json`. New package launched and returned to tray; preserved all user AI configuration and pre-existing uncommitted changes.
