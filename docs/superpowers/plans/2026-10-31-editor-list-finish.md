# Editor and Action List Finish Implementation Plan

> Execute inline in the current checkout; user approved the preceding UI review. No additional worktree or unrelated changes.

**Goal:** Expose the basic editor controls and make the existing sorting interaction discoverable.

**Architecture:** Keep the existing WPF windows and persistence contracts. Add a styled resize Thumb and a visible drag grip; remove duplicate presentation only.

**Tech Stack:** .NET 10, C#, WPF, existing console test runners.

- [x] Editor: replace fixed 184 DIP height with a 120 DIP default, clamp drag/keyboard resizing to 96–480 DIP, Home resets; add focus and hover feedback in Controls.xaml. Reduce form field spacing while preserving text and control sizing.
- [x] Action list: remove duplicate group heading and built-in subtitles; add the six-point grip in ActionUi.cs and ActionSettingsWindow.cs; use the system drag threshold and retain arrow sorting and persistence. Capture mouse until the threshold to support fast cross-row motion.
- [x] Verification: extend the existing appearance checks for actual viewport containment, resizing bounds/content preservation and group navigation. Run build.ps1, UI appearance and action-layout runners, inspect generated screenshots, then publish. Core 282, appearance 58, layout 13, diagnostics 25; six Probe smoke checks. Native preview confirmed resizing and fast cross-row dragging; capture fix verified.
- [x] Delivery: record evidence in docs/p0-validation.md, verify Markdown UTF-8 without BOM and git diff --check, refresh the published validation copy and start the updated app after normal exit with no unsaved editor open. Automatic detection loaded normally; Sublime clipboard compatibility remains explicit session opt-in.
