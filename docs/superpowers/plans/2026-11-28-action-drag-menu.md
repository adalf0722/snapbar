# Action Drag and Context Menu Implementation Plan

**Goal:** Make action movement discoverable and support moving between Bar and More with consistent context commands.

**Architecture:** Keep placement and visibility policy in ActionCatalog. Put panel drag/drop and context commands in a focused partial class; generate DPI-aware grab cursors from WPF geometry. Reuse existing persistence, validation messages and menu styles.

**Tech Stack:** .NET 10, WPF, existing console test runners.

- [x] Add shared panel interaction helpers in `src/SnapBar.App/ActionSettingsPanel.Interactions.cs`: panel-owned drag payload, placement validation, drop feedback and commit-on-drop. Bar rows select before/after insertion; More keeps its current ordering.
- [x] Add `src/SnapBar.App/GrabCursors.cs` using WPF vector geometry and in-memory CUR data for open/closed hands. Use the closed hand during allowed moves and the system forbidden cursor for rejected drops.
- [x] Wire handles, names, group headings, empty groups and row menus in `src/SnapBar.App/ActionSettingsPanel.cs`. Preserve button and switch hit targets, drag threshold, Esc cancellation and search restrictions.
- [x] Add matching strings in both `src/SnapBar.App/Resources/Strings*.resx`; add bilingual `release-notes/2026-11-28-action-drag-menu.json`.
- [x] Extend `tests/SnapBar.Windows.Tests/ActionLayoutTest.cs` with placement/menu, capacity, visibility and drag-target behavior checks. Run `pwsh -NoProfile -File scripts/build.ps1`, then desktop Layout and Appearance suites, and inspect generated screenshots.
- [x] Read Chinese Markdown back as strict UTF-8, check whitespace and record verification in `docs/p0-validation.md`.
