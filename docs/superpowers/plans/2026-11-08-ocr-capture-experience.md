# OCR Capture Experience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Deliver frozen-screen OCR selection, immediate processing feedback, recoverable cancellation/timeout, and a nearby editable result card.

**Architecture:** Windows.Ocr owns disposable monitor snapshots, crop encoding and recognition. App coordinates one capture session and displays overlays and one processing/result window. A short-lived App worker entry point isolates native OCR when its cancellation contract cannot guarantee termination.

**Tech Stack:** .NET 10, C#, WPF, System.Drawing, Tesseract 5.2.0, existing console test runners and PowerShell desktop scripts.

**Approved specification:** `docs/superpowers/specs/2026-11-08-ocr-capture-experience-design.md` (including the user's approved Musk review corrections).

## Global constraints

Reuse `D:/codex/snapbar`; create a `codex/` branch before implementation. Preserve all selection safety, persisted preferences, OCR language ordering, existing dependencies and local-only recognition. Do not log user text or pixels, change the installed package, publish, push, close or kill the user's running SnapBar. The installed App was initially running from Downloads; the user exited it normally before desktop validation. Markdown and JSON must be UTF-8 without BOM.

Use sequential implementers and task reviews. Build/test commands run serially. Only terminate OCR worker processes created and owned by this work. All changed production strings have English and Traditional Chinese resources. Record actual results and limitations rather than claiming unavailable desktop/DPI evidence.

## File map and interface contracts

- `src/SnapBar.Windows.Ocr/OcrSnapshot.cs`: owns one monitor Bitmap; exposes `Bounds`, `Capture(ScreenRect)`, `CopyBgraPixels()` and `CropPng(ScreenRect)`, plus `Dispose()`. Physical coordinates must be integral, finite, contained and checked before allocating; cropped region keeps existing 4 × 4 and 16,000,000 pixel limits. Full-screen capture has a separate checked allocation policy and supports one 8K screen.
- `src/SnapBar.Windows.Ocr/OcrWorker.cs`: worker entry and bounded anonymous-stdio protocol; parent client owns process lifetime. `RecognizeAsync(string executable, byte[] png, bool preferEnglish, CancellationToken token, TimeSpan? timeout = null)` returns existing `OcrResult`, default timeout 15 seconds. Cancellation throws `OperationCanceledException`; timeout returns status `timeout`; launch/protocol failure returns a content-free failure status. Child `RunAsync(bool preferEnglish)` reads bounded image input, calls existing recognition, writes bounded JSON only to stdout, and exits without settings/tray/hooks/mutex. No screenshots or results in temporary files, command line, or stderr logs.
- `src/SnapBar.App/Program.cs`: intercept exact `--ocr-worker` / `--ocr-worker --english` arguments before single-instance setup; call worker entry, return exit status.
- `src/SnapBar.App/OcrOverlay.cs`: display frozen BitmapSource and exclude selection rectangle from the dark layer; show one centered help banner on active monitor, hidden while dragging; emit clamped region or cancellation.
- `src/SnapBar.App/OcrResultWindow.cs`: processing/result state on the same window, accessible IDs, direct Copy/Reselect/Search, scrollable editable text, opening-language snapshot, error/timeout recovery and keyboard cancellation.
- `src/SnapBar.App/MainWindow.Ocr.cs`: snapshot/session/worker coordination, guarded callbacks, state ownership and DPI-aware card placement. Keep one-argument `ShowOcrResult(OcrResult)` available for existing language tests or update its test callers together. Locate packaged worker explicitly alongside App, not `Environment.ProcessPath` (tests launch under a different executable).
- `src/SnapBar.App/Resources/Strings.resx` and `Strings.zh-Hant.resx`: paired capture/result strings; preserve existing resource keys when practical.
- `tests/SnapBar.Windows.Tests/ScreenOcrTest.cs`: real frozen-source integration, asynchronous readiness, current card structure, screenshots and performance measurements.
- `tests/SnapBar.Windows.Tests/InterfaceLanguageTest.cs`: adapt OCR callback checks to disabled empty-search behavior and new window without weakening the opening-language contract.
- `release-notes/2026-11-08-ocr-capture-experience.json` and `docs/p0-validation.md`: user-facing change and actual validation evidence.

### Task 1: Frozen capture and bounded recognition lifetime

- [x] Record the pre-change OCR baseline using the existing runner if no installed instance is active; otherwise explicitly defer live baseline. Inspect Tesseract calls: current cancellation checks surround `engine.Process`/`page.GetText`, so they do not promise interruption inside native work. Use the isolated worker path if that remains the available contract.
- [x] Add meaningful backend checks to the existing OCR test runner (or a focused helper invoked by it): crop of known snapshot keeps original pixels, negative-coordinate mapping is correct, too-small/out-of-bounds/oversized selections are rejected before allocation, disposed snapshots reject use, and input/output protocol sizes are bounded. Do not create tests merely checking property assignments.
- [x] Implement snapshot ownership and crop API above. Convert a full screen to BGRA at most once for WPF display, freeze the resulting BitmapSource; do not PNG-encode a monitor every time the mouse moves. Dispose cropped/native buffers on every success and failure path.
- [x] Implement worker entry/client if native interruption is unavailable. Start a hidden redirected child with `ProcessStartInfo.ArgumentList`, not a shell. Bound stdin PNG and stdout JSON, preserve language preference, suppress native content logging. The timeout covers start, transfer and recognition; link cancellation, kill only the owned child on cancellation/timeout, observe pipe tasks and wait for exit before releasing the busy lease. Handle caller cancellation separately from timeout.
- [x] Ensure parent exit cannot orphan an owned worker, using a kill-on-close Job Object or an equivalent tested parent-lifetime mechanism; never kill processes discovered by name. Test cancellation during transfer and stalled native work with a disposable test worker.
- [x] Build with `pwsh -NoProfile -File scripts/build.ps1`. Expected: Release build and Core checks pass. If an installed process blocks only desktop suites, do not terminate it. Commit task-local files with `feat(ocr): freeze captures and bound recognition`.
- [x] Review Task 1 for spec and code quality; resolve important findings before UI integration.

### Task 2: Continuous capture and result experience

- [x] Implement overlay using Task 1 snapshot API. Preserve per-monitor physical positioning, reversed drags, lost-capture recovery, Esc and right-click. Selection shows original pixels at original brightness; dark mask has a rectangular hole. Keep a single fixed banner on the current monitor and no cursor-following tooltip. Min-size threshold is physical pixels, line widths are DIP.
- [x] Build `OcrResultWindow` with one processing/error/result body. Set processing text immediately, show an indeterminate indicator after a configurable 300 ms initial delay, respecting reduced motion. Transition in place and re-clamp window bounds when it grows; cancel timers on close.
- [x] Result card starts at 420 × 300 DIP, minimum 320 × 220 limited by work area; use editable scrollable text and Copy/Reselect/Search, plus standard close/Esc. Update copy label based on selection, show a configurable 1.5-second success confirmation, disable empty copy/search, preserve content on failure. Search uses existing policy and opening-language messages.
- [x] Add paired language resources, accessible names and stable `OcrCaptureHelp`, `OcrStatus`, `OcrEditor`, `OcrCopy`, `OcrReselect`, `OcrSearch`, `OcrCancel` IDs. Use existing theme controls; do not implement a new visual system.
- [x] Change orchestration to hide owned transient surfaces, await repaint, capture monitors before showing overlays, crop the displayed snapshot and start bounded worker. A generation/session object owns all disposable state. Before showing results verify identity, cancellation and exit flags. Closing an old window or completing an old task must not clear a newer session.
- [x] Place processing/result card near selection lower-right with 12 DIP gap, try above then clamp to target work area; convert using that monitor's DPI, handle negative coordinates. Reselect uses a fresh desktop snapshot and leaves the old result closed. Cancel/timeout frees worker and allows the next capture; no automatic clipboard writes or action replay.
- [x] Update OCR integration expectations for asynchronous overlay readiness and new controls; retain existing recognition correctness. Build with the repository build script. Commit with `feat(ocr): connect capture progress and result card`.
- [x] Review Task 2 against approved specification and lifetime/concurrency contract; fix important issues before final validation.

### Task 3: Recovery, measurement and delivery evidence

- [x] Add integration cases with a changing fixture: capture snapshot, mutate source, select original text, assert original frozen text is recognized. Restore source before a second capture and verify reselect obtains a new snapshot.
- [x] Exercise delayed/stalled worker cancellation and timeout, assert owned PID exits and a subsequent capture succeeds. Cover repeated trigger, stale completion, close/exit cleanup, empty result, cropped-region rejection, clipboard failure and selected-text copy. Preserve and restore original clipboard when tests intentionally exercise copying.
- [x] Adapt language tests to new controls while asserting recognized content remains unchanged and callbacks retain opening-language errors; use invalid nonempty text to test search validation now that empty search is disabled.
- [x] Save visual evidence through existing `UiAppearanceTest.Snapshot`: overlay, processing, editable success and empty result. Use screenshots only of disposable fixtures. Inspect images for clipping, focus and card placement. Record which DPI/work-area/multimonitor environments actually ran.
- [x] Measure one cold and five warm fixture runs: trigger-to-ready, release-to-first-feedback, release-to-result, parent/worker peak memory and screen/DPI/selection dimensions. Store only numeric metrics and fixture identifiers; do not log recognized text. Mark missing baseline or 4K/8K environments unmeasured. Use a Stopwatch from the trigger and a ready-state signal, not arbitrary fixed sleeps as the measurement endpoint.
- [x] Add a unique Traditional Chinese release note describing frozen capture, progress/recovery and copy/reselect; usage mentions existing OCR hotkey/tray entry and VC++ x64 runtime prerequisite. Update `docs/p0-validation.md` with test results, report paths and limitations, then strictly decode both changed Markdown files as UTF-8 without BOM.
- [x] Run `pwsh -NoProfile -File scripts/check.ps1`. Expected: encoding, syntax, tool contracts, Release build and Core tests pass. Then run `pwsh -NoProfile -Command "& ./scripts/test-desktop.ps1 -Suite Ocr,Language,Appearance"` only after installed SnapBar has exited normally. Expected: desktop preflight plus all three suites pass. Do not publish or use this limited run as full Release evidence.
- [x] If the user's installed instance is still active, finish code and non-desktop checks, report the exact blocker and request normal exit; keep desktop evidence explicitly pending. Do not claim full delivery until required validation is resolved.
- [x] Commit verified task files with `test(ocr): verify recovery and capture experience`, generate whole-change review package, perform final independent review, fix findings and repeat only affected checks. Update task checkboxes and report actual completion state.

## Self-review

Specification sections map to Tasks 1–3: snapshots and termination (1), interaction/localization/positioning (2), acceptance/performance/release evidence (3). One-argument language-test seam and packaged worker entry are explicit integration points. Timers are adjustable defaults, not measured optimal values. Live baseline and desktop validation are conditional on normal exit of the installed instance; no process interference is authorized.

## Completion

Completed on `codex/ocr-capture-experience`. Backend `dc7c220` and UI `3064220` passed independent task reviews. Validation `6b91a07` and additional acceptance checks `a29c3d4` passed whole-change review and scoped re-review with no remaining findings. Final evidence: repository checks/Core510, backend21, OCR36, Language50, Appearance174, Preflight1; detailed report paths and measurement limits are in `docs/p0-validation.md`. Screenshots inspected, UTF-8 verified, no publish or installed-package replacement performed.
