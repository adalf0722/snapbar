# Toolbar Appearance Delay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the approved adjustable automatic toolbar appearance delay with precise synchronized controls and persistence.

**Architecture:** Keep existing capture delays and auto-copy sequencing. Measure elapsed time from the selection event, wait only for the remaining display delay, and revalidate delayed selections before presentation. Use a focused Core policy and App partials to separate persistence, editing, and asynchronous presentation.

**Tech Stack:** .NET 10, WPF, custom console test runners, PowerShell 7.

## Global Constraints

Read `AGENTS.md` and `docs/superpowers/specs/2026-11-18-toolbar-appearance-delay-design.md` as the authoritative requirements. Work in the existing checkout on a `codex/` branch. Do not publish or push. Markdown is UTF-8 without BOM. Preserve password, foreground/focus, candidate identity, clipboard sequence and cancellation safeguards. No selected text in logs. This is one coordinated task because persistence, editing, scheduling and cancellation form one feature.

### Task 1: Implement and verify adjustable automatic appearance delay

**Files:**
- Create: `src/SnapBar.Core/ToolbarAppearanceDelay.cs`, `src/SnapBar.App/GeneralSettingsPage.AppearanceDelay.cs`, `src/SnapBar.App/SelectionController.AppearanceDelay.cs` and `src/SnapBar.App/MainWindow.AppearanceDelay.cs`.
- Modify: `src/SnapBar.Core/SettingsStore.cs`, `src/SnapBar.App/MainWindow.cs`, language-refresh integration, `src/SnapBar.App/SelectionController.cs`, `src/SnapBar.App/Resources/Strings.resx`, `src/SnapBar.App/Resources/Strings.zh-Hant.resx`.
- Test: focused new Core policy/settings test registered in `tests/SnapBar.Tests/Program.cs`; focused new desktop test integrated with the existing GeneralSettings runner and appropriate selection/auto-copy runner.
- Document: new unique Traditional Chinese release note; validation record in `docs/p0-validation.md`.

- [ ] Step 1: Add meaningful Core tests for exact remaining-time arithmetic, default and bounded persistence, legacy missing field and preserving unrelated preferences. Implement this policy surface, using a monotonic timestamp supplied from the event:

```csharp
public static class ToolbarAppearanceDelay
{
    public const int DefaultMilliseconds = 120;
    public const int MaximumMilliseconds = 2000;
    public static int Normalize(int milliseconds) => Math.Clamp(milliseconds, 0, MaximumMilliseconds);
    public static TimeSpan Remaining(int milliseconds, TimeSpan elapsed) =>
        TimeSpan.FromMilliseconds(Math.Max(0, Normalize(milliseconds) - Math.Max(0, elapsed.TotalMilliseconds)));
}
```

Add `ToolbarAppearanceDelayMilliseconds` initialized to the policy default to `ApplicationSettings` and normalize on load/save without changing schema 8. Run the existing Core runner with the repository-selected SDK. Cases include `Remaining(300, 180ms) == 120ms`, `Remaining(300, 450ms) == 0`, zero, boundaries, and JSON save/load.

- [ ] Step 2: Build the synchronized controls as a focused `GeneralSettingsPage` partial. Use range 0–2000, slider drag ticks 10, keyboard increments 1, exact integer textbox, reset 120, Enter/focus commit and Escape rollback. Save slider drag only on release and debounce keyboard changes; keep no-op saves quiet. Persist before updating active preferences, restore on failure, expose accessible error/status and stable automation IDs. Handle text-to-slider focus transition without committing an abandoned invalid draft. Disable with automatic appearance off, retain value, cancel pending edit/debounce on unload, and handle language reconstruction.

- [ ] Step 3: Connect page initialization, language reconstruction, persistence and controller initialization using existing `ConfigureAutoCopy`/`SaveAutoCopy` patterns. Add both resource languages with text explicitly defining zero as no additional delay; do not promise bypassing the existing capture wait.

- [ ] Step 4: Record a monotonic timestamp and saved delay for each automatic selection at input handling before dispatch. Preserve existing pre-capture waits, auto-copy and all direct paths. Delay only presentation by `ToolbarAppearanceDelay.Remaining(savedDelay, elapsed)`, with cancellation. Keep pending snapshots out of visible-popup keyboard/pointer behavior. Show auto-copy feedback before the new display wait without duplicate feedback or feedback timeout cancellation. For delayed selections revalidate source focus and source-specific candidate identity/content, avoiding clipboard write races or new side effects. Guard generation/token/automatic/target after every await. No lease extension.

- [ ] Step 5: Add desktop tests for precise values, invalid/empty input, Enter/focus/Escape, slider sync and keyboard, defaults, reset, failure rollback, persisted reopen, disabled controls, bilingual layout. Add behavior coverage with reasonable timing tolerances for non-additive delay, cancellation/reselection/foreground/focus, pending popup pointer/keyboard state, slow capture, immediate auto-copy feedback and unaffected shortcuts. Reuse the existing fixture harness and never operate on user content.

- [ ] Step 6: Run `pwsh -NoProfile -File scripts/check.ps1`, then relevant desktop suites as selected from `scripts/test-desktop.ps1 -List`. Check existing App/Probe processes before desktop testing and exit normally using existing supported behavior if necessary; never force-kill user processes. Capture a settings screenshot with fixture preferences and record actual test results and limitations. Add a unique `release-notes/2026-11-18-toolbar-appearance-delay.json` following the repository schema, including prerequisites and how to start. Validate changed Markdown as strict UTF-8 without BOM and run `git diff --check`.

- [ ] Step 7: Self-review the complete diff against the approved spec, fix discovered issues, commit the feature using Conventional Commits, and write a report with files changed, exact test commands/results, screenshots and remaining limitations. Do not push or publish. Report back for independent task and whole-change review.
