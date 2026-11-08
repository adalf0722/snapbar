# Multiple Dictionaries Implementation Plan

**Goal:** Display installed dictionary names and usable entry counts, independently enable/remove dictionaries, and look up all enabled sources.

**Architecture:** Keep the bounded StarDict parser and its validated binary snapshots. Add an atomically saved library manifest, migrate the original snapshot without deleting it, and preserve selection validation around grouped results. Same-name imports require explicit replacement; failed imports preserve the library.

**Tech stack:** .NET 10, WPF, System.Text.Json, existing console and desktop runners.

## Implementation

- [x] Expose validated name/count in `src/SnapBar.Core/OfflineDictionary.cs`. Add `OfflineDictionaryLibrary.cs` for storage, legacy migration, duplicate detection, enabled preferences and lookup groups. Persist immutable snapshots before switching the manifest; only delete unreferenced files after a successful manifest save.
- [x] Add `tests/SnapBar.Tests/OfflineDictionaryLibraryTests.cs` with multiple-source lookup, enable/reload/remove, duplicate update, failed replacement, missing/damaged sources, manifest write failure, path validation and legacy migration cases.
- [x] Replace the single status in `src/SnapBar.App/MainWindow.Dictionary.cs` with per-source rows, counts, enable switches and removal. Confirm same-name replacements. Preserve settings navigation and expose automation IDs.
- [x] Route `SelectionController.Dictionary.cs` through the library and render source headings inside the existing bounded result scroll area in `ActionResultWindow.cs`. Keep focus revalidation, stale cancellation, compact sizing and clipboard behavior.
- [x] Extend `tests/SnapBar.Windows.Tests/LookupTest.cs` and `SettingsNavigationTest.cs` for management and grouped lookup, then run `scripts/check.ps1` and `scripts/test-desktop.ps1 -Suite Lookup,Appearance`. Inspect screenshots and record actual validation results.

## Acceptance

The management page retains names and counts after restart; importing a second source preserves the first. Disabled sources remain installed and do not produce results. Exact matches are tried across all sources before word-form fallbacks. Each result identifies its source. A failed update does not replace a working snapshot. Existing single-source installations migrate automatically. No selected text or definitions enter logs.

## Validation

Final repository checks passed: tooling 21, push contracts 10, automatic-release contracts 11, Core 485, Release build. Desktop preflight 1, Lookup 16 and Appearance 174 passed with no failures; report: `artifacts/checks/desktop/20261107T120144549Z-cd02fc5b470f4c61a0db0341245364f8/report.json`. Inspected `artifacts/ui-polish/settings-multiple-dictionaries.png` and `lookup-multiple-dictionaries.png`. The first desktop attempt encountered a Windows foreground grant race in the disposable fixture; renewing its existing grant while it owns the foreground resolved the issue, and the full requested suites passed on rerun. No distribution package was published.
