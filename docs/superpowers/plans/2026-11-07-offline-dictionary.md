# Offline Dictionary Implementation Plan

**Goal** Add offline lookup to the selection toolbar with a user-imported dictionary.

**Architecture** Core reads bounded StarDict data and stores one validated local snapshot. The app manages imports in settings and routes lookup through the existing selection validation and result window.

**Tech stack** .NET 10, WPF, StarDict 2.4.2, console test runners.

- [x] Implement the StarDict parser and atomic snapshot in `src/SnapBar.Core/OfflineDictionary.cs`. Use bounded reads for source and decompressed data, validate UTF-8 and index ranges, and resolve exact terms before English inflection candidates.
- [x] Wire import and removal in `src/SnapBar.App/MainWindow.Dictionary.cs` and `GeneralSettingsPage.cs`. Wire the built-in lookup through `ActionCatalog.cs` and `SelectionController.cs`, and reuse `ActionResultWindow.cs` for the anchored copyable result.
- [x] Exercise fixture lookup, storage reload, and failed replacement in the Core runner. Run `pwsh -NoProfile -File .\scripts\check.ps1` and verify the actual dictionary and desktop workflow where available.
