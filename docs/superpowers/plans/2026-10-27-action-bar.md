# Action Bar and Custom Actions Implementation Plan

> **For agentic workers:** Use subagent-driven-development for the isolated Core and Windows tasks; integrate the WPF UI in this session. Steps use checkbox syntax for tracking.

**Goal:** Implement the approved compact toolbar, searchable overflow and URL / PowerShell / JavaScript action workflow.

**Architecture:** Core owns persisted definitions and pure policies; Windows owns bounded cancellable execution. WPF owns the compact nonactivating toolbar and action management/editor/results; SelectionController revalidates every side effect.

**Tech Stack:** .NET 10, WPF, Win32, System.Text.Json, existing console test runners.

## Global Constraints

- Preserve password rejection, foreground/focus and selection revalidation, stale cancellation, and clipboard compatibility opt-in. Do not log selection, clipboard, script, output, stderr, or destination URL.
- Use UTF-8 without BOM for Markdown, JSON and generated scripts.
- Six pinned actions maximum; recent five; new actions go to More; hidden actions never appear in search.
- Scripts receive selected text only through POPCLIP_TEXT, never interpolation or command arguments. Timeout 1–300 seconds, default 10. No script runs during save/import.
- Reuse the current checkout; commit only explicitly owned files. No changes to user preferences during automated tests.

## Task 1: Core definitions and persistence

- [x] Create `src/SnapBar.Core/ActionCatalog.cs`, `CustomActionPolicy.cs` and meaningful `tests/SnapBar.Tests/ActionCatalogTests.cs` behavior checks.
- [x] Implement the shared contracts in `.superpowers/sdd/core-brief.md`, including deterministic normalization, placement changes, ordering, recent updates and URL expansion.
- [x] Upgrade `SettingsStore.cs` to v4 with an `Actions` property, preserve v1–v3 loading and existing safety limits, and update migration assertions in `SettingsStoreTests.cs`.
- [x] Register checks in `tests/SnapBar.Tests/Program.cs`; run `.tools/dotnet/dotnet.exe run --project tests/SnapBar.Tests -c Release`. Expected: all checks pass.

## Task 2: Windows execution

- [x] Create `src/SnapBar.Windows/CustomActionRunner.cs` with `Task<ScriptExecutionResult> RunAsync(CustomActionDefinition action, string text, CancellationToken token)` and bounded UTF-8 process I/O.
- [x] Test real PowerShell execution and portable Node execution when available; reject bad runtime paths, invalid action kinds, timeout, cancellation, nonzero exit and overflow. No shell string interpolation.
- [x] Register `--custom-action-runner` in the Windows console runner; run that switch. Expected: successful output and cleanup, no private contents in status.

## Task 3: WPF toolbar and action settings

- [x] Create shared `ActionUi.cs` icon and styling helpers. Replace `PopupWindow.cs` with a one-row toolbar and bounded More list. Keep existing action AutomationIds.
- [x] Add scoped menu key filtering to `InputMonitor.cs`; handle characters and navigation only while the More panel is open and original target is foreground. Preserve ordinary keyboard delivery elsewhere.
- [x] Add `ActionSettingsWindow.cs` for immediate preview, placement, drag/arrow reorder, edit/duplicate/delete and preview-before-import/export.
- [x] Add `ActionEditorWindow.cs` for three types, variables, icon picker, output and timeout validation; explicit cancellable sample tests.
- [x] Add `ActionResultWindow.cs` for plain/Markdown output, raw Copy, conditional Apply and short HUD; no active HTML or remote resources.

## Task 4: Controller integration and delivery

- [x] Integrate action configuration, recent persistence and custom dispatch into `SelectionController.cs` and expose management entry points through `MainWindow.cs`.
- [x] Revalidate before execution and after script completion; retain nonactivating result actions only while original session is current. Apply validates editability, clipboard sequence and modifiers immediately before paste.
- [x] Adapt native UI tests to the new visual hierarchy instead of casting `PopupWindow.Content` to the old WrapPanel. Add targeted action integration checks.
- [x] Run `scripts/build.ps1`, Core tests, the script runner checks and relevant web/path regressions. Review the full diff for safety and scope.
- [x] Publish to `artifacts/p0`, inspect native UI, update `README.md` and `docs/p0-validation.md` with evidence and limitations. Verify modified Chinese Markdown with strict UTF-8 decoding.
