# Unified Version Implementation Plan

> Execute inline in the current checkout, following the approved unified-version design. User approval to implement was received on 2026-11-16.

**Goal:** Make the downloaded release version match the running App and Probe, preserving main push prereleases.

**Architecture:** A shared C# version parser supplies both Core/UI and PowerShell tools. A root Version.props supplies the development series. The build receives the selected version; reusable metadata checks validate staging and ZIP contents against the workflow version and source SHA. Release ancestry selection is separate from version allocation.

**Tech Stack:** .NET 10, WPF, PowerShell 7, GitHub Actions, existing console test runners.

## Tasks

- [x] Add Version.props, shared ProductVersion parser and classifier, and regression cases for valid and invalid versions. Use the parser through Add-Type in scripts/version-common.ps1.
- [x] Pass Version and SourceRevisionId through build.ps1 and both publish operations. Check App/Probe ProductVersion in staging and ZIP before replacing or publishing packages. Exercise real compiled metadata, mismatched identities, missing and duplicate ZIP entries.
- [x] Generalize auto-release version allocation to the configured series. Recover and validate the existing run first; block new rc allocation after stable publication. Select and persist a deterministic previous release using Git ancestry, with explicit PreviousTag for ambiguity.
- [x] Update workflow verification and publisher entry points to call the common package validator. Validate manual Release channel flags; preserve existing attachment and checksum protections. Test publisher writes through the existing gh fake.
- [x] Show version and localized channel on the support page and bilingual website. Preserve full informational version in diagnostics. Extend appearance and language checks.
- [x] Add Traditional Chinese release notes and update development and validation records. Run the targeted scripts, scripts/check.ps1, desktop Release suite and a self-contained package check. Record unavailable external verification accurately.

## Validation commands

```powershell
pwsh -NoProfile -File scripts/test-version.ps1
pwsh -NoProfile -File scripts/test-auto-release.ps1
pwsh -NoProfile -File scripts/test-site.ps1
pwsh -NoProfile -File scripts/check.ps1
pwsh -NoProfile -File scripts/test-desktop.ps1 -Suite Release
```

Do not push or create a public Release as part of local validation. Keep existing published downloads and notes unchanged.
