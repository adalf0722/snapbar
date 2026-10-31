# Repository Guidelines

## Project Structure & Module Organization

SnapBar is a Windows text-selection toolbar prototype built with .NET 10 and WPF. `SnapBar.slnx` contains:

- `src/SnapBar.Core/`: selection contracts, routing, validation policies, and popup placement.
- `src/SnapBar.Windows/`: Win32 interop, input monitoring, clipboard handling, and probe management.
- `src/SnapBar.Probe/`: isolated UI Automation (UIA) reading and selection revalidation.
- `src/SnapBar.App/`: WPF windows and selection orchestration.
- `tests/SnapBar.Tests/` and `tests/SnapBar.Windows.Tests/`: console test runners; `tests/fixtures/` holds sample text and HTML.

Designs and validation records live in `docs/`; distribution instructions live in `packaging/`. Generated packages go to ignored `artifacts/p0/`.

## Development Entry Points

Use Windows, PowerShell 7 and the SDK selected by `global.json`. Scripts prefer `.tools/dotnet/dotnet.exe`, then PATH. See [docs/development.md](docs/development.md) for test suites, reports, release prerequisites and review guidance.

```powershell
pwsh -NoProfile -File .\scripts\check.ps1
pwsh -NoProfile -File .\scripts\install-hooks.ps1
pwsh -NoProfile -File .\scripts\test-desktop.ps1 -List
pwsh -NoProfile -File .\scripts\test-desktop.ps1 -Suite Release
pwsh -NoProfile -File .\scripts\build.ps1 -Publish
```

Publishing requires successful desktop Release evidence for the current source and no App/Probe processes using the package, including tray-only processes. It stages and smoke-tests a fresh package before replacing `artifacts/p0`; failures retain the previous package. Exit SnapBar normally before desktop tests or publishing.

## Coding Style & Naming Conventions

Match existing C#: four-space indentation, file-scoped namespaces, PascalCase types and methods, and camelCase locals and private fields. Nullable references and implicit usings are enabled centrally in `Directory.Build.props`. No dedicated formatter or linter is configured. Keep platform-independent policy in Core and native operations in Windows or Probe.

Write Markdown as UTF-8 without BOM; explicitly select UTF-8 when scripting writes and verify Chinese text reads back correctly.

## Testing Guidelines

Tests use custom console runners, without third-party frameworks. Add descriptive behavior names through the existing `Check` pattern. Run relevant runners explicitly; `dotnet test` is not sufficient. No numeric coverage threshold is configured.

Clipboard tests temporarily replace clipboard contents; avoid concurrent copying. Use disposable fixture text for Cut/Paste checks. Record cross-app and DPI verification in `docs/p0-validation.md`.

## Commit & Pull Request Guidelines

Use concise, imperative, type-prefixed Conventional Commits. PRs should explain behavior changes, link relevant issues or design notes, list validation performed and remaining limitations, and include screenshots for UI changes.

## Selection Safety

Preserve password-field rejection, foreground/focus revalidation, candidate identity and stale-result cancellation. Generic clipboard compatibility defaults on and must respect persisted preferences; Sublime key bindings require explicit first-time setup and persisted choices must be preserved. Keep selected text and clipboard contents out of logs. Review timeout/lease and action permission changes across Core, Windows, Probe and App together.
