param(
    [Parameter(Mandatory)][ValidatePattern('^v0\.1\.0-rc\.[1-9][0-9]*$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [string]$PreviousTag,
    [string]$Repository = $env:GH_REPO,
    [ValidatePattern('^[0-9]+$')][string]$RunId = $env:GITHUB_RUN_ID
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'auto-release-common.ps1')
if (-not $Repository -or -not $RunId) { throw 'Repository and workflow run ID are required.' }
$archive = Join-Path $PackageDirectory "SnapBar-$Version-win-x64.zip"
$checksum = "$archive.sha256"
foreach ($path in @($archive, $checksum)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing release file: $path" }
}
$expected = (Get-Content -LiteralPath $checksum -Raw -Encoding utf8).Split(' ')[0]
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $expected) { throw 'Automatic release ZIP checksum mismatch.' }
$releases = @(Get-SnapBarReleases $Repository)
$existing = @($releases | Where-Object { $_.tag_name -ceq $Version })
if ($existing.Count -gt 1) { throw 'Duplicate Release version.' }
if ($existing.Count) { Assert-SnapBarAutoRelease $existing[0] $Version $Commit $RunId }
else {
    $refs = (Invoke-SnapBarGh @('api', "repos/$Repository/git/matching-refs/tags/$Version")) | ConvertFrom-Json
    if (@($refs | Where-Object { $_.ref -ceq "refs/tags/$Version" }).Count) { throw 'Release tag was reserved by another publisher. Rerun the entire workflow to select a new version.' }
    $arguments = @('api', '--method', 'POST', "repos/$Repository/releases/generate-notes", '-f', "tag_name=$Version", '-f', "target_commitish=$Commit")
    if ($PreviousTag) { $arguments += @('-f', "previous_tag_name=$PreviousTag") }
    $generated = (Invoke-SnapBarGh $arguments) | ConvertFrom-Json
    $template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../packaging/AUTO-RELEASE-NOTES.md'), [Text.Encoding]::UTF8)
    $notes = $template.Replace('{version}', $Version).Replace('{commit}', $Commit).Replace('{run}', $RunId).Replace('{changes}', $generated.body)
    $notesPath = Join-Path $PackageDirectory 'release-notes.md'
    [IO.File]::WriteAllText($notesPath, $notes, [Text.UTF8Encoding]::new($false))
    $null = Invoke-SnapBarGh @('release', 'create', $Version, '--repo', $Repository, '--target', $Commit, '--title', "SnapBar $Version", '--draft', '--prerelease', '--notes-file', $notesPath)
}
$release = Get-SnapBarReleaseByVersion $Repository $Version
Assert-SnapBarAutoRelease $release $Version $Commit $RunId
$uploads = @(Get-SnapBarAssetUploads @($release.assets) @($archive, $checksum))
if (-not $release.draft -and $uploads.Count) { throw 'Published automatic Release is incomplete; refusing to change public downloads.' }
foreach ($file in $uploads) {
    $null = Invoke-SnapBarGh @('release', 'upload', $Version, $file, '--repo', $Repository)
}
$release = Get-SnapBarReleaseByVersion $Repository $Version
Assert-SnapBarAutoRelease $release $Version $Commit $RunId
if (@(Get-SnapBarAssetUploads @($release.assets) @($archive, $checksum)).Count) { throw 'Release assets are incomplete; leaving draft unpublished.' }
if ($release.draft) {
    $refs = (Invoke-SnapBarGh @('api', "repos/$Repository/git/matching-refs/tags/$Version")) | ConvertFrom-Json
    if (@($refs | Where-Object { $_.ref -ceq "refs/tags/$Version" }).Count) {
        $tagCommit = (Invoke-SnapBarGh @('api', "repos/$Repository/commits/$Version")) | ConvertFrom-Json
        if ($tagCommit.sha -cne $Commit) { throw 'Release tag points to a different commit; draft remains unpublished.' }
    }
    $null = Invoke-SnapBarGh @('release', 'edit', $Version, '--repo', $Repository, '--draft=false', '--prerelease', '--latest=false')
}
Write-Host "Published automatic prerelease: https://github.com/$Repository/releases/tag/$Version"
