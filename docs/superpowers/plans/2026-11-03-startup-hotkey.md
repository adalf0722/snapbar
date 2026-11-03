# Startup and Hotkey Implementation Plan

> Execute inline in the existing checkout; preserve unrelated AI settings work. The user approved the design and its three review corrections on 2026-11-03.

**Goal:** Add truthful login startup control and transactional global shortcut editing to General settings.

**Architecture:** Core owns shortcut validation and schema 7 migration. Windows owns startup registration and reversible hotkey registration. App owns capture, status feedback and silent background startup.

**Tech Stack:** .NET 10, WPF, Win32 RegisterHotKey, HKCU Run, existing console runners.

## Tasks

- [x] Core: create `src/SnapBar.Core/HotkeySettings.cs`, update `SettingsStore.cs`; validate full combinations, recover malformed shortcut fields independently, back up original bytes before explicit repair. Add migration, preservation and roundtrip checks in `tests/SnapBar.Tests/SettingsStoreTests.cs` and `HotkeySettingsTests.cs`. Run `pwsh -NoProfile -File scripts/build.ps1`; expect build and Core checks to pass.
- [x] Windows: create `StartupRegistration.cs` and `GlobalHotkey.cs`; read actual Run and system approval state, write only owned Run value, leave approval records unchanged. Prepare new hotkey before persistence; commit after save or release on failure. Integrate `InputMonitor.cs`; retain native selection safety.
- [x] App: expand `GeneralSettingsPage.cs`, add `MainWindow.General.cs`, integrate `MainWindow.cs`, `MainWindow.Settings.cs`, `SelectionController.cs` and `Program.cs`. Capture Ctrl/Alt/Shift combinations with preview, save/cancel/reset; suppress shortcut actions throughout editing and end on deactivation/navigation. Initialize hidden background instance using a native handle, surface settings on tray failure; duplicate background launch does not reveal settings.
- [x] Windows verification: add isolated startup and real native hotkey checks plus General editing and hidden startup checks to Lifecycle runner. Test occupied registration, unchanged old ownership on failed persistence, key recording cancellation and restart. Isolate registry under a unique test root; do not change real login settings.
- [x] Run `pwsh -NoProfile -File scripts/check.ps1` then relevant desktop Lifecycle/Appearance/Keyboard suites. Inspect produced General screenshots and record results and untested real login in `docs/p0-validation.md`. Verify modified Markdown using strict UTF-8 decoding and `git diff --check`.

## Review

All approved behaviors map to a task above. Startup refresh includes activation and explicit changes. Shortcut recovery preserves other preferences; UI registration and persistence use the same commit boundary. No publishing or changes to the user's actual startup preference are part of validation.
