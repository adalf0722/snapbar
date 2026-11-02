# Task 5 Report — Fixed Text Service Test Lifecycle

## Files

- Added `src/SnapBar.App/AiServiceTestController.cs` with the fixed request text, busy guard, reset/cancel behavior, revision checks, cancellation-aware waiting, safe status updates, and disposal handling.
- Extended `tests/SnapBar.Windows.Tests/AiServicesTest.cs` with a deferred client and lifecycle checks for fixed request contents, duplicate start, stale completion after reset, replacement success, cancellation of a noncooperative client, failure status without response text, and late completion after disposal.

## RED / GREEN

- RED: `pwsh -NoProfile -File .\scripts\test-desktop.ps1 -Suite AiPanel` failed during build because the new lifecycle tests referenced the not-yet-created `AiServiceTestController`.
- Baseline before controller implementation: the same AiPanel suite passed with 195 checks; desktop preflight passed and no SnapBar App/Probe processes were present at preflight.
- GREEN: `pwsh -NoProfile -File .\scripts\test-desktop.ps1 -Suite AiPanel` passed after implementation: 206 passed, 0 failed. Desktop preflight passed.

## Concerns

- No live API, user configuration, or process termination was used. AiPanel's existing fixture reports foreground-window wait messages while it drives its UI checks; the run completed successfully.
- The controller is intentionally dispatcher-affine, matching the brief; no cross-thread UI dispatch was added.

## Review Fix — Reentrant Cancellation Before Client Invocation

- Added a post-`Changed` guard before `ExecuteAsync`. It verifies the controller is still active, the revision and request CTS still belong to this start, and cancellation has not been requested. Returning through the existing `finally` preserves the rule that only the still-current request can settle its busy state or emit a completion notification.
- Added synchronous `Changed` callback regressions for `Reset`, `Cancel`, and `Dispose`; each asserts that the client receives zero calls and the controller is idle with `untested` status.
- Added the overlap case: start a replacement while the canceled request is unwinding, then verify the old request neither clears `Busy` nor emits a notification or overwrites the replacement status. Also complete the old fake request late and verify the replacement remains pending.
- TDD RED: `pwsh -NoProfile -File .\scripts\test-desktop.ps1 -Suite AiPanel` failed as expected at `synchronous reset prevents service invocation` (45 passed, 1 failed); build and preflight passed.
- TDD GREEN / final verification: `pwsh -NoProfile -File .\scripts\test-desktop.ps1 -Suite AiPanel` — Build exit 0, Preflight exit 0 (`PASS interactive desktop and Ctrl+Alt+V preconditions`), AiPanel exit 0 with 213 passed and 0 failed. Preflight confirmed no SnapBar App/Probe processes were present. Covering test file: `tests/SnapBar.Windows.Tests/AiServicesTest.cs` (controller lifecycle checks run within AiPanel).
- Log review: the seven `WAITING: activate … if necessary (up to 60 seconds)` lines are fixture focus instructions emitted by the UI test; the AiPanel run completed successfully with no `FAIL` or `ERROR` lines. They are not timeout or timing failures.
- No live API was used and no process was force-stopped.
