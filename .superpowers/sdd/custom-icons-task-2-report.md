# Task 2 Report: Shared Themed Icon Rendering

## Changes

- Added `AiIconPresenter.Foreground`, defaulting to `ActionUi.Muted`; changing it rerenders Fluent glyphs and fallback vectors with the requested brush.
- Added `ActionUi.ItemIcon`: custom actions use the shared presenter and built-in actions retain the existing vector factory. Existing `Icon` behavior and catalog resolution rules are unchanged.
- Renamed the picker title to `選擇圖示`.
- Added presenter tests for explicit legacy and bare legacy names, official Fluent names, unknown legal IDs falling back to spark, default and changed colors, factory routing/sizing, and the generic picker title.

## Validation

Command: `pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite AiPanel`

Output:

```text
Build: exit 0
Core: exit 0; 412 passed, 0 failed
Preflight: exit 0; 1 passed, 0 failed
AiPanel: exit 0; 130 passed, 0 failed
```

Desktop report: `artifacts/checks/desktop/20261101T234303983Z-f63ef2e37eca4e34876f03c25c0f3908/report.json`.
Build details: `artifacts/checks/build/20261101T234304734Z-9074f20611994c49a6e0ae43131825f0/Build.log`.

Warnings: existing CS8602 at `AiPanelWindow.cs:95` (reported for temporary and final WPF projects) and existing CS4014 on `editor.Dispatcher.BeginInvoke`, now at `AiIconTest.cs:142` because new checks shifted its line number. Both statements were confirmed in HEAD before this task. No new warnings.

`git diff --check` passed. Commit subject validation via `python C:/Users/User/.agents/skills/commit-message-helper/scripts/validate_commit.py "refactor(icons): support shared themed rendering"` returned `Valid commit message format`.

## Self-review and concerns

- `Stored` retains priority for legacy names; `TryResolve` is untouched.
- Picker selection, cancellation, IME, keyboard navigation, and virtualization behavior remain covered by passing existing cases.
- No selection safety, execution, clipboard, or persisted preference behavior was changed.
- Task 2 only introduces the action factory; downstream action surfaces will adopt it in later tasks.
- No publishing performed. No new concerns.
