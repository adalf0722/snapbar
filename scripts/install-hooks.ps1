$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$existing = & git -C $script:SnapBarRepo config --get core.hooksPath
if ($existing -and $existing -ne '.githooks') { throw "Existing core.hooksPath '$existing' is preserved; integrate .githooks/pre-commit and pre-push manually." }
foreach ($name in @('pre-commit', 'pre-push')) {
    $legacy = & git -C $script:SnapBarRepo rev-parse --git-path "hooks/$name"
    if (-not [IO.Path]::IsPathRooted($legacy)) { $legacy = Join-Path $script:SnapBarRepo $legacy }
    if (-not $existing -and (Test-Path -LiteralPath $legacy)) { throw "Existing hook '$legacy' is preserved; integrate the project checks manually." }
}
& git -C $script:SnapBarRepo config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw 'Hook installation failed.' }
Write-Host 'Installed pre-commit checks and pre-push Release desktop tests with commit evidence upload.'
