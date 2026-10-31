# Whitespace preview refinement

The user approved this design in chat on 2026-10-31 after reviewing the current editor screenshot.

- Add a compact, initially unchecked Show whitespace checkbox beside the result heading. It appears after a successful script test, including tests without selection input. Toggle the existing result display between its raw text and visible whitespace; keep the captured raw output unchanged and never rerun the script when toggling.
- Symbol mode and both comparison boxes preserve actual line boundaries with no soft wrapping and horizontal scrolling. Spaces, tabs, CR, LF, and CRLF remain distinguishable. CR-only lines retain their line boundaries too.
- Keep input/output comparison initially collapsed and move its count/change summary inside the expansion. The default result view shows a single output, with the comparison available when explicitly opened.
- Keep the existing Use selection setting authoritative. Turning it off hides the sample and comparison; no script inspection or automatic rewriting of saved actions is introduced. An IP lookup can use this existing setting.
- Clear the captured display state and reset the checkbox when editing, changing kind, beginning another test, or presenting failure. Do not expose the checkbox for URL previews or failed execution.
- Preserve diagnostics, clipboard behavior, selection validation, and concurrent edits already in the checkout. Publish the review build to an isolated artifact folder if the normal published executable is running.

Verify native editor toggling without another execution, exact restoration of raw Unicode output, CR/LF/CRLF rendering, long lines without artificial wrapping, initially collapsed comparison, and hidden sample/comparison with selection disabled. Inspect WPF screenshots at normal and minimum editor size.
