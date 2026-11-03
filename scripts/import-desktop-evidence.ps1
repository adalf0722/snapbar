param([string]$NotesRef = 'refs/notes/snapbar-desktop')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'desktop-evidence.ps1')
$commit = Get-SnapBarCommit 'HEAD'
Assert-SnapBarCleanCommit $commit
$tree = & git -C $script:SnapBarRepo rev-parse 'HEAD^{tree}'
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve checkout tree.' }
$json = & git -C $script:SnapBarRepo notes "--ref=$NotesRef" show $commit
if ($LASTEXITCODE -ne 0) { throw 'No desktop evidence for this commit. Push it with the installed pre-push hook first.' }
$receipt = ($json -join "`n") | ConvertFrom-Json
Assert-SnapBarDesktopReceipt $receipt $commit "$tree"
$run = New-SnapBarRun 'desktop-import'
# The exact clean commit/tree binds both checkouts, independent of Git CRLF checkout settings.
# Retain the original receipt separately; authorize this checkout using its local byte fingerprint.
Write-SnapBarJson (Join-Path $run 'receipt.json') $receipt
$report = $receipt.report
$report.sourceFingerprint = Get-SnapBarFingerprint
$path = Join-Path $run 'report.json'
Write-SnapBarJson $path $report
Assert-SnapBarDesktopReport $path $report.sourceFingerprint
Write-Host "Verified pre-push desktop evidence for $commit"
return $path
