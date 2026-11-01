# Task 1 Report: Preserve Icon IDs in Core

## Changes

- Added custom icon round-trip cases for built-in names, legacy-style identifiers, Fluent-style identifiers, and unknown valid identifiers.
- Added fallback cases for empty, whitespace-containing, path-like, non-ASCII, overlong, null, and control-character identifiers. Added the 128-character accepted boundary case.
- Added `CustomActionPolicy.NormalizeIcon`, which preserves 1–128 ASCII letters/digits, underscore, hyphen, or colon and maps other values to `spark`.
- Applied the same normalization from `ActionCatalog.Normalize`, `CustomActionPolicy.Export`, and `CustomActionPolicy.Import` without changing their validation order or other fields.

## RED/GREEN evidence

RED command:

```powershell
.tools/dotnet/dotnet.exe run --project tests/SnapBar.Tests -c Release
```

Before the implementation, the runner reported `408 passed, 4 failed`. The failing cases were round trips for `legacy:clean`, `translate_20_regular`, and `unknown-fixture`, plus the accepted 128-character boundary. The other new invalid-value fallback cases passed against the prior behavior.

GREEN command:

```powershell
.tools/dotnet/dotnet.exe run --project tests/SnapBar.Tests -c Release | Select-String 'custom icon|failed|passed'
```

Output included all custom icon cases passing and ended with `412 passed, 0 failed`.

## Review and concerns

- The change is confined to Core policy and its console tests; selection validation, action execution permissions, and URL validation were not changed.
- The commit helper's documented `scripts/validate_commit.py` was not present in this checkout, so that optional message validator could not be run. The requested Conventional Commit subject is used.
