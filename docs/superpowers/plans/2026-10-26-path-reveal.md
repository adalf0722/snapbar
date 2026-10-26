# Path Reveal Implementation Plan

> Inline execution in the existing checkout; preserve the existing selection validation and console runner patterns.

**Goal:** Select a path in Explorer without executing the selected item or blocking the WPF UI.

**Architecture:** Core performs lexical normalization. A dedicated Probe path worker prepares the Shell PIDL, then reveals only after the controller revalidates the original selection. Cancellation and request deadlines terminate the isolated worker.

**Tech Stack:** .NET 10, WPF, Win32 Shell PIDLs, JSON pipes, custom console runners.

## Tasks

- [x] Add `src/SnapBar.Core/PathActionPolicy.cs` and `tests/SnapBar.Tests/PathActionPolicyTests.cs`. Check drive/UNC/profile syntax, paired quotes, Unicode, spaces, commas, dot segments; reject relative/device/ADS/wildcard/URI input without file I/O. Run `./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Tests -c Release`.
- [x] Extend `Contracts.cs` with `TextAction.RevealPath`, optional request path and target identity. Add `src/SnapBar.Windows/ShellPathSelection.cs`, `src/SnapBar.Probe/PathWorker.cs`; extend `ProbeClient` with worker mode and cancellation. `prepare-path` replaces and owns the PIDL, `reveal-path` consumes it once and checks target identity. All errors return neutral statuses.
- [x] Add optional recording path launcher to `SelectionController`, conditional `RevealPathAction` button in `PopupWindow`, cancellation on invalidation, and a second selection check after prepare. Add readonly path fixture arguments in `Program.cs` and `FixtureWindow.cs`.
- [x] Add `tests/SnapBar.Windows.Tests/PathActionsTest.cs` for readonly button events, exact normalized path, invalidation and clipboard invariants. Add worker lifetime tests for cancellation, timeout and restart; native prepare tests use temporary files and directories. Add an opt-in Shell smoke check and inspect its actual Explorer result.
- [x] Run `./scripts/build.ps1 -Publish`, relevant Windows regressions and the new runners sequentially. Update README, package instructions, validation and handoff. Verify Chinese Markdown as strict UTF-8 without BOM; create a new ZIP and compare each entry SHA-256 with `artifacts/p0`.

## Completion criteria

No automatic filesystem lookup while presenting the toolbar; readonly paths work; invalidated or timed-out requests do not reveal later; failures omit source paths; published package contains the current binaries and validation record.


Completed: 310 checks passed; Release/publish produced zero warnings and errors. All 883 ZIP entries match the published files by SHA-256. See docs/p0-validation.md for native Explorer evidence and remaining manual coverage.
