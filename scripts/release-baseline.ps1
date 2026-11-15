. (Join-Path $PSScriptRoot 'version-common.ps1')
. (Join-Path $PSScriptRoot 'release-notes.ps1')

function Test-SnapBarReleaseAssets($Release, [string]$Repository) {
    $filename = "SnapBar-$($Release.tag_name)-win-x64.zip"
    foreach ($name in @($filename, "$filename.sha256")) {
        $assets = @($Release.assets | Where-Object { $_.name -ceq $name })
        if ($assets.Count -ne 1 -or $assets[0].state -cne 'uploaded' -or $assets[0].size -le 0 -or
            $assets[0].browser_download_url -cne "https://github.com/$Repository/releases/download/$($Release.tag_name)/$name") { return $false }
    }
    return $true
}

function Test-SnapBarAncestor([string]$Root, [string]$Ancestor, [string]$Commit) {
    $PSNativeCommandUseErrorActionPreference = $false
    & git -C $Root merge-base --is-ancestor $Ancestor $Commit
    if ($LASTEXITCODE -gt 1) { throw 'Cannot verify release ancestry.' }
    return $LASTEXITCODE -eq 0
}

function Get-SnapBarReleaseBaseline {
    param([object[]]$Releases, [string]$Root, [string]$Commit, [string]$Repository,
        [string]$ExcludeTag, [string]$PreviousTag)
    $null = Invoke-SnapBarNotesGit $Root @('rev-parse', '--verify', "$Commit^{commit}")
    if ((Invoke-SnapBarNotesGit $Root @('rev-parse', '--is-shallow-repository')) -ne 'false') {
        throw 'Complete Git history is required to choose a release baseline.'
    }
    $candidates = @(
        foreach ($release in $Releases) {
            if ($release.draft -or $release.tag_name -ceq $ExcludeTag) { continue }
            try { $identity = Get-SnapBarVersion $release.tag_name } catch { continue }
            if (-not $identity.IsPublic -or -not (Test-SnapBarReleaseAssets $release $Repository)) { continue }
            if ([bool]$release.prerelease -ne ($identity.Channel -eq 'Preview')) { throw 'Published release channel does not match its tag.' }
            $published = [DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParse([string]$release.published_at, [ref]$published)) { throw 'Published release timestamp is invalid. Refresh release data before retrying.' }
            $tag = [string]$release.tag_name
            try { $sha = Invoke-SnapBarNotesGit $Root @('rev-parse', '--verify', "refs/tags/$tag^{commit}") }
            catch {
                $null = Invoke-SnapBarNotesGit $Root @('fetch', '--no-tags', 'origin', "refs/tags/${tag}:refs/tags/$tag")
                $sha = Invoke-SnapBarNotesGit $Root @('rev-parse', '--verify', "refs/tags/$tag^{commit}")
            }
            [pscustomobject]@{ Tag = $tag; Commit = $sha; Stable = $identity.Channel -eq 'Stable'; Published = $published }
        }
    )
    $ancestors = @($candidates | Where-Object { Test-SnapBarAncestor $Root $_.Commit $Commit })
    if ($PreviousTag) {
        if (@($ancestors | Where-Object { $_.Tag -ceq $PreviousTag }).Count -ne 1) { throw 'Explicit PreviousTag is not a complete published ancestor release.' }
        return $PreviousTag
    }
    if (-not $candidates.Count) { return '' }
    if (-not $ancestors.Count) { throw 'Published packages exist but none is an ancestor baseline. Refusing first-release fallback.' }
    $nearest = @($ancestors | Where-Object {
        $candidate = $_
        -not @($ancestors | Where-Object {
            $_.Commit -cne $candidate.Commit -and (Test-SnapBarAncestor $Root $candidate.Commit $_.Commit)
        }).Count
    })
    if (@($nearest.Commit | Sort-Object -Unique).Count -ne 1) { throw 'Ambiguous release ancestry. Specify a verified PreviousTag.' }
    $best = $nearest[0]
    foreach ($candidate in $nearest) {
        if (($candidate.Stable -and -not $best.Stable) -or
            ($candidate.Stable -eq $best.Stable -and ($candidate.Published -gt $best.Published -or
                ($candidate.Published -eq $best.Published -and [string]::CompareOrdinal($candidate.Tag, $best.Tag) -lt 0)))) { $best = $candidate }
    }
    return $best.Tag
}
