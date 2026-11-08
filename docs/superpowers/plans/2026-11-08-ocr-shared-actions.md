# OCR Shared Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make OCR recognized text use the same configured actions as external text selection, with explicit local-target validation and preserved output semantics.

**Architecture:** Share the action catalog, runner and output routing. Keep external selection authorization in SelectionController; local OCR tickets validate card identity, text and selection revision. Adapt AI and result presentation with a local target contract, never by manufacturing a writable native selection.

**Tech Stack:** .NET 10, WPF/C#, existing Core policy and console test runners, CustomActionRunner, AI and dictionary services, bilingual resx resources.

**Approved spec:** `docs/superpowers/specs/2026-11-08-ocr-shared-actions-design.md`. User explicitly requested implementation after approving the spec.

## Global constraints

Reuse D:/codex/snapbar and the current codex/ocr-capture-experience branch. Base commit42cfc23. No publish, push, install, replacement of artifacts/p0, or killing unrelated processes. User normally exited the running development App/Probe before implementation; verify exit before builds/desktop tests. Preserve password rejection, foreground/focus/identity validation, probe/clipboard leases, stale cancellation, saved action settings and OCR language preferences. Never print selected text, clipboard data, script input or screenshots of user apps in logs. UTF-8 without BOM for Markdown/JSON and strict Chinese readback. Builders/tests run serially; one implementation agent at a time.

No new per-OCR action settings/schema. Include both Bar and More placements and exclude Hidden; keep copy outside, exclude native cut/paste and OCR. Include UseSelection=false custom actions even if the successful OCR editor is blank. Source adapters may share capabilities but must not bypass native policies. Existing native code performs its existing validations before/after shared execution and again on deferred Apply.

## File boundaries and contracts

- Core `ActionCatalog.cs` and a small local-input policy file: derive OCR menu items from existing normalized settings and existing content policies. Track OCR card/session identity, text and selection revisions, immutable input range/ticket, and guarded replacement. Existing external-selection overload retains native write policy.
- App `ActionOutputRouter.cs` (new): one central dispatch for None/Clipboard/ReplaceSelection/Hud/Window/Markdown and diagnostic behavior. Host callbacks supply source-specific validated copy/replace/present/status. Refactor SelectionController.CustomActions.cs to use it after unchanged native validation. Source adapters supply deferred Apply callbacks; do not duplicate the output-mode switch in OCR.
- App `OcrResultWindow.cs` plus focused `OcrResultWindow.Actions.cs`/`OcrActionController.cs` as needed: More menu, ticket/source adapter, busy/cancel state, shared action dispatch, local replace with Undo, child lifetime. Keep one file per clear responsibility rather than growing a single large controller.
- App `MainWindow.Ocr.cs` and existing settings/dictionary wiring: provide live action settings, remember/save callback, management entry, shared AI/dictionary/path services. Both ShowOcrResult and real capture path receive the same action dependencies.
- App `AiPanelWindow.cs` and `AiPanelWindow.Commands.cs` only where necessary: introduce explicit text-target overload/adapter for local OCR input and validated Apply. Existing native selection constructor/authorization remains intact. No fake TargetIdentity or SessionPolicy exception for OCR.
- App `ActionResultWindow.cs` only where needed: allow target-specific Apply label for OCR and invalidation behavior. Reuse existing HUD/Markdown/lookup UI and diagnostics.
- App `Resources/Strings.resx` and `Strings.zh-Hant.resx`: paired new UI/errors; preserve opening-language snapshots and custom/user text.
- `tests/SnapBar.Tests/Program.cs`: policy tests with existing Check pattern.
- `tests/SnapBar.Windows.Tests/OcrActionsTest.cs` (new), runner Program.cs and scripts/test-desktop.ps1: register a focused OcrActions suite if existing Ocr suite becomes too large; update existing OCR/Language expectations for More.
- `release-notes/2026-11-08-ocr-shared-actions.json`, `docs/p0-validation.md`: actual delivered behavior and measured evidence/limits.

### Task 1: Shared policy, local tickets and output routing

- [ ] Read current catalog/policies/native custom-action flow and add meaningful Core cases before code: menu combines Bar+More without hidden/copy/cut/paste, invalid links/paths/lookup stay inapplicable, blank successful editor permits no-input scripts, selected/full corrected input, revision change invalidates ticket, guarded replace rejects old range/card and returns correct new text. Verify failing tests through repository's actual runner before implementing.
- [ ] Implement minimal capability/local-ticket policy. Store text/range/revision only in memory; keep immutable invocation input. Text or selection changes invalidate later output side effects, card close/reselect invalidates identity. Existing native authorization must not be loosened.
- [ ] Extract output routing once from SelectionController.CustomActions.cs. Use explicit host callbacks: completion status, clipboard write, guarded replacement with result fallback, result display and diagnostics. Respect None/Clipboard/ReplaceSelection/Hud/Window/Markdown including existing stderr/diagnostic semantics. Do not move native Current/ValidateCustomAsync/lease extension and release out of their existing source adapter.
- [ ] Tests for router must verify actual mode-to-host effects and no side effect on stale host, not just enum property assignments. Use an App test helper if WPF dependency prevents Core tests; no live clipboard in this focused helper. Preserve native deferred Apply validation.
- [ ] Run `pwsh -NoProfile -File scripts/build.ps1` and focused new cases, expected build/Core pass. Commit owned files with `refactor(actions): share routing and local targets` and write task report.
- [ ] Independent task review for spec compliance and code quality; resolve important findings before UI integration. Report concrete signatures and host contracts for Task2.

### Task 2: OCR More, shared actions and local AI target

- [ ] Replace direct Search footer with More, keeping Copy/Reselect. Show only after successful OCR result. Open uses live settings; includes all eligible visible placements with deduplicated copy removed, shared recent ordering/search and accessible keyboard behavior; disabled reasons for inapplicable items. Manage Actions opens existing settings without resetting editor or saved preferences.
- [ ] Preserve editor selection while menu is open/closed. Invoke using current corrected full/selected input; verify action definition still exists, is visible and matches opened definition before executing. Reopening refreshes settings. Remember action through existing persistence, report save failure without claiming success.
- [ ] Wire built-in search/link/path/lookup to existing policy/services and shared result presentation. Revalidate local invocation before destination/Shell effect after awaits. Preserve path prepare/timeout/cancellation and dictionary source grouping/management behavior.
- [ ] Wire scripts through existing CustomActionRunner with exact kind/content/interpreter/UseSelection/timeout. Use Task1 router for all outputs; OCR ReplaceSelection modifies only captured local range/full document using TextBox selection replacement inside BeginChange/EndChange so Undo works and clipboard stays unchanged.
- [ ] Introduce explicit local AI target (text plus CanApply/ApplyAsync/label or equivalent). Reuse current AI settings/client/prompts/result UI. Native constructor keeps SessionPolicy and external controller checks; local target validates card/revision only and shows localized Apply to recognized text. Do not fabricate native snapshot identity.
- [ ] One action at a time per card, progress/cancel feedback, no queued clicks. Edits remain allowed and invalidate previous ticket; cancel/close/reselect disposes owned work and prevents late callbacks writing to a new card. Retain safe result fallback for stale Apply; do not imply script external effects can be undone.
- [ ] Add paired English/Traditional Chinese strings and stable OcrMore, OcrActionSearch, OcrAction_<id>, OcrManageActions, OcrActionStatus/OcrActionCancel IDs. Esc closes menu first and restores editor before card close; menu scrolls in constrained work area.
- [ ] Adapt existing Ocr/Language tests to search via More without weakening blank-input/recognized-text/opening-language checks. Add meaningful focused integration tests for new UI and capture screenshots over disposable fixture backdrops. Run build and relevant focused desktop suites serially; commit `feat(ocr): connect shared actions through more` and report.
- [ ] Independent spec/quality review with local/native target distinction and cancellation audit before final validation.

### Task 3: Cross-source acceptance and delivery evidence

- [ ] Add configured URL test: same action available through native selection and OCR, corrected/full/selected text resolved correctly with injected browser; edit/hide/remove visible consistently, stale menu rejected, settings stored once. Confirm custom names/ordering survive reopening and language change.
- [ ] Add PowerShell/JavaScript interpreter/timeout/input tests and all six output modes through shared router. Verify actual local replacement leaves clipboard intact and Undo restores original, and stale text/selection/card rejects Clipboard/Replace side effects; after cancel/timeout next invocation succeeds.
- [ ] Add controlled AI test using existing mock client: same profile/prompts, valid local Apply, text/range modification/close/reselect rejection, explicit OCR Apply label. Retain existing non-writable/native foreground/candidate/lease rejection regressions.
- [ ] Verify one busy action, duplicate click rejection, menu keyboard/Esc/selection retention, management flow and successful blank editor/no-input action. Capture More, disabled/search, running and applied results in both languages/restricted area as available. Save only disposable fixtures; inspect screenshots for clipping/focus.
- [ ] Add unique Traditional Chinese release note with existing OCR and action-management entry/required script interpreters or AI profile prerequisites. Append docs/p0-validation.md actual counts/report paths/hardware limits; strict UTF-8 readback.
- [ ] Run `pwsh -NoProfile -File scripts/check.ps1`; expected tooling, build and Core pass. Run `pwsh -NoProfile -Command "& ./scripts/test-desktop.ps1 -Suite Ocr,OcrActions,Actions,ActionRunner,AiPanel,Language,Compatibility,Appearance"` after registering OcrActions if created. Use smaller focused reruns while iterating, final suites serially, no full Release claim unless that complete suite actually runs.
- [ ] Commit verified tests/docs/release note as `test(ocr): verify shared action workflows`. Create final whole-change package and independent broad review; fix findings, rerun covering tests, re-review. Mark checklist and durable ledger only after actual verification. Final report links screenshot and development launcher, records no publish/install and remaining hardware gaps.

## Self-review

All approved sections map to tasks: policy/settings/input/output/native safety (1), UI/built-ins/scripts/AI/lifetime/localization (2), cross-source acceptance/documentation (3). Interfaces are deliberately source-aware and use existing runner/services. No schema migration, no second action registry, no fabricated writable native selection, no output-mode duplication. Running App was exited by user; builders remain serial.
