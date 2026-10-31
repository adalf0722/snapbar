# Automatic Compatibility Implementation Plan

> Execute inline in the existing checkout. User approved the preceding automatic compatibility design. Preserve the pending UI changes.

**Goal:** Use verified clipboard fallback when native selection is unavailable and remember compatibility choices.

**Architecture:** Extend ProbeResponse with a bounded candidate token, implement focus validation in the isolated Probe, reuse ClipboardSelectionReader through a generic adapter, and keep unverified writes disabled. Persist mode flags in schema 6.

**Tech Stack:** .NET 10, WPF, Win32 clipboard/input, UI Automation, custom console runners.

- [x] Core: schema 6 migration, default-enabled compatibility and nullable legacy Sublime preference; candidate contract and copied-selection metadata policy with behavior tests. Generic source is also denied write permission independently of editability.
- [x] Probe: issue candidates only for verified focused non-password ancestry; validate identity, focus and expiration before re-copy. No fallback on explicit empty selection or failures. Automatic pointer capture must be inside the focused control bounds.
- [x] Windows/App: generic Ctrl+C plan with terminal exclusion and copied-data validation; capture/revalidation for built-ins and custom actions; retain Sublime write checks and reject generic writes. Parse bounded Chromium Pickle metadata in addition to direct editor flags.
- [x] Settings UI: saved general mode and Sublime preference, legacy valid binding restoration, startup validation without installation, explicit setup on user toggle. Default window height 820 keeps new explanations and footer visible.
- [x] Verification: independent unsupported-provider fixture and integration runner; run Core 292, appearance 60, lifecycle 20, layout 13, clipboard roundtrip 13, races 17, generic compatibility 20, native UIA 4 and custom actions 65 (504 total), plus six Probe smoke checks. Publish passed without warnings/errors. Actual UI off/on saved schema 6; normal restart restored both compatibility flags, with unchanged Sublime keymap hash. Record limits and strict UTF-8 verification in p0-validation.md.

### Review corrections

- [x] Synchronize compatibility toggles through Checked/Unchecked, prevent reentry and initialization writes; test real IToggleProvider calls with isolated Sublime setup dependencies. Lifecycle 28 checks include disabled controller state, persisted preferences, no duplicate setup and failed setup rollback.
- [x] Extend a verified candidate lease for admitted script timeout plus validation grace, release on every exit, retain focus/text/password validation; test a 35-second script and source-focus change during execution. Compatibility integration 22 checks passed, including real elapsed time across the prior deadline.
- [x] Run relevant runners and expanded protocol smoke cases; publish and record evidence. Core 292, lifecycle 28, compatibility 22, appearance 60, diagnostics 25 (427 total), plus 10 Probe smoke checks. Final build/publish passed with zero warnings/errors.
