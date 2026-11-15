function Get-SnapBarWebsiteReleases([string]$Repository) {
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'A valid GitHub owner/repository is required.' }
    $headers = @{ Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
    if ($env:GH_TOKEN) { $headers.Authorization = "Bearer $env:GH_TOKEN" }
    $releases = [Collections.Generic.List[object]]::new()
    $page = 1
    do {
        [object[]]$batch = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=100&page=$page" -Headers $headers -TimeoutSec 30
        foreach ($release in $batch) { $releases.Add($release) }
        $page++
    } while ($batch.Count -eq 100)
    return $releases.ToArray()
}

function Get-SnapBarWebsiteDownload([object[]]$Releases, [string]$Repository) {
    if ($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'A valid GitHub owner/repository is required.' }
    $candidates = foreach ($release in $Releases) {
        if ($release.draft -or $release.tag_name -cnotmatch '^v[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$') { continue }
        $published = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string]$release.published_at, [ref]$published)) { continue }
        $version = $release.tag_name
        $filename = "SnapBar-$version-win-x64.zip"
        $releaseUrl = "https://github.com/$Repository/releases/tag/$version"
        $downloadUrl = "https://github.com/$Repository/releases/download/$version/$filename"
        $complete = $true
        foreach ($name in @($filename, "$filename.sha256")) {
            $expectedUrl = "https://github.com/$Repository/releases/download/$version/$name"
            $assets = @($release.assets | Where-Object { $_.name -ceq $name })
            if ($assets.Count -ne 1 -or $assets[0].state -cne 'uploaded' -or $assets[0].size -le 0 -or
                $assets[0].browser_download_url -cne $expectedUrl) {
                $complete = $false
                break
            }
        }
        if ($complete) {
            [pscustomobject]@{ Version = $version; DownloadUrl = $downloadUrl; ReleaseUrl = $releaseUrl; PublishedAt = $published }
        }
    }
    # Include prereleases; publication time, not API order or /releases/latest, decides.
    $selected = @($candidates | Sort-Object -Property PublishedAt, Version -Descending | Select-Object -First 1)
    if (-not $selected.Count) { throw 'No published Windows release has a complete ZIP and checksum. Website was not generated.' }
    return $selected[0]
}

function New-SnapBarWebsite([string]$SourceDirectory, [string]$OutputDirectory, [object[]]$Releases, [string]$Repository) {
    $download = Get-SnapBarWebsiteDownload $Releases $Repository
    $source = [IO.Path]::GetFullPath($SourceDirectory)
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    $separator = [IO.Path]::DirectorySeparatorChar
    if ($output.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
        $output.StartsWith($source.TrimEnd($separator) + $separator, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Website output must be outside the source directory.'
    }
    if (Test-Path -LiteralPath $output) { throw 'Website output must be a new directory.' }
    $pages = @{}
    foreach ($relative in @('index.html', 'en/index.html')) {
        $html = ConvertFrom-SnapBarUtf8 ([IO.File]::ReadAllBytes((Join-Path $source $relative))) $relative
        $patterns = @(
            @{ Pattern = '(?<=data-windows-download href=")[^"]*'; Count = 2; Value = $download.DownloadUrl },
            @{ Pattern = '(?<=id="release-notes" href=")[^"]*'; Count = 1; Value = $download.ReleaseUrl },
            @{ Pattern = '(?<=<span id="download-version">)[^<]*'; Count = 1; Value = $download.Version }
        )
        foreach ($replacement in $patterns) {
            if ([regex]::Matches($html, $replacement.Pattern).Count -ne $replacement.Count) {
                throw "Website download markers are missing or ambiguous: $relative"
            }
            $html = [regex]::Replace($html, $replacement.Pattern, $replacement.Value)
        }
        $pages[$relative] = $html
    }
    # Validate both pages before creating a fresh deployment artifact; never edit tracked source.
    $null = [IO.Directory]::CreateDirectory($output)
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $output -Recurse -Force
    foreach ($relative in $pages.Keys) {
        [IO.File]::WriteAllText((Join-Path $output $relative), $pages[$relative], [Text.UTF8Encoding]::new($false))
    }
    return $download
}
