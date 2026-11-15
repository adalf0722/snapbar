. (Join-Path $PSScriptRoot 'version-common.ps1')

function Invoke-SnapBarGh([string[]]$Arguments) {
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub command failed: $($Arguments[0]) $($Arguments[1])" }
    return ($output -join "`n")
}

function Get-SnapBarReleases([string]$Repository) {
    $pages = (Invoke-SnapBarGh @('api', '--paginate', '--slurp', "repos/$Repository/releases?per_page=100")) | ConvertFrom-Json
    return @($pages | ForEach-Object { foreach ($release in $_) { $release } })
}

function Get-SnapBarAutoVersion([object[]]$Releases, [string[]]$Tags, [string]$RunId,
    [string]$VersionPrefix = (Get-SnapBarVersionPrefix), [string]$Commit) {
    if ($RunId -notmatch '^[0-9]+$') { throw 'Automatic release requires a numeric workflow run ID.' }
    if ((Get-SnapBarVersion "v$VersionPrefix").Channel -ne 'Stable') { throw 'Automatic version prefix must be stable.' }
    $marker = "<!-- snapbar-run: $RunId -->"
    $existing = @($Releases | Where-Object { $_.body -and $_.body.Contains($marker) })
    if ($existing.Count -gt 1) { throw 'Multiple Releases claim the same workflow run.' }
    $pattern = '^v' + [regex]::Escape($VersionPrefix) + '-rc\.([1-9][0-9]*)$'
    $versions = @(@($Releases | ForEach-Object { $_.tag_name }) + @($Tags) | Where-Object { $_ -cmatch $pattern })
    if ($existing.Count) {
        $version = $existing[0].tag_name
        if ($version -cnotmatch '^v[0-9]+\.[0-9]+\.[0-9]+-rc\.[1-9][0-9]*$') { throw 'Existing automatic Release uses an unexpected version.' }
        $null = Get-SnapBarVersion $version
        if ($Commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Recovery requires the exact source commit.' }
        Assert-SnapBarAutoRelease $existing[0] $version $Commit $RunId
        $previousMarkers = [regex]::Matches($existing[0].body, '<!-- snapbar-previous: (.*?) -->')
        if ($previousMarkers.Count -gt 1) { throw 'Release contains ambiguous previous-tag markers.' }
        $previous = [regex]::Match($existing[0].body, '<!-- snapbar-previous: (.*?) -->')
        $previousTag = if ($previous.Success) { $previous.Groups[1].Value } else { '' }
        if ($previousTag) { $null = Get-SnapBarVersion $previousTag }
        return [pscustomobject]@{ version = $version; previousTag = $previousTag; recovered = $true; previousKnown = $previous.Success; draft = $existing[0].draft }
    }
    else {
        $maximum = [long]0
        foreach ($reserved in $versions) { $maximum = [math]::Max($maximum, [long]([regex]::Match($reserved, $pattern).Groups[1].Value)) }
        if ($maximum -eq [long]::MaxValue) { throw 'Release version overflow.' }
        if (@($Tags | Where-Object { $_ -ceq "v$VersionPrefix" }).Count -or
            @($Releases | Where-Object { -not $_.draft -and $_.tag_name -ceq "v$VersionPrefix" }).Count) {
            throw 'This version series already has a stable release/tag. Update Version.props before allocating a new prerelease.'
        }
        $version = "v$VersionPrefix-rc.$($maximum + 1)"
    }
    return [pscustomobject]@{ version = $version; previousTag = ''; recovered = $false; previousKnown = $false; draft = $true }
}

function Assert-SnapBarAutoRelease($Release, [string]$Version, [string]$Commit, [string]$RunId) {
    if ($Release.tag_name -cne $Version -or $Release.target_commitish -cne $Commit -or
        -not $Release.body -or -not $Release.body.Contains("<!-- snapbar-run: $RunId -->") -or
        -not $Release.body.Contains("<!-- snapbar-source: $Commit -->") -or -not $Release.prerelease) {
        throw 'Existing Release belongs to a different source or workflow run; nothing was overwritten.'
    }
}

function Get-SnapBarAssetUploads([object[]]$Assets, [string[]]$Files) {
    $uploads = [Collections.Generic.List[string]]::new()
    foreach ($file in $Files) {
        $name = [IO.Path]::GetFileName($file)
        $matches = @($Assets | Where-Object { $_.name -ceq $name })
        if ($matches.Count -gt 1) { throw "Duplicate Release asset: $name" }
        if (-not $matches.Count) { $uploads.Add($file); continue }
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($matches[0].state -ne 'uploaded' -or $matches[0].size -ne (Get-Item -LiteralPath $file).Length -or
            $matches[0].digest -cne "sha256:$hash") {
            throw "Existing Release asset differs: $name. Retry failed jobs with the original artifact; existing downloads were preserved."
        }
    }
    return @($uploads)
}
