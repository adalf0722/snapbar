# AI Toolbox Implementation Plan

**Goal:** Deliver the approved two-area toolbox with complete spatial keyboard navigation.

**Architecture:** Keep the current AI session/controller contracts. Isolate toolbox rendering and spatial key handling in an AiPanelWindow partial; the window constructor owns matching area surfaces. Use the existing resources and custom desktop runner.

**Tech Stack:** .NET 10, WPF, PowerShell desktop validation.

- [x] Update `src/SnapBar.App/AiPanelWindow.cs`: matching white padded command/reader cards and gutter, label AI commands, remove nested reader border, preserve header/footer and minimum reader space. Route command rendering to the partial and intercept command-area Esc before the window clear/close path.
- [x] Create `src/SnapBar.App/AiPanelWindow.Commands.cs`: two-column Grid with 8-DIP gaps and 76-DIP tiles; centered icon and two-line name, small pinned indicator, existing pin/options IDs and menu actions. Track each rendered prompt and its row/column. On unmodified arrows move geometrically, clamp incomplete final row, BringIntoView; Enter executes; Esc focuses input. Keep IME and modified key handling unchanged. Restore focus by prompt identity after pinning.
- [x] Update `src/SnapBar.App/Themes/Controls.xaml`: persistent neutral tile fill/outline, hover/focus accent feedback, tool visibility on outer tile focus/hover. Keep existing app-wide Button styles unchanged.
- [x] Add `tests/SnapBar.Windows.Tests/AiToolboxTest.cs` and call it from AiPanelTest. Raise the actual routed keyboard events against focused controls to verify directional movement, row edges, Esc draft retention, Enter execution and busy state, filtered/single/empty collections. Verify two-column geometry, long-label bounds, scrolling to the final command and minimum-size footer visibility. Reuse existing original/result/replacement tests and snapshots.
- [x] Run `pwsh -NoProfile -File scripts/check.ps1`, then `pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite AiPanel`; fix failures and inspect `artifacts/ai-panel/*.png`. Exit the installed app normally before desktop tests.
- [x] Run `pwsh -NoProfile -Command "& './scripts/test-desktop.ps1' -Suite @('Release','Layout')"`, record successful evidence in `docs/p0-validation.md`, publish with `scripts/build.ps1 -Publish -DesktopReport <report>`, and launch the updated package. Keep user configuration and pre-existing changes intact; stage only this task's files.

Validation: check.ps1 passed (tooling 12, Core 389); Release + Layout passed, including AiPanel 80. Desktop report: artifacts/checks/desktop/20261101T093324360Z-d9e82a3240434b87b48144809bbe5e80/report.json. Publication: artifacts/checks/build/20261101T093502525Z-dcf12795ad3c4a3f90605e842dad04a4/report.json. Updated package launched successfully and returned to tray; user AI settings hash unchanged.
