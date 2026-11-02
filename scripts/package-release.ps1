param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$')]
    [string]$Version,
    [string]$DesktopReport
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'release-common.ps1')
if (-not $DesktopReport) { $DesktopReport = Join-Path $script:SnapBarRepo 'artifacts/checks/desktop/latest.json' }
$fingerprint = Get-SnapBarFingerprint
& (Join-Path $PSScriptRoot 'build.ps1') -Publish -DesktopReport $DesktopReport
Assert-SnapBarDesktopReport $DesktopReport $fingerprint
if ($fingerprint -ne (Get-SnapBarFingerprint)) { throw 'Source changed before release packaging.' }
$output = Join-Path $script:SnapBarRepo ('artifacts/releases/' + [guid]::NewGuid().ToString('N'))
$result = New-SnapBarReleaseArchive -Package (Join-Path $script:SnapBarRepo 'artifacts/p0') -Version $Version -OutputDirectory $output
if ($fingerprint -ne (Get-SnapBarFingerprint)) { throw 'Source changed during release packaging.' }
Write-Host "Release ZIP: $($result.archive)"
return $result
