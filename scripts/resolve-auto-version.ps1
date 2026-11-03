param([string]$Repository = $env:GH_REPO, [string]$RunId = $env:GITHUB_RUN_ID)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'auto-release-common.ps1')
if (-not $Repository) { throw 'Repository is required.' }
$releases = @(Get-SnapBarReleases $Repository)
$refs = (Invoke-SnapBarGh @('api', "repos/$Repository/git/matching-refs/tags/v0.1.0-rc.")) | ConvertFrom-Json
$tags = @($refs | ForEach-Object { $_.ref -replace '^refs/tags/', '' })
return Get-SnapBarAutoVersion $releases $tags $RunId
