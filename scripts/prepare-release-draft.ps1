<#
.SYNOPSIS
Creates a private formal release draft with validated bilingual update metadata.
.DESCRIPTION
Run before publishing a formal release in GitHub. Review the draft, then publish it
to start the existing verified package/attachment workflow. Existing drafts and
published releases are returned unchanged, including releases predating metadata.
No ZIP, checksum, tag or existing release body is replaced by this command.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [string]$PreviousTag,
    [string]$SourceDirectory = (Split-Path $PSScriptRoot -Parent),
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = $env:GH_REPO
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'auto-release-common.ps1')
. (Join-Path $PSScriptRoot 'release-notes.ps1')
if (-not $Repository) { throw 'Repository is required.' }
$identity = Get-SnapBarVersion $Version
if (-not $identity.IsPublic) { throw 'Formal drafts require a public stable or preview version.' }
$existing = @(Get-SnapBarReleases $Repository | Where-Object { $_.tag_name -ceq $Version })
if ($existing.Count -gt 1) { throw 'Duplicate Release version.' }
if ($existing.Count) {
    if ($existing[0].draft) {
        if ($existing[0].target_commitish -cne $Commit) { throw 'Existing draft belongs to a different source commit.' }
        Assert-SnapBarDraftUpdateMetadata $existing[0] $SourceDirectory $Commit
    }
    Write-Host "Existing release retained unchanged: https://github.com/$Repository/releases/tag/$Version"
    return $existing[0]
}
if ($PreviousTag) { $null = Get-SnapBarVersion $PreviousTag }
$body = New-SnapBarReleaseBody -Root $SourceDirectory -Commit $Commit -PreviousTag $PreviousTag -Version $Version -Repository $Repository -RunId ''
$refs = (Invoke-SnapBarGh @('api', "repos/$Repository/git/matching-refs/tags/$Version")) | ConvertFrom-Json
if (@($refs | Where-Object { $_.ref -ceq "refs/tags/$Version" }).Count) {
    $tagCommit = (Invoke-SnapBarGh @('api', "repos/$Repository/commits/$Version")) | ConvertFrom-Json
    if ($tagCommit.sha -cne $Commit) { throw 'Release tag points to a different source commit; no draft was created.' }
}
# Formal drafts do not claim ownership by an automatic workflow run.
$body = [regex]::Replace($body, '(?m)^<!-- snapbar-run:  -->\r?\n', '')
if ($identity.Channel -eq 'Stable') { $body = $body.Replace('預覽版 / Preview', '正式版 / Stable') }
$temporary = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($temporary, $body, [Text.UTF8Encoding]::new($false))
    $preview = ($identity.Channel -eq 'Preview').ToString().ToLowerInvariant()
    $draft = (Invoke-SnapBarGh @('api', '--method', 'POST', "repos/$Repository/releases",
        '-f', "tag_name=$Version", '-f', "target_commitish=$Commit", '-f', "name=SnapBar $($identity.Version)",
        '-F', "body=@$temporary", '-F', 'draft=true', '-F', "prerelease=$preview")) | ConvertFrom-Json
    Assert-SnapBarDraftUpdateMetadata $draft $SourceDirectory $Commit
    return $draft
}
finally { [IO.File]::Delete($temporary) }
