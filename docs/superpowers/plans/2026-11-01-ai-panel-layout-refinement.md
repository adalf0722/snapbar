# AI Panel Layout Refinement Plan

**Goal:** Fix the three approved layout issues and view-specific status without changing source safety.

**Architecture:** Keep the version-label cache inside AiPanelWindow; Core session and HTTP/controller contracts remain unchanged. Use WPF MinLines/MaxLines for bounded input growth.

- [x] Update `src/SnapBar.App/AiPanelWindow.cs`: 28-DIP basis row, reserved progress strip, two-to-five-line input, tighter group/list gaps and a named list viewport; retain 40-DIP buttons.
- [x] Capture prompt display names at submission, prune names with evicted/branched versions, render friendly metadata and put basis in the instruction details dialog; hide single-version arrows. Match footer wording to original/result views.
- [x] Extend `tests/SnapBar.Windows.Tests/AiPanelTest.cs` with meaningful geometry, input-growth, metadata/history and status checks. Preserve existing replacement and stale-request tests; inspect refreshed snapshots.
- [x] Run `scripts/check.ps1` and desktop `Release` plus `Layout` after normal App exit. Commit only owned changes, append validation, verify UTF-8 and publish using the successful current-fingerprint report. Launch the new package and preserve user configuration.

Desktop evidence: `artifacts/checks/desktop/20261101T010246578Z-1bd685290c6a406891bcf68d03d61288/report.json` (all pass, AI panel 35). Publication: `artifacts/checks/build/20261101T010418548Z-31b119044c86459281a74c7dc17f4c36/report.json` (Core 389, Probe smoke 10). Updated package launched and returned to tray; no user AI configuration changed.
