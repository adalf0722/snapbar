$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'site-common.ps1')
$run = New-SnapBarRun 'site-tests'
$script:sitePassed = 0
function Check-Site([string]$Name, [scriptblock]$Test) {
    & $Test
    $script:sitePassed++
    Write-Host "PASS $Name"
}
function Assert-Site([bool]$Condition) { if (-not $Condition) { throw 'Website assertion failed.' } }
function Reject-Site([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected website rejection.' }
}
function New-SiteRelease([string]$Version, [string]$Published = '2026-11-15T00:00:00Z') {
    $filename = "SnapBar-$Version-win-x64.zip"
    $assets = foreach ($name in @($filename, "$filename.sha256")) {
        [pscustomobject]@{ name = $name; state = 'uploaded'; size = 123; browser_download_url = "https://github.com/fixture/repo/releases/download/$Version/$name" }
    }
    return [pscustomobject]@{ tag_name = $Version; published_at = $Published; draft = $false; prerelease = $true; assets = @($assets) }
}
$old = New-SiteRelease 'v0.1.0-rc.9' '2026-11-13T00:00:00Z'
$current = New-SiteRelease 'v0.1.0-rc.10'
Check-Site 'newest publication wins regardless of API order and includes prereleases' {
    $selected = Get-SnapBarWebsiteDownload @($old, $current) 'fixture/repo'
    Assert-Site ($selected.Version -ceq 'v0.1.0-rc.10')
    $stable = New-SiteRelease 'v0.1.0' '2026-11-16T00:00:00Z'
    $stable.prerelease = $false
    Assert-Site ((Get-SnapBarWebsiteDownload @($current, $stable, $old) 'fixture/repo').Version -ceq 'v0.1.0')
}
Check-Site 'drafts, missing timestamps and unrelated tags cannot replace public downloads' {
    $draft = New-SiteRelease 'v0.1.0-rc.11' '2026-11-16T00:00:00Z'; $draft.draft = $true
    $invalid = New-SiteRelease 'v0.1.0-rc.12' 'invalid'
    $tag = New-SiteRelease 'validation'
    Assert-Site ((Get-SnapBarWebsiteDownload @($draft, $invalid, $tag, $old) 'fixture/repo').Version -ceq $old.tag_name)
}
Check-Site 'internal versions and inconsistent Release flags cannot become public downloads' {
    foreach ($version in @('v0.0.0-validation', 'v0.1.0-dev', 'v0.0.0-ci.123.g012345abcdef', 'v0.1.0-rc.01')) {
        Assert-Site ((Get-SnapBarWebsiteDownload @((New-SiteRelease $version), $old) 'fixture/repo').Version -ceq $old.tag_name)
    }
    $wrongFlag = New-SiteRelease 'v0.2.0'
    Assert-Site ((Get-SnapBarWebsiteDownload @($wrongFlag, $old) 'fixture/repo').Version -ceq $old.tag_name)
    $beta = New-SiteRelease 'v0.2.0-beta.1'
    Assert-Site ((Get-SnapBarWebsiteDownload @($beta, $old) 'fixture/repo').Channel -ceq 'Preview')
}
Check-Site 'incomplete, duplicate, empty or foreign assets leave the previous complete release selected' {
    foreach ($kind in @('checksum', 'upload', 'duplicate', 'empty', 'foreign')) {
        $broken = New-SiteRelease 'v0.1.0-rc.11'
        switch ($kind) {
            'checksum' { $broken.assets = @($broken.assets[0]) }
            'upload' { $broken.assets[0].state = 'starter' }
            'duplicate' { $broken.assets += $broken.assets[0] }
            'empty' { $broken.assets[1].size = 0 }
            'foreign' { $broken.assets[0].browser_download_url = 'https://example.invalid/package.zip' }
        }
        Assert-Site ((Get-SnapBarWebsiteDownload @($broken, $old) 'fixture/repo').Version -ceq $old.tag_name)
    }
}
$source = Join-Path $script:SnapBarRepo 'site'
Check-Site 'no public package or unsafe repository fails before generating an artifact' {
    $output = Join-Path $run 'rejected'
    Reject-Site { New-SnapBarWebsite $source $output @() 'fixture/repo' }
    Reject-Site { New-SnapBarWebsite $source $output @($current) '../unexpected' }
    Assert-Site (-not (Test-Path -LiteralPath $output))
}
Check-Site 'generated bilingual pages have matching static ZIP, notes and version without changing source' {
    $before = @{}
    foreach ($relative in @('index.html', 'en/index.html')) { $before[$relative] = (Get-FileHash -LiteralPath (Join-Path $source $relative)).Hash }
    $output = Join-Path $run 'generated'
    $selected = New-SnapBarWebsite $source $output @($old, $current) 'fixture/repo'
    foreach ($relative in $before.Keys) {
        $html = ConvertFrom-SnapBarUtf8 ([IO.File]::ReadAllBytes((Join-Path $output $relative))) $relative
        Assert-Site ([regex]::Matches($html, [regex]::Escape($selected.DownloadUrl)).Count -eq 2)
        Assert-Site ($html.Contains('id="release-notes" href="' + $selected.ReleaseUrl + '"'))
        $label = if ($relative -eq 'index.html') { '預覽版' } else { 'Preview' }
        Assert-Site ($html.Contains('<span id="download-version">0.1.0-rc.10 · ' + $label + '</span>'))
        Assert-Site (-not $html.Contains('download.js') -and -not $html.Contains('api.github.com'))
        Assert-Site ((Get-FileHash -LiteralPath (Join-Path $source $relative)).Hash -ceq $before[$relative])
    }
    Assert-Site (Test-Path -LiteralPath (Join-Path $output 'style.css'))
    Assert-Site (Test-Path -LiteralPath (Join-Path $output 'selection-demo.js'))
    Assert-Site (Test-Path -LiteralPath (Join-Path $output 'icon.png'))
    $zh = [IO.File]::ReadAllText((Join-Path $output 'index.html'), [Text.Encoding]::UTF8)
    Assert-Site ($zh.Contains('選好文字') -and $zh.Contains('回覆太囉嗦'))
    Reject-Site { New-SnapBarWebsite $source $output @($current) 'fixture/repo' }
    Reject-Site { New-SnapBarWebsite $source (Join-Path $source 'nested-output') @($current) 'fixture/repo' }
}
Check-Site 'a missing marker in either language blocks the whole deployment artifact' {
    $fixture = Join-Path $run 'source'
    $null = New-Item -ItemType Directory -Path $fixture
    Copy-Item -Path (Join-Path $source '*') -Destination $fixture -Recurse
    $english = Join-Path $fixture 'en/index.html'
    $html = [IO.File]::ReadAllText($english).Replace('id="download-version"', 'id="missing-version"')
    [IO.File]::WriteAllText($english, $html, [Text.UTF8Encoding]::new($false))
    $output = Join-Path $run 'bad-markers'
    Reject-Site { New-SnapBarWebsite $fixture $output @($current) 'fixture/repo' }
    Assert-Site (-not (Test-Path -LiteralPath $output))
}
Check-Site 'GitHub JSON arrays are flattened and all release pages are read' {
    $script:siteRequests = [Collections.Generic.List[string]]::new()
    function Invoke-RestMethod {
        param($Uri, $Headers, $TimeoutSec)
        $script:siteRequests.Add($Uri)
        if ($Uri.EndsWith('page=1')) { Write-Output -NoEnumerate @((1..100) | ForEach-Object { $old }) }
        elseif ($Uri.EndsWith('page=2')) { Write-Output -NoEnumerate @($current) }
        else { throw 'Unexpected release page.' }
    }
    try {
        $releases = @(Get-SnapBarWebsiteReleases 'fixture/repo')
        Assert-Site ($releases.Count -eq 101 -and $script:siteRequests.Count -eq 2)
        Assert-Site ((Get-SnapBarWebsiteDownload $releases 'fixture/repo').Version -ceq $current.tag_name)
    }
    finally { Remove-Item Function:Invoke-RestMethod }
}
Check-Site 'GitHub API failure is propagated instead of generating stale downloads' {
    function Invoke-RestMethod { throw 'Controlled API failure.' }
    try { Reject-Site { Get-SnapBarWebsiteReleases 'fixture/repo' } }
    finally { Remove-Item Function:Invoke-RestMethod }
}
Write-Host "$script:sitePassed passed, 0 failed"
