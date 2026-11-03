# AI Services Settings Implementation Plan

> Execute inline against the approved specification in `docs/superpowers/specs/2026-11-03-ai-services-settings-design.md`. Preserve the existing checkout and user preferences.

**Goal:** Make connection setup, saved credentials, test results and draft persistence clear in the WPF settings page.

**Architecture:** Keep preferences, scope isolation and the service-test controller unchanged. Extract presentation into an `AiSettingsPanel` partial, add explicitly keyed styles, and extend the existing fixture runner for the new credential interaction.

**Tech Stack:** .NET 10, WPF, custom console/desktop test runners, PowerShell 7.

## 1. Layout and scoped styles

- [x] Extract control construction from `AiSettingsPanel.cs` into `AiSettingsPanel.Presentation.cs`.
- [x] Use a padded card for fields, header-row links, a quiet expander, and fixed footer with dirty-state-dependent actions.
- [x] Add `AiSettingsLink` and `AiSettingsExpander` styles to `Themes/Controls.xaml`, preserving keyboard focus and existing global control styles.
- [x] Clarify global detection text in `MainWindow.cs`.

## 2. Credential and test presentation

- [x] Render stored-key summary plus explicit replacement action, with no secret prefill. Render pending replacement/removal separately. Catch expected protected-store read errors with safe text.
- [x] Keep key controls in the tree for existing automation; hide unused controls, restore editor focus on replacement, and preserve scope/draft semantics.
- [x] Update `AiSettingsPanel.Testing.cs` to show cancellation only while busy and place result next to the test action. Use success text with a check mark.
- [x] Preserve reset-on-edit, cancellation, explicit paid-request intent and test success across save.

## 3. Focused behavior verification

- [x] Extend `AiServicesTest.cs` to exercise stored-key replacement/focus, clean versus dirty actions, pending removal, scope changes and cancellation visibility. Update existing success-text assertions.
- [x] Keep all endpoint isolation, persisted key roundtrip, partial failure/retry and stale result assertions.
- [x] Add snapshots for normal, advanced/minimum and saved-key states and the embedded settings page.

## 4. Build and desktop verification

- [x] Run `pwsh -NoProfile -File scripts/check.ps1`; expect successful tooling checks, Release build and Core runner.
- [x] Inspect running SnapBar windows; exit normally before desktop tests without killing processes or saving drafts.
- [x] Run the selected suites serially from PowerShell:

```powershell
& ./scripts/test-desktop.ps1 -Suite AiRuntime,AiPanel,Appearance,Lifecycle
```

- [x] Expect a successful preflight and zero failures in all suites; inspect the generated report and relevant PNGs.
- [x] Record actual DPI and limits in `docs/p0-validation.md`; do not claim synthetic or unperformed physical DPI testing.

## 5. Delivery

- [x] Read changed Chinese Markdown with strict UTF-8 and verify no BOM; run `git diff --check`.
- [x] Report the implemented behavior, verification and a representative screenshot. Keep publishing out of scope.
