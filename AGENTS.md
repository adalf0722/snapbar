# Repository Guidelines

## Project Structure & Module Organization

SnapBar is a Windows text-selection toolbar prototype built with .NET 10 and WPF. `SnapBar.slnx` contains:

- `src/SnapBar.Core/`: selection contracts, routing, validation policies, and popup placement.
- `src/SnapBar.Windows/`: Win32 interop, input monitoring, clipboard handling, and probe management.
- `src/SnapBar.Probe/`: isolated UI Automation (UIA) reading and selection revalidation.
- `src/SnapBar.App/`: WPF windows and selection orchestration.
- `tests/SnapBar.Tests/` and `tests/SnapBar.Windows.Tests/`: console test runners; `tests/fixtures/` holds sample text and HTML.

Designs and validation records live in `docs/`; distribution instructions live in `packaging/`. Generated packages go to ignored `artifacts/p0/`.

## Build, Test, and Development Commands

Use Windows, PowerShell, and SDK `10.0.402` or a compatible patch permitted by `global.json`. Build scripts prefer `.tools/dotnet/dotnet.exe`, then use `dotnet` from PATH.

```powershell
pwsh -NoProfile -File .\scripts\build.ps1
pwsh -NoProfile -File .\scripts\build.ps1 -Publish
dotnet run --project tests/SnapBar.Tests -c Release
dotnet run --project tests/SnapBar.Windows.Tests -c Release -- --clipboard-roundtrip
```

The first command builds Release, runs core tests, and copies the probe beside the app. `-Publish` also creates self-contained Windows x64 packages and runs probe protocol smoke checks. Substitute the bundled executable for `dotnet` if necessary. After publishing, run `artifacts/p0/SnapBar.App.exe`; add `--fixture` for the test window.

## Coding Style & Naming Conventions

Match existing C#: four-space indentation, file-scoped namespaces, PascalCase types and methods, and camelCase locals and private fields. Nullable references and implicit usings are enabled centrally in `Directory.Build.props`. No dedicated formatter or linter is configured. Keep platform-independent policy in Core and native operations in Windows or Probe.

Write Markdown as UTF-8 without BOM; explicitly select UTF-8 when scripting writes and verify Chinese text reads back correctly.

## Testing Guidelines

Tests use custom console runners, without third-party frameworks. Add descriptive behavior names through the existing `Check` pattern. Run relevant runners explicitly; `dotnet test` is not sufficient. No numeric coverage threshold is configured.

Clipboard tests temporarily replace clipboard contents; avoid concurrent copying. Use disposable fixture text for Cut/Paste checks. Record cross-app and DPI verification in `docs/p0-validation.md`.

## Commit & Pull Request Guidelines

The current history contains one commit: `feat: add Windows SnapBar selection toolbar`. Follow its concise, imperative, type-prefixed style. PRs should explain behavior changes, link relevant issues or design notes, list validation performed and remaining limitations, and include screenshots for UI changes.

## Selection Safety

Preserve password-field rejection, foreground/focus revalidation, stale-result cancellation, and explicit opt-in clipboard compatibility. Keep selected text and clipboard contents out of logs.
