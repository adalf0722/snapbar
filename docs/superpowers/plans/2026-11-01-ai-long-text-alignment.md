# AI long-text alignment

User approved the reading-layout correction with “go”. Keep panel layout, typography and text content. Set explicit left alignment on the AI FlowDocument so viewer initialization cannot apply justified spacing. Preserve returned paragraph/Markdown structure and raw copy/apply output.

- [x] Add native long-English, Chinese and multi-paragraph Markdown regression cases in `tests/SnapBar.Windows.Tests/AiPanelTest.cs`. Inspect effective alignment after template/layout and verify final text is reachable at scroll end; save full-size and minimum-size evidence. Reproduce the alignment failure before the correction.
- [x] Set explicit `TextAlignment.Left` in `src/SnapBar.App/AiMarkdownView.cs`; retain safe rendering, 15-DIP text and 24-DIP line height. Do not change unrelated custom action rendering.

Red reproduction: `artifacts/checks/desktop/20261101T012336984Z-85b5045b12d34956abb2eb9ab0bf17bb/report.json` fails the new effective left-alignment assertion after viewer template initialization, with 30 earlier checks passing.
- [ ] Run check and desktop Release/Layout, inspect long-text screenshots, publish using matching fingerprint evidence and launch the updated package. Preserve user AI settings and existing uncommitted changes; verify Chinese Markdown as UTF-8 without BOM.
