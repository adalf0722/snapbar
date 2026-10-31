# Development Guardrails Implementation Plan

> Execute inline in the existing checkout. User approved high and medium retrospective candidates.

**Goal:** Make fast checks automatic, desktop verification discoverable, and publishing fail safely.

**Architecture:** Share SDK selection, source fingerprinting, stage logs and publish checks in scripts/common.ps1. Keep CI headless; require current interactive test evidence for package promotion. Extract candidate expiration policy into Core with TimeProvider.

**Tech Stack:** .NET 10/WPF, PowerShell 7, Git hooks, GitHub Actions.

- [x] Core clock policy and deterministic boundary tests; separate slow compatibility mode.
- [x] Shared scripts, UTF-8/index checks, script contract tests, GitHub workflow and installed project hook.
- [x] Desktop CLI suites, fresh fixture paths, preflight, unique logs and JSON results.
- [x] Early process guard, source-bound desktop release gate, staging publication and rollback.
- [x] Update AGENTS/development navigation; verify encoding and record positive/negative guard checks.
- [x] Final source-matched Release desktop run: Lifecycle 28, Appearance 60 and CompatibilitySlow 22 passed with the user keeping the desktop idle. Report: artifacts/checks/desktop/20261031T134518370Z-4ab6e2f624d94879afedaf1966241b4c/report.json.
- [x] Promoted the fresh package after 10 Probe smoke checks. Previous package retained at artifacts/publish/f78cd4a213c848669498e47f53625ab9/previous. New app started with existing preferences and returned to tray operation.
