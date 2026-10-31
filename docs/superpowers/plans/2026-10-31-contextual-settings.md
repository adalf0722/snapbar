# SnapBar Contextual Settings Implementation Plan

> Execute inline in the existing checkout; user approved the preceding three-part design.

**Goal:** Make action settings describe actual behavior and expose common choices directly.

**Architecture:** Extend CustomActionDefinition with a default-true UseSelection flag and version persistence safely. Keep input suppression at both controller dispatch and native runner boundaries. Compose group tabs and search presets in the existing WPF windows.

**Tech Stack:** .NET 10, C#, WPF, existing console runners.

- [x] Core: add UseSelection, enforce URL input, schema 5 migration, action envelope v2 with v1 import support; extend policy/persistence tests.
- [x] Runtime: suppress script input when disabled, retain all validation/cancellation/writing checks; verify direct runner and controller dispatch.
- [x] Editor: contextual output help, explicit script input option, conditional input/comparison and variable insertion; preserve original action defaults and drafts.
- [x] Management: group tabs with live counts, hidden visibility, empty states and unchanged editing/sorting. Update existing layout fixtures to select the relevant tab.
- [x] Search: provider presets plus explicit save, custom-only fields and preserved custom draft.
- [x] Verification: build; run Core, UI appearance, layout, diagnostics, script runner and custom-action integrations; inspect native UI; publish, restart and record evidence with UTF-8 checks.

Completed: Core 282, UI appearance 40, layout 13, diagnostics 25, native script runner 20, custom-action integration 65, tray lifecycle 15 (460 total), plus six Probe smoke checks. Release and self-contained publish passed without warnings/errors. Native app checks confirmed provider dropdown and More navigation without saving or modifying user actions.
