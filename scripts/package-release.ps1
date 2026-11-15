param(
    [Parameter(Mandatory)]
    [string]$Version,
    [string]$DesktopReport,
    [string]$SourceCommit
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'release-common.ps1')
if (-not $DesktopReport) { $DesktopReport = Join-Path $script:SnapBarRepo 'artifacts/checks/desktop/latest.json' }
$fingerprint = Get-SnapBarFingerprint
$identity = Get-SnapBarVersion $Version
if (-not $SourceCommit) { $SourceCommit = (& git -C $script:SnapBarRepo rev-parse HEAD) }
& (Join-Path $PSScriptRoot 'build.ps1') -Publish -DesktopReport $DesktopReport -Version $identity.Version -SourceCommit $SourceCommit
Assert-SnapBarDesktopReport $DesktopReport $fingerprint
if ($fingerprint -ne (Get-SnapBarFingerprint)) { throw 'Source changed before release packaging.' }
$output = Join-Path $script:SnapBarRepo ('artifacts/releases/' + [guid]::NewGuid().ToString('N'))
$result = New-SnapBarReleaseArchive -Package (Join-Path $script:SnapBarRepo 'artifacts/p0') -Version $Version -OutputDirectory $output -Commit $SourceCommit
if ($fingerprint -ne (Get-SnapBarFingerprint)) { throw 'Source changed during release packaging.' }
Write-Host "Release ZIP: $($result.archive)"
return $result
