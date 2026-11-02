# GitHub Release Implementation Plan

**Goal:** Publish a tested, self-contained Windows x64 ZIP as a GitHub Release asset.

**Architecture:** Reuse the existing desktop evidence and publish gates on a dedicated interactive Windows runner. Transfer verified packages to a hosted runner with narrowly scoped Release write permission.

**Tech Stack:** PowerShell 7, .NET 10, GitHub Actions, GitHub CLI.

## Tasks

- [x] Add `scripts/release-common.ps1` with version, required-file, ZIP structure and checksum validation. Add fixture-based behavior checks in `scripts/test-tooling.ps1` for invalid versions, missing files, successful packaging and unchanged existing output.
- [x] Add `scripts/package-release.ps1 -Version v1.2.3` to invoke the existing gated publisher, then create the verified archive in a unique artifacts directory. Preserve the current source fingerprint checks.
- [x] Add `.github/workflows/release.yml`: published event, read-only self-hosted build/test/package job, artifact transfer and hosted upload job with contents write. Keep tag data in environment variables, persist no checkout credentials, preserve existing assets, and retain logs on failure.
- [x] Update `README.md` for public downloads and `docs/development.md` for runner setup, release operation, retries and unsigned package limitations.
- [x] Run `pwsh -NoProfile -File scripts/check.ps1`, validate workflow with actionlint, run `scripts/test-desktop.ps1 -Suite Release` and `scripts/package-release.ps1 -Version v0.0.0-local` when the desktop is available. Report any external setup still required.

Execution stays in this chat under the user's existing approval. Existing unrelated UI changes remain untouched; no commit or public release is required by this task.
