param([string]$Repository = $env:GH_REPO, [string]$RunId = $env:GITHUB_RUN_ID,
    [string]$Commit = $env:GITHUB_SHA, [string]$PreviousTag = $env:SNAPBAR_PREVIOUS_TAG,
    [string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'auto-release-common.ps1')
. (Join-Path $PSScriptRoot 'release-baseline.ps1')
if (-not $Repository) { throw 'Repository is required.' }
$releases = @(Get-SnapBarReleases $Repository)
$refs = (Invoke-SnapBarGh @('api', '--paginate', '--slurp', "repos/$Repository/git/matching-refs/tags/")) | ConvertFrom-Json
$refs = @($refs | ForEach-Object { foreach ($entry in $_) { $entry } })
$tags = @($refs | ForEach-Object { $_.ref -replace '^refs/tags/', '' })
$plan = Get-SnapBarAutoVersion $releases $tags $RunId -VersionPrefix (Get-SnapBarVersionPrefix $Root) -Commit $Commit
if (-not $plan.recovered -or (-not $plan.previousKnown -and $plan.draft)) {
    if ($plan.recovered -and -not $PreviousTag) { throw 'Legacy draft recovery requires an explicit verified PreviousTag.' }
    $plan.previousTag = Get-SnapBarReleaseBaseline -Releases $releases -Root $Root -Commit $Commit -Repository $Repository -ExcludeTag $plan.version -PreviousTag $PreviousTag
}
return $plan
