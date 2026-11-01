# AI Panel Layout Refinement Plan

**Goal:** Fix the three approved layout issues and view-specific status without changing source safety.

**Architecture:** Keep the version-label cache inside AiPanelWindow; Core session and HTTP/controller contracts remain unchanged. Use WPF MinLines/MaxLines for bounded input growth.

- [ ] Update `src/SnapBar.App/AiPanelWindow.cs`: 28-DIP basis row, reserved progress strip, two-to-five-line input, tighter group/list gaps and a named list viewport; retain 40-DIP buttons.
- [ ] Capture prompt display names at submission, prune names with evicted/branched versions, render friendly metadata and put basis in the instruction details dialog; hide single-version arrows. Match footer wording to original/result views.
- [ ] Extend `tests/SnapBar.Windows.Tests/AiPanelTest.cs` with meaningful geometry, input-growth, metadata/history and status checks. Preserve existing replacement and stale-request tests; inspect refreshed snapshots.
- [ ] Run `scripts/check.ps1` and desktop `Release` plus `Layout` after normal App exit. Commit only owned changes, append validation, verify UTF-8 and publish using the successful current-fingerprint report. Launch the new package and preserve user configuration.
