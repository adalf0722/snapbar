# OCR More Menu Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make OCR More immediately explain search, text source and action grouping while preserving existing execution and authorization.

**Architecture:** Extract a focused menu presentation component from OcrResultWindow.Actions.cs. Keep common ActionCatalog/settings, invocation handlers, LocalActionTicket and ActionOutputRouter unchanged. The menu owns a nonselectable search/source header, scrollable grouped action list and reachable management footer, sized to the target work area.

**Tech Stack:** .NET10, WPF/C#, existing ActionUi/theme resources, paired resx and console desktop test runners.

**Approved spec:** docs/superpowers/specs/2026-11-09-ocr-more-menu-polish-design.md including approved height/inline-reason/Other-actions revisions.

## Constraints and file ownership

Reuse current codex/ocr-capture-experience checkout. No publish/push/install/package replacement. Preserve source tickets, action definitions/settings/output modes/cancellation, local Undo/clipboard behavior and native selection safety. Do not change PopupWindow native input monitoring. No new preferences or action registry. Serial builds/tests; normal App exit required, never terminate unrelated processes. Markdown/JSON strict UTF-8 without BOM. Only fixture data in screenshots/logs.

Files: modify App/OcrResultWindow.Actions.cs for presentation wiring only; create focused App/OcrActionMenu.cs and optionally a local menu XAML resource if simpler than programmatic templates; paired Resources/Strings.resx and Strings.zh-Hant.resx. Adapt OcrActionsTest/InterfaceLanguageTest/ScreenOcrTest helpers where they assume the old search-as-MenuItem structure, preserving semantic AutomationIds and all existing assertions. Add focused menu presentation cases (separate helper file if useful), unique release-notes/2026-11-09-ocr-more-menu-polish.json and docs/p0-validation.md evidence. Avoid expanding the already-large action execution partial.

### Task 1: Complete the approved menu presentation and evidence

- [ ] Inspect existing Ocr More, ActionUi and original Popup presentation; create focused tests for placeholder/source/grouping/height/disabled explanations and confirm expected failures before implementation. Baseline shared action suite has60 cases; keep those behaviors.
- [ ] Implement a true input header: magnifier, localized Search actions placeholder, thin focus outline and no selectable blue row. Do not fix it by disabling hit testing, leaving a default selectable MenuItem wrapper or merely repainting blue. Search/source/footer are not executable action rows. Preserve editable query and opening-language snapshot.
- [ ] Show localized source caption for selected vs all recognized text, update while open without clearing selection, never echo OCR contents. Non-input action semantics unchanged. Opening menu reads live normalized settings; same existing action IDs and definition revalidation on invocation.
- [ ] Group empty-query actions into Recent (existing RecentIds max5) and Other actions, no duplicate IDs/empty headings. Search query shows Search results only; no matches has visible empty message and Enter no-op. Manage action remains separated/reachable and requires explicit interaction.
- [ ] Apply ActionUi typography/colors/icons/padding; default width320, maxheight420DIP clamped to target monitor/DPI/work-area availability. Only action list scrolls. Single-line rows min36DIP; short disabled reason inline when it fits, wraps when needed; full tooltip and AutomationProperties.HelpText retained. No fixed unused row reserved for every reason.
- [ ] Preserve Down/Up/Enter navigation, bring focused row into view, Esc closes menu first/editor retains selection, second Esc closes card; Tab avoids inert wrappers. Provide stable OcrMore/OcrActionSearch/OcrAction_<id>/OcrManageActions plus source/group/empty IDs. Existing action click contract calls the current RunActionAsync; no new executor.
- [ ] Adapt existing tests to find actual controls by stable ID/component API, not old first-MenuItem.Header assumptions; never create hidden duplicate controls just to satisfy tests. Verify selection retention, placeholder toggles, Recent/Other ordering, hidden filtering, query empty/results, disabled inline reasons/help, no-match Enter, keyboard/scroll/footer, target sizing and close/reopen cleanup. Retain all60 output/lifetime/security regressions.
- [ ] Capture EN/zh actual expanded menu with fixture-only card/backdrop and separate visual renders for search, groups, disabled state, no results, long list and constrained area. Check popup-to-card position on an actual expanded fixture, not only standalone component render; don't save user app pixels. Parent inspects screenshots before delivery.
- [ ] Run `pwsh -NoProfile -File scripts/check.ps1` (expected tooling/build/Core pass). Run `pwsh -NoProfile -Command "& ./scripts/test-desktop.ps1 -Suite OcrActions,Language,Appearance"` serially; run Ocr if its helper changed. No unrelated native Actions wheel reruns: existing unchanged failure also reproduced on preintegration42cfc23 and is documented. Do not claim full Release. New UI geometry can use simulated work area, label physical DPI/multimonitor limits honestly.
- [ ] Add unique Traditional Chinese release note with user benefit and More usage; append actual counts, report paths, screenshot inspection and hardware limits to docs/p0-validation.md. Strict UTF-8 readback and diff check. Commit owned implementation/tests/docs with Conventional Commits, write .superpowers/sdd/ocr-menu-polish-report.md with exact commands/results and any real limitations.
- [ ] Generate diff package from recorded task base, independent spec+quality/whole-change review; fix findings and repeat covering tests then scoped re-review. Mark plan/ledger complete only after actual delivery. Do not ask user to reconfirm already approved UI choices.

## Self-review

All three original improvements and approved Musk revisions are covered. Menu changes are presentation-only with existing executor/dependency contracts. Search/filter/group controls have semantic test access and proper keyboard behavior; no fake nested/hidden controls. Height and target work-area rules explicitly prevent the expanded UI from growing without bound. Baseline native wheel gate is preserved as a validation limitation, not part of this presentation change.
