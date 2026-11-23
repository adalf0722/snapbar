# Update Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Deliver the approved update notification experience in `docs/superpowers/specs/2026-11-23-update-notifications-design.md`.

**Architecture:** Core owns semantic ordering, validated release metadata, candidate policy, and atomic local state. Windows retrieves bounded public GitHub release pages. App coordinates cancellable checks, deferred notifications, and persistent bilingual UI; release scripts generate the same machine-readable summaries as the human release notes.

**Tech Stack:** .NET 10, C#, WPF/WinForms NotifyIcon, HttpClient, System.Text.Json, PowerShell 7, custom console test runners.

## Task 1: Core contracts, policy, and persistence

Files: modify `src/SnapBar.Core/ProductVersion.cs`; create `src/SnapBar.Core/UpdatePolicy.cs`, `src/SnapBar.Core/UpdateStateStore.cs`, `tests/SnapBar.Tests/UpdatePolicyTests.cs`; register in `tests/SnapBar.Tests/Program.cs`.

- [x] Add meaningful failing checks for rc.9/rc.10 ordering, stable/preview transitions, malformed metadata, duplicate versions, docs-only releases, independent download/notification candidates, state roundtrip and preservation of corrupt/newer files.
- [x] Implement `ProductVersion : IComparable<ProductVersion>` using arbitrary-length numeric identifiers and SemVer prerelease ordering.
- [x] Define these public contracts for the following tasks:

```csharp
public sealed record UpdateText(string[] Summaries, string[] Notices);
public sealed record UpdateMetadata(int SchemaVersion, string Version, string SourceCommit,
    bool DesktopChanged, UpdateText Zh, UpdateText En);
public sealed record UpdateRelease(string Version, UpdateMetadata? Metadata);
public sealed record UpdateCandidates(UpdateRelease? Latest, UpdateRelease? Notification);
```

`UpdatePolicy.ReadMetadata(body, version)` validates the unique base64 comment, UTF-8, schema, SHA and text limits. `UpdatePolicy.Select(current, releases, notified)` filters public versions by channel, rejects duplicates, and selects independently the highest newer package and highest unnotified desktop update.

- [x] `UpdateState` stores schema 1, enabled preference, UTC attempt/success/retry timestamps, 64 notified versions, latest candidate and notification candidate. `UpdateStateStore.Load()` returns defaults or protected state; expose `Writable`; `Save(state)` atomically validates and writes at most 64 KiB without changing `settings.json`.
- [x] Run `.tools/dotnet/dotnet.exe run --project tests/SnapBar.Tests -c Release`; fix failures, inspect spec compliance, then code quality.

## Task 2: Bounded GitHub source

Files: create `src/SnapBar.Windows/GitHubUpdateSource.cs`, `tests/SnapBar.Windows.Tests/UpdateNotificationTest.cs`; register `--updates` in `tests/SnapBar.Windows.Tests/Program.cs`.

- [x] Use injectable HttpClient and fixture tests for successful public lists, draft/incomplete assets, malformed entries, duplicate versions, >100 results, cancellation, response size, page cap, malicious Link headers, 403/429 and Retry-After.
- [x] Implement a disposable source with `Task<IReadOnlyList<UpdateRelease>> ReadAsync(CancellationToken)`; production client has redirects disabled. Send fixed User-Agent, GitHub JSON Accept and supported API version. Limit every response to 8 MiB, the whole operation to 30 seconds and ten pages of 100 entries.
- [x] Accept only same-origin fixed repository pagination. Require fully uploaded correctly named ZIP and checksum; construct release URLs from validated versions. Expose a typed rate-limit error with UTC RetryAt. No real network in tests.
- [x] Run the new runner via `.tools/dotnet/dotnet.exe tests/SnapBar.Windows.Tests/bin/Release/net10.0-windows/SnapBar.Windows.Tests.dll --updates` after build.

## Task 3: App coordinator, busy snapshot, and UI

Files: create `src/SnapBar.App/UpdateCoordinator.cs`, `src/SnapBar.App/UpdateSettingsPanel.cs`, `src/SnapBar.App/MainWindow.Updates.cs`; modify `MainWindow.cs`, `MainWindow.Settings.cs`, `SelectionController.cs`, `TrayIcon.cs`, `SettingsSupportPage.cs`, both `Resources/Strings*.resx`.

- [x] Coordinator accepts source, UTC clock, busy predicate, notification callback and state store; expose state/status changes and manual check. Start only public versions; wait 30 seconds before automatic checks, then poll every five seconds with persisted 24-hour throttling and minimum 60-second manual cooldown.
- [x] Cancel request generations on disabling automatic checks or disposal; check generation again before state mutation. Persist attempts before network and notified versions before calling notification. Save failure suppresses automatic notification, preserving manual UI. Failed checks retain cached candidates and success timestamp; successful checks clear withdrawn candidates.
- [x] Add a boolean busy snapshot covering capture, clipboard, pending appearance/autocopy, popup, action execution, AI and OCR. Do not invalidate or probe selections to decide notification eligibility.
- [x] Update settings panel shows current state, auto-check checkbox, manual check, up to three localized summaries, notices, exact release target and install instructions. Refresh dynamic text on language changes. Use a ScrollViewer so the support page remains usable on small windows.
- [x] Tray uses explicit notification kind/target, supports a persistent update item, and routes notification clicks to the exact cached release. Preserve existing hidden-notice click behavior. On close/dispose cancel work and detach handlers.
- [x] Fixture tests cover clock-driven throttling, busy deferral, cross-launch dedupe, late results, save failures, cancellation, language refresh, manual failure/status and safe targets. Confirm no production network in desktop fixtures.

## Task 4: Publish metadata

Files: modify `scripts/release-notes.ps1`, publication entry points as necessary, `scripts/test-release-notes.ps1` and release tests.

- [x] Compute desktop change classification before the notes branch, using existing delivered-desktop path policy. Add structured summaries/notices to the release-content object without changing existing rendering.
- [x] Emit `<!-- snapbar-update-v1: BASE64 -->` in newly generated bodies. JSON camelCase keys match Core records. Include all required notices and validate contract size/content before draft creation. Preserve old public bodies and existing rerun behavior.
- [x] Ensure formal publication also generates/validates contract for new drafts while grandfathering existing drafts and public releases. Keep ZIP/checksum evidence intact.
- [x] Run `pwsh -NoProfile -File scripts/test-release-notes.ps1` and `scripts/test-auto-release.ps1`; add fixtures for document-only commits, desktop changes, metadata roundtrip and reruns.

## Task 5: Integration and delivery evidence

Files: modify `scripts/common.ps1`, `scripts/test-desktop.ps1`, `docs/development.md`, `packaging/README.md`, `packaging/README.en.md`; create `release-notes/2026-11-23-update-notifications.json` and validation record.

- [x] Add Updates to required Release desktop suite and document its fixtures/behavior. Add a bilingual user release note with one to three highlights and install instructions.
- [x] Run `pwsh -NoProfile -File scripts/check.ps1`; run Updates, Language, Lifecycle and SupportRecovery desktop suites, and broaden to Release when desktop access is available. Keep evidence honest if any prerequisite prevents execution.
- [x] Inspect UI with screenshots using the fixture and verify readable zh-Hant/en layouts at narrow and normal sizes. Record screenshots and actual check results. Verify changed Chinese Markdown strictly decodes as UTF-8 without BOM.
- [x] Review complete diff for spec coverage then correctness; fix actionable issues. Commit only task-owned files using Conventional Commits. Do not publish or push without user instruction.

## Execution outcome

Implementation and individual suites completed. Repository checks pass. Full Release was attempted three times but remains blocked by CompatibilitySlow clipboard-sequence or foreground-fixture failures; neither safety checks nor release evidence requirements were relaxed. Baseline and changed versions both pass the isolated 43-check CompatibilitySlow suite. See the validation record for exact reports. No package was published.
