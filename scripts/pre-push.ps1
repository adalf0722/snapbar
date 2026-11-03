param([Parameter(Mandatory)][string]$Remote)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'desktop-evidence.ps1')
$lines = [Console]::In.ReadToEnd() -split '\r?\n'
$commits = @(Get-SnapBarPushCommits $lines)
if (-not $commits.Count) { exit 0 }
foreach ($commit in $commits) { Assert-SnapBarCleanCommit $commit }
Write-Host 'Push gate: exit SnapBar normally; avoid typing or copying during the full Release desktop tests.'
& (Get-Command pwsh).Source -NoProfile -File (Join-Path $PSScriptRoot 'test-desktop.ps1') -Suite Release
if ($LASTEXITCODE -ne 0) { throw 'Release desktop tests failed; push blocked.' }
$receipt = New-SnapBarDesktopReceipt $commits[0] (Join-Path $script:SnapBarRepo 'artifacts/checks/desktop/latest.json')
Send-SnapBarDesktopReceipt $Remote $receipt
Assert-SnapBarCleanCommit $commits[0]
Write-Host 'Release desktop tests passed and commit evidence uploaded. Continuing push.'
