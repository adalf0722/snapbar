# Workflow Validation Implementation Plan

**Goal:** Make workflow lint mandatory and support a complete release rehearsal without publishing.

**Architecture:** Extend the existing check entry point with pinned actionlint and index snapshots. Share package and transferred-artifact verification jobs between release and manual events, with a separate release-only write job.

**Tech Stack:** PowerShell 7, Git index, actionlint 1.7.12, GitHub Actions.

## Tasks

- [x] Add `scripts/workflow-checks.ps1` for verified tool provisioning and workflow snapshots; extract index blob reads into `scripts/common.ps1` and reuse them from `scripts/check.ps1`.
- [x] Add isolated-index regression tests to `scripts/test-tooling.ps1` for staged errors, unstaged repairs, invalid context expressions and staged custom runner labels.
- [x] Update `.github/workflows/release.yml` with manual dispatch, shared read-only verification, release-only upload and Actions summary. Document operation in `docs/development.md`.
- [x] Run full repository checks and hook tests, push changes, run a manual rehearsal with the online desktop runner, and inspect jobs, artifacts and unchanged Release/tag inventory.

Execute in this chat under the user's approved retro scope. Do not create another Release or alter the desktop safety gates.
