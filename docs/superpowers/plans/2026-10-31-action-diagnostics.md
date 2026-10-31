# Custom Action Diagnostics Implementation Plan

> Execute inline in this checkout, as authorized by the user after reviewing the design. Review each task against the approved specification before proceeding.

**Goal:** Make custom action failures understandable while retaining bounded, copyable original diagnostics.

**Architecture:** Keep process capture in Windows. Add a shared formatter and WPF diagnostic view in App, consumed by the editor and existing result-window lifecycle. Preserve successful output routing and all selection validation.

**Tech Stack:** .NET 10, WPF, Windows PowerShell, custom console test runners.

## Task 1: Preserve process diagnostics

- [x] Extend `src/SnapBar.Windows/CustomActionRunner.cs` result metadata with `OutputTruncated`, `ErrorTruncated`, and `CaptureIncomplete` flags, defaulting to false.
- [x] Replace discarded stream task results on timeout with bounded, synchronized capture snapshots. Preserve stdout/stderr and available exit code after job termination; return exception messages for startup failures.
- [x] Extend `tests/SnapBar.Windows.Tests/CustomActionRunnerTest.cs` with real scripts: `[Console]::Write('before'); [Console]::Error.Write('failure'); exit 7`, `Read-Host 'input'`, a flushed marker before `Start-Sleep 30`, and overflow in each stream. Verify retained contents and metadata without printing selected text.
- [x] Run `.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release -- --custom-action-runner`; require all checks to pass, including descendant cleanup.

## Task 2: Shared presentation and routing

- [x] Create `src/SnapBar.App/ScriptDiagnostic.cs`: `Create(CustomActionDefinition action, ScriptExecutionResult result)` returns summary and details, preserving both streams and displaying termination/capture metadata. Detect the actual NonInteractive signature before suggesting an interaction fix; suggest curl.exe only for an actual curl command.
- [x] Create `src/SnapBar.App/ActionDiagnosticView.cs`: a summary, collapsed Details expander with a readonly scrolling textbox, and Copy details with clipboard-busy feedback. Copy only on explicit click.
- [x] Modify `src/SnapBar.App/ActionResultWindow.cs` with an optional diagnostic mode using the same native nonactivation hooks and lifecycle; omit Apply, automatic clipboard changes, and the HUD timer in diagnostic mode.
- [x] Modify `src/SnapBar.App/SelectionController.CustomActions.cs` to show a diagnostic window on current failed executions and retain it until closed. Preserve the preexisting stale/cancel checks.
- [x] Modify `src/SnapBar.App/ActionEditorWindow.cs` to show the shared diagnostic view inline for failures, retain stderr diagnostics on successful tests, and clear stale details when inputs change or another test starts.

## Task 3: Validation and delivery

- [x] Add `tests/SnapBar.Windows.Tests/ActionDiagnosticsTest.cs` and `--action-diagnostics` routing in `Program.cs`. Assert exact multiline Unicode diagnostics, exit and truncation metadata, conservative hints, collapsed Details, copy control, no Apply, and editor inline failure behavior with `throw 'fixture failure'`.
- [x] Extend `CustomActionsTest.cs` with injected failed results and stale failures to verify actual controller routing.
- [x] Run `scripts/build.ps1`, `--custom-action-runner`, and `--action-diagnostics`. Use the dedicated fixture for controller checks when the foreground can be controlled.
- [x] Capture the native diagnostic view for visual inspection. Update `docs/p0-validation.md` with checks run and any remaining cross-app limitations; verify edited Markdown as strict UTF-8 without BOM.
- [x] Review `git diff --check` and the final diff. Report the built executable path and validation results.
