function Invoke-SnapBarGh([string[]]$Arguments) {
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub command failed: $($Arguments[0]) $($Arguments[1])" }
    return ($output -join "`n")
}

function Get-SnapBarReleases([string]$Repository) {
    $pages = (Invoke-SnapBarGh @('api', '--paginate', '--slurp', "repos/$Repository/releases?per_page=100")) | ConvertFrom-Json
    return @($pages | ForEach-Object { foreach ($release in $_) { $release } })
}

function Get-SnapBarAutoVersion([object[]]$Releases, [string[]]$Tags, [string]$RunId) {
    if ($RunId -notmatch '^[0-9]+$') { throw 'Automatic release requires a numeric workflow run ID.' }
    $marker = "<!-- snapbar-run: $RunId -->"
    $existing = @($Releases | Where-Object { $_.body -and $_.body.Contains($marker) })
    if ($existing.Count -gt 1) { throw 'Multiple Releases claim the same workflow run.' }
    $versions = @(@($Releases | ForEach-Object { $_.tag_name }) + @($Tags) | Where-Object { $_ -cmatch '^v0\.1\.0-rc\.[1-9][0-9]*$' })
    $maximum = [long]0
    foreach ($version in $versions) { $maximum = [math]::Max($maximum, [long]($version -replace '^v0\.1\.0-rc\.', '')) }
    if ($existing.Count) {
        $version = $existing[0].tag_name
        if ($version -cnotmatch '^v0\.1\.0-rc\.[1-9][0-9]*$') { throw 'Existing automatic Release uses an unexpected version.' }
    }
    else {
        if ($maximum -eq [long]::MaxValue) { throw 'Release version overflow.' }
        $version = "v0.1.0-rc.$($maximum + 1)"
    }
    $number = [long]($version -replace '^v0\.1\.0-rc\.', '')
    $previous = $Releases | Where-Object {
        -not $_.draft -and $_.tag_name -cmatch '^v0\.1\.0-rc\.[1-9][0-9]*$' -and
        [long]($_.tag_name -replace '^v0\.1\.0-rc\.', '') -lt $number
    } | Sort-Object { [long]($_.tag_name -replace '^v0\.1\.0-rc\.', '') } -Descending | Select-Object -First 1
    return [pscustomobject]@{ version = $version; previousTag = $(if ($previous) { $previous.tag_name } else { '' }) }
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
