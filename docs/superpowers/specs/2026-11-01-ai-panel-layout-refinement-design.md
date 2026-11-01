# AI panel layout refinement

Approved by the user with “go” after review of real original/result screenshots.

- Keep the existing two-column workflow and source safety. At the standard 800 × 650 window with default five commands and collapsed filter, every command must fit completely in the list viewport.
- Input starts at two lines (at least 60 DIP), grows naturally for wrapped/multiline text up to five lines / 120 DIP, and then scrolls. Tighten category/list spacing while keeping 40-DIP command hit targets and all commands searchable.
- Reserve a 28-DIP basis row and the 2-DIP progress strip so input position stays stable before, during and after execution. Keep draft-driven input growth intentional.
- Result metadata shows the executed saved command name or “自訂指令”, never an excerpt of its raw instruction. Capture the saved name at submission and retain it in memory with the matching version, bounded to the session's 20 retained versions. Full instruction and basis remain available in the owned details dialog. Hide history arrows when only one version exists.
- Original/result footer text matches the active view, including readonly and invalidated targets. Preserve foreground, selection and clipboard revalidation without relaxing invalidation.

Validate native geometry (all five buttons inside the viewport and stable input position), input growth, saved/free instruction labels, history controls and view-specific status. Reuse original replacement/stale-request checks, inspect normal/minimum screenshots, run check plus Release/Layout, and publish through the existing fingerprint gate. Do not modify user AI settings or pre-existing uncommitted action-management changes.

## Approved copy clarity follow-up

User requested “繼續” after review of the refined panel. Keep the layout. Label the processing basis “下次改寫”, name the shown-version source switch “改用第 n 版” and the original switch “改用原文”. Keep the basis single-line with a full tooltip so simultaneous source switches cannot wrap it and move the input. Replace the document-only details icon with the quiet text action “指令詳情”. Explain readonly sources as “此處無法直接替換，請複製結果。” with matching original-view wording. No session, safety or persistence changes; verify through existing native regressions and screenshots.
