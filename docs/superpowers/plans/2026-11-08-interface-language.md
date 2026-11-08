# SnapBar Interface Language Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Deliver a complete Traditional Chinese / English desktop experience, preserve existing preferences, and provide English download and startup instructions.

**Architecture:** Core owns language preference normalization, schema migration, and stable validation codes. Windows provides an independent system UI language source; App owns paired resource dictionaries, scoped language selection, display-only bundled item names, and UI refresh without rebuilding controllers or drafts. Existing editor/result windows retain their opening language; settings, tray, and subsequent surfaces use the current language.

**Tech Stack:** .NET 10, C#, WPF, Windows Forms NotifyIcon, .resx / ResourceManager, existing console test runners, PowerShell 7, static HTML.

**Approved specification:** `docs/superpowers/specs/2026-11-08-interface-language-design.md` (including the approved review changes).

**Global constraints:** Reuse this checkout. Do not modify selection safety, clipboard leases, action permissions, user text, scripts, credentials, OCR priorities, or AI instructions. Do not launch a user's installed app, kill their running instance, push, or publish a release. Maintain UTF-8 without BOM. Keep AutomationId values and test fixture contracts stable. Assign one owner per file; parent delivery work may proceed independently of the desktop implementer. Builds and desktop tests run serially; review desktop behavior before integrated verification.

## Task 1: Desktop bilingual behavior

**Files:**

- Create `src/SnapBar.Core/InterfaceLanguage.cs` for stable values `system`, `zh-Hant`, `en` and pure resolution.
- Modify `src/SnapBar.Core/SettingsStore.cs` to schema 8 with legacy defaults and independent preservation of unrelated fields.
- Modify `src/SnapBar.Core/CustomActions.cs`, `ActionCatalog.cs`, `AiSettings.cs`, and `AiPromptCatalog.cs` where product validation messages reach UI; expose stable error codes and arguments without changing checks.
- Create `src/SnapBar.Windows/SystemUiLanguage.cs` for the user's Windows UI language, independent of App CurrentUICulture.
- Create `src/SnapBar.App/Resources/Strings.resx` and `Strings.zh-Hant.resx` with English neutral fallback; add the neutral-language assembly attribute.
- Create focused App localization helpers for explicit culture lookup, window scopes, metadata refresh, and bundled item display names. Use stable resource keys, never Chinese sentences as keys or translation matching on arbitrary user text.
- Modify all production App surfaces with product text: Action UI/editors/results/settings, AI panels/editors/settings/errors, icon picker, exclusions, dictionary/OCR, MainWindow, popup, tray, diagnostics, and selection status messages. Fixture text is intentionally unchanged.
- Extend `tests/SnapBar.Tests/SettingsStoreTests.cs`, add `InterfaceLanguageTests.cs`, and register it in `Program.cs`.
- Add `tests/SnapBar.Windows.Tests/InterfaceLanguageTest.cs` and its runner flag; retain legacy tests under an explicit Traditional Chinese test culture so unrelated expected labels remain stable.

- [x] **1. Add behavior tests before changing the implementation.** Cover missing settings (system), valid schema 1–7 without language (zh-Hant), schema 8 roundtrip, unknown preference fallback, future schema refusal, and unrelated hotkeys/actions preservation. Check `InterfaceLanguage.Resolve("system", "zh-TW") == "zh-Hant"`, `Resolve("system", "fr-FR") == "en"`, and `Resolve("en", "zh-TW") == "en"`. Use the existing `Check` pattern and disposable temp paths; no third-party framework.
- [x] **2. Run the Core console runner and confirm the new tests fail for the intended missing behavior.** Use `.tools/dotnet/dotnet.exe run --project tests/SnapBar.Tests -c Release`; use PATH dotnet only if the bundled SDK is absent.
- [x] **3. Implement language policy, settings migration and independent OS source.** Determine old/new status from the actual JSON schema/field presence, not an object initializer that cannot distinguish missing fields. Continue refusing writes after unreadable/corrupt/future settings. Save language through the existing atomic SettingsStore path before changing visible language.
- [x] **4. Build paired resources and convert product-owned UI text.** Use `ResourceManager.GetString(key, culture)` with explicit culture. Product formatted messages use resources with positional parameters; preserve user/external strings as values. Convert Core validation to code + arguments and map at App boundaries rather than comparing localized exception messages. Render built-in names by stable ID; render bundled editable items by ID plus original-name equality. Editor saves must not materialize a translated default name as a user rename.
- [x] **5. Add `語言 / Language` selection to GeneralSettingsPage.** Options are `跟隨系統 / System`, `繁體中文`, `English`. Add stable AutomationId values for selector/status. Update MainWindow labels, control item collections, tooltips, automation names and tray in place. Retain selected category, all draft values, scroll, and controller identity. If saving fails, restore the selection and show an error in the previous language.
- [x] **6. Scope independent windows.** Capture opening language in editor/result windows, including callbacks/async status. Keep existing windows and work intact on language change. A newly created window uses the new language; reused hidden surfaces must refresh only when safe. System→English→System must query the independent OS source again.
- [x] **7. Add resource and desktop tests.** Compare exact bilingual resource key sets and positional format signatures without ResourceManager fallback concealing missing keys. Verify no untranslated product labels in representative English settings/action/AI/OCR/dictionary surfaces. Exercise bundled defaults, renamed items, persisted preferences, save failure, draft retention, opening-window language, tray refresh, and controller preservation. Add screenshot output under `artifacts/ui-polish/language-*.png` using the existing screenshot helper.
- [x] **8. Build and run targeted checks.** Run `scripts/build.ps1`, Core runner, and the new isolated language runner. Do not change Windows display language. Resolve all failures caused by schema changes or new resource access. Record exact commands/results.
- [x] **9. Self-review and commit desktop implementation.** Inspect resource gaps, accidental localization of user text, static cached labels, event subscription lifetime, thread/dispatcher access, and changed safety conditions. Use a focused Conventional Commit such as `feat(i18n): add bilingual desktop interface`.

## Task 2: Desktop task review

- [x] **1. Review Task 1 against every approved spec section and the diff.** Use a separate reviewer, checking both correctness and requirement coverage, particularly validation errors, bundled edits, scopes, save failures, and safe UI refresh.
- [x] **2. Correct important findings, rerun affected tests, and review corrections.** Do not defer desktop coverage gaps as completed work.

## Task 3: English download and package experience

**Files:** `README.en.md`, `packaging/README.en.md`, `README.md`, `packaging/README.md`, `site/index.html`, `site/en/index.html`, `site/download.js`, `site/style.css`, `scripts/build.ps1`, `scripts/release-common.ps1`, package tooling tests, `release-notes/2026-11-08-interface-language.json`, `docs/development.md`.

- [x] **1. Inspect the actual shipping features and prerequisites.** Document supported Windows/x64, extraction and startup, language selector, shortcuts, close-to-tray versus Quit, optional dictionary import, optional AI credentials, and runtime prerequisites for custom actions. Do not advertise retired translation features or imply Node.js is needed for ordinary Copy/Search.
- [x] **2. Add English repository and package quick starts.** Preserve Chinese instructions and link the language alternatives. The package English quick start must work without navigating source code or developer setup.
- [x] **3. Add a static English landing/download page and visible language links.** Reuse existing styles, screenshots and download resolution logic. Ensure links are relative to the English subdirectory correctly and all user-visible download labels/failures are English on that page. Use shared behavior and locale-specific strings, not a second divergent downloader.
- [x] **4. Include English documentation in normal build and publish output.** Copy `packaging/README.en.md` to output `README.en.md` alongside the unchanged Chinese `README.md`. Add required-file checks for `README.en.md` and `zh-Hant/SnapBar.App.resources.dll` in package verification; update package fixtures and tests to require both.
- [x] **5. Add the unique Traditional Chinese release note.** Required fields: `title`, `summary`, 1–3 `highlights`, `usage`. Describe how to switch and explain the behavior of already-open windows. Do not edit published notes.
- [x] **6. Review links and validate scripts.** Run `pwsh -NoProfile -File scripts/check.ps1`; run release tooling tests if touched. Verify normal build output includes both documentation and localized assembly.
- [x] **7. Commit delivery changes after self-review.** Use `feat(i18n): add English download instructions`.

## Task 4: Integrated verification and final review

**Files:** `scripts/test-desktop.ps1`, `scripts/desktop-evidence.ps1` if needed for suite registration; `docs/p0-validation.md`; this checklist.

- [x] **1. Register language validation in the desktop suite catalog and release evidence contracts.** Keep required suite lists and tooling tests consistent; do not weaken existing release requirements.
- [x] **2. Run relevant desktop regressions and the full Release suite before producing a local package.** Use the official test entry point, confirm no user App/Probe instance is running, and inform the user before tests use the foreground/clipboard. Record failures and fix them without bypassing evidence gates.
- [x] **3. If desktop Release succeeds, produce a fresh package through the official build/publish path.** Unpack into a new artifacts directory, verify English instructions and localized assembly, and exercise bilingual resource lookup from the extracted package with independent culture input. Do not replace artifacts/p0 on failed gates or claim an untested package is verified.
- [x] **4. Inspect bilingual screenshots and record actual DPI and coverage.** Add concrete results to `docs/p0-validation.md`; explicitly record any environment limitations rather than marking checks passed.
- [x] **5. Request a broad final code review and fix important findings.** Include migration, Core/App localization boundaries, user content preservation, window scopes, ZIP contents, site paths, tests and security invariants.
- [x] **6. Verify Markdown as strict UTF-8 without BOM and run `git diff --check`.** Commit final validated corrections and reports. Report implementation, exact checks, artifact paths, and any remaining limitation; no push or remote release.
