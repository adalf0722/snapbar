# AI panel visual integration

User approved this design with “同意,go” after reviewing the three-part proposal.

## Scope

Keep the approved two-column workflow, combined search/instruction input, original/result switching, prompt visibility, version history, and selection safety. Integrate the panel with SnapBar's existing action settings visual system.

## Visual rules

- Use the shared canvas, ink, accent, rounded controls and 24-DIP outer inset. The left rail uses the canvas; the right reader remains a white rounded card. Keep a 280-DIP rail and usable minimum 680 × 500 window.
- Use the same visual treatment as ActionPlacementTab for original/result, while retaining the existing button semantics and keyboard behavior. Increase the title to 24 DIP and reader text to 15 DIP with approximately 24-DIP line height and 20-DIP insets.
- Display a compact provider/settings entry, capped at 180 DIP; keep the full provider/model in its tooltip and settings dialog.
- Prompt names stay visible. Use shared vector icons for pin/options. Reveal row management controls on hover or keyboard focus without shifting the name; retain a small pin indicator for saved pins. Context menus remain accessible.
- Remove the repeated result heading. Combine version navigation, processing basis and instruction access into a bounded single information row. Full instructions remain available in the owned scrollable dialog.
- Put source status and result actions in one footer row. Copy is the accent action for readonly/invalidated result sources; replace is the accent action when safe replacement is available. Original copy remains secondary. Preserve visible focus and high-contrast button text.

## Verification and delivery

Use existing AI runtime/panel and Release desktop runners, checking regular/minimum original and result screenshots plus long-instruction layout. Verify keyboard access to row management and that readonly copy and editable replacement have the correct emphasis. No real API calls or test values in user settings. Publish through the current source fingerprint gate, normally closing the running package first. Preserve pre-existing uncommitted action-management changes.
