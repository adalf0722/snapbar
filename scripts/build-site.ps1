param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Repository = $env:GH_REPO,
    [string]$SourceDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'site')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'site-common.ps1')
$releases = @(Get-SnapBarWebsiteReleases $Repository)
$download = New-SnapBarWebsite -SourceDirectory $SourceDirectory -OutputDirectory $OutputDirectory -Releases $releases -Repository $Repository
Write-Host "Website generated for published release $($download.Version): $($download.DownloadUrl)"
if ($env:GITHUB_STEP_SUMMARY) {
    $summary = "Website downloads: [$($download.Version)]($($download.ReleaseUrl))`n"
    [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $summary, [Text.UTF8Encoding]::new($false))
}
