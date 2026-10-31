# Custom action diagnostics

Approved in chat on 2026-10-31: explain a failure briefly, retain the original diagnostics behind Details, and present them where the action was run.

- The editor displays diagnostics inline. Toolbar failures open a persistent, nonmodal diagnostic window with Details, Copy details, and Close. Diagnostic windows never offer Apply to original text.
- Both surfaces share one formatter and view. Known noninteractive PowerShell failures explain that input cannot be requested; a verified `curl` command also suggests `curl.exe`. Unknown failures receive an honest generic summary with the raw cause available in Details.
- Details include action name, runtime kind, status, exit code when available, stderr, and stdout. No diagnostic data is written to logs or copied automatically.
- Keep each captured stream bounded to 262,144 characters. Preserve captured text on timeout and overflow, identify the truncated stream, and label incomplete capture when a pipe cannot be drained after termination. Cancellation and stale requests remain suppressed in the toolbar.
- Preserve password rejection, foreground and selection validation, cancellation, job-based descendant cleanup, and existing successful output routing. Stderr alone does not turn a successful process into a failure; the editor still makes those diagnostics available.

Alternatives considered: a transient status alone is insufficient for long errors; automatically writing log files adds storage and privacy costs without helping the immediate workflow.

Validation: real PowerShell terminating errors, Unicode stderr, nonzero exit with both streams, noninteractive input without network access, partial output before timeout, bounded overflow, and the existing descendant-cleanup tests. Native WPF checks cover collapsed details, copy availability, absence of Apply, editor failures staying inline, and shared content. Record results in `docs/p0-validation.md`.
