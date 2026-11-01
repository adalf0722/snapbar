# AI prompt editor refinement

Approved by the user with “請繼續” after the three-part editor proposal.

Keep the main panel and service configuration. Show name and instruction first, with “直接描述處理方式，選取文字會自動帶入。” Category, search keywords and icon move into a collapsed “更多設定（選填）” section. New commands default to category 自訂 and the existing default icon; editing preserves hidden values. Use shared vector icon previews with Chinese labels, preserving unknown existing identifiers until explicitly changed.

The instruction editor fills remaining normal-window space and scrolls internally. The form's minimum height follows its measured header/optional section plus a 140-DIP editor minimum; an outer scroll is needed only when the expanded form or small window cannot fit. Footer remains outside that scroll. Keep raw instruction text, validation and safe persistence.

- [x] Implement the form in `src/SnapBar.App/AiPromptEditorWindow.cs`, including field identifiers, required-field validation and initial name focus.
- [x] Extend native AI tests for collapsed default, complete normal view, small-window reachability, advanced options/icons, exact save and hidden-value preservation, error/cancel behavior and unchanged provider configuration after saving prompts. Use isolated settings only.
- [ ] Inspect editor snapshots, run check and desktop Release/Layout, publish with successful current-fingerprint evidence and launch the updated package. Verify UTF-8 documentation and preserve pre-existing uncommitted changes.

Service-state read-only audit: current user ai.json is readable schema 1 with two profiles; selected profile has a model ID. No endpoint, model value, prompt text or secret was printed. Baseline hash recorded in this session for final preservation verification; do not rewrite user AI settings to address the earlier screenshot.
