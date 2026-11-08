$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'auto-release-common.ps1')
. (Join-Path $PSScriptRoot 'release-notes.ps1')
$run = New-SnapBarRun 'auto-release-tests'
$script:autoPassed = 0
function Check-Auto([string]$Name, [scriptblock]$Test) {
    & $Test
    $script:autoPassed++
    Write-Host "PASS $Name"
}
function Assert-Auto([bool]$Condition) { if (-not $Condition) { throw 'Automatic release assertion failed.' } }
function Reject-Auto([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected automatic release rejection.' }
}
function New-AutoFixtureRelease([string]$Version, [bool]$Draft = $false) {
    return [pscustomobject]@{ tag_name = $Version; draft = $Draft; body = ''; assets = @(); target_commitish = ''; prerelease = $true }
}
Check-Auto 'automatic version starts at rc.1 and increments numerically across tags and drafts' {
    Assert-Auto ((Get-SnapBarAutoVersion @() @() '123').version -eq 'v0.1.0-rc.1')
    $releases = @((New-AutoFixtureRelease 'v0.1.0-rc.9'), (New-AutoFixtureRelease 'v0.1.0-rc.10' $true))
    $plan = Get-SnapBarAutoVersion $releases @('v0.1.0-rc.11', 'v1.0.0-rc.99', 'v0.1.0', 'v0.1.0-rc.012') '123'
    Assert-Auto ($plan.version -eq 'v0.1.0-rc.12' -and $plan.previousTag -eq 'v0.1.0-rc.9')
}
Check-Auto 'rerun retains its version and rejects ambiguous ownership' {
    $release = New-AutoFixtureRelease 'v0.1.0-rc.5' $true
    $release.body = '<!-- snapbar-run: 123 -->'
    $plan = Get-SnapBarAutoVersion @($release) @('v0.1.0-rc.99') '123'
    Assert-Auto ($plan.version -eq 'v0.1.0-rc.5')
    Reject-Auto { Get-SnapBarAutoVersion @($release, $release) @() '123' }
    Reject-Auto { Get-SnapBarAutoVersion @() @() 'not-a-run' }
}
Check-Auto 'a new run skips a draft reserved by an earlier failed run' {
    $published = New-AutoFixtureRelease 'v0.1.0-rc.5'
    $draft = New-AutoFixtureRelease 'v0.1.0-rc.6' $true
    $draft.body = '<!-- snapbar-run: 123 -->'
    $plan = Get-SnapBarAutoVersion @($draft, $published) @('v0.1.0-rc.5') '124'
    Assert-Auto ($plan.version -eq 'v0.1.0-rc.7' -and $plan.previousTag -eq 'v0.1.0-rc.5')
}
$archive = Join-Path $run 'SnapBar-v0.1.0-rc.5-win-x64.zip'
[IO.File]::WriteAllText($archive, 'controlled package fixture', [Text.UTF8Encoding]::new($false))
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$archive.sha256", "$hash  $([IO.Path]::GetFileName($archive))`n", [Text.UTF8Encoding]::new($false))
function New-AutoAsset([string]$Path) {
    return [pscustomobject]@{ name = [IO.Path]::GetFileName($Path); state = 'uploaded'; size = (Get-Item -LiteralPath $Path).Length; digest = 'sha256:' + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
}
Check-Auto 'asset retries skip identical uploads and reject different or duplicate public assets' {
    $asset = New-AutoAsset $archive
    Assert-Auto (@(Get-SnapBarAssetUploads @($asset) @($archive, "$archive.sha256")).Count -eq 1)
    Reject-Auto { Get-SnapBarAssetUploads @($asset, $asset) @($archive) }
    $asset.digest = 'sha256:wrong'
    Reject-Auto { Get-SnapBarAssetUploads @($asset) @($archive) }
}
# All GitHub commands below are handled by this in-process fake; no public Release is created.
$global:SnapBarFixtureReleases = @()
$global:SnapBarFixtureTags = @()
$global:SnapBarFixtureCommands = [Collections.Generic.List[string]]::new()
$global:SnapBarFixtureFailUpload = $false
$global:SnapBarFixtureFailQuery = $false
$global:SnapBarFixtureHideDraft = $false
function gh {
    $arguments = @($args)
    $global:SnapBarFixtureCommands.Add(($arguments -join ' '))
    $global:LASTEXITCODE = 0
    if ($arguments[0] -eq 'api') {
        if ($global:SnapBarFixtureFailQuery) { $global:LASTEXITCODE = 1; return }
        $endpoint = @($arguments | Where-Object { $_ -like 'repos/*' })[0]
        if ($endpoint -like '*/releases?per_page=*') {
            $listed = @($global:SnapBarFixtureReleases | Where-Object { -not ($global:SnapBarFixtureHideDraft -and $_.draft) })
            return '[' + (ConvertTo-Json -InputObject $listed -Depth 12 -Compress) + ']'
        }
        if ($endpoint -like '*/git/matching-refs/*') { return ConvertTo-Json -InputObject @($global:SnapBarFixtureTags) -Compress }
        if ($endpoint -like '*/commits/*') { return '{"sha":"different-source"}' }
        if ($endpoint -eq 'repos/fixture/repo/releases' -and $arguments[2] -eq 'POST') {
            $version = ($arguments | Where-Object { $_ -like 'tag_name=*' }) -replace '^tag_name=', ''
            $global:SnapBarFixtureRelease = New-AutoFixtureRelease $version $true
            $global:SnapBarFixtureRelease | Add-Member -NotePropertyName id -NotePropertyValue 42
            $global:SnapBarFixtureRelease.target_commitish = ($arguments | Where-Object { $_ -like 'target_commitish=*' }) -replace '^target_commitish=', ''
            $notesPath = ($arguments | Where-Object { $_ -like 'body=@*' }) -replace '^body=@', ''
            $global:SnapBarFixtureRelease.body = [IO.File]::ReadAllText($notesPath)
            $global:SnapBarFixtureReleases = @($global:SnapBarFixtureRelease)
            return ConvertTo-Json -InputObject $global:SnapBarFixtureRelease -Depth 12 -Compress
        }
        if ($arguments[3] -like 'https://uploads.github.com/repos/fixture/repo/releases/42/assets?name=*') {
            if ($global:SnapBarFixtureFailUpload) { $global:LASTEXITCODE = 1; return }
            $global:SnapBarFixtureRelease.assets += (New-AutoAsset $arguments[-1])
            return
        }
        if ($endpoint -eq 'repos/fixture/repo/releases/42') {
            if ($arguments[2] -eq 'PATCH') { $global:SnapBarFixtureRelease.draft = $false }
            return ConvertTo-Json -InputObject $global:SnapBarFixtureRelease -Depth 12 -Compress
        }
    }
    throw "Unexpected fake GitHub call: $arguments"
}
$source = Join-Path $run 'source'
$null = New-Item -ItemType Directory -Path $source
function Invoke-NotesFixtureGit([string[]]$Arguments) {
    return Invoke-SnapBarNotesGit $source $Arguments
}
function Write-NotesFixture([string]$Path, [string]$Content) {
    $full = Join-Path $source $Path
    $null = New-Item -ItemType Directory -Force -Path (Split-Path $full -Parent)
    [IO.File]::WriteAllText($full, $Content, [Text.UTF8Encoding]::new($false))
}
function Save-NotesFixture {
    $null = Invoke-NotesFixtureGit @('add', '-A')
    $null = Invoke-NotesFixtureGit @('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'core.hooksPath=', 'commit', '-qm', 'fixture')
    return Invoke-NotesFixtureGit @('rev-parse', 'HEAD')
}
$null = Invoke-NotesFixtureGit @('init', '-q')
Write-NotesFixture 'src/app.txt' 'old desktop'
$base = Save-NotesFixture
$null = Invoke-NotesFixtureGit @('tag', 'v0.1.0-rc.4')
Write-NotesFixture 'src/app.txt' 'new desktop'
$missing = Save-NotesFixture
Check-Auto 'desktop changes without notes fail instead of publishing an empty changelog' {
    Reject-Auto { Get-SnapBarReleaseContent -Root $source -Commit $missing -PreviousTag v0.1.0-rc.4 }
}
$validNote = '{"title":"離線查詞","summary":"閱讀時查看釋義。","highlights":["多本詞典一起查詢。"],"usage":"先匯入自己的詞典。"}'
Write-NotesFixture 'release-notes/lookup.json' $validNote
$commit = Save-NotesFixture
Check-Auto 'notes use the exact source commit and retain Traditional Chinese' {
    Write-NotesFixture 'release-notes/lookup.json' 'invalid working copy'
    $content = Get-SnapBarReleaseContent -Root $source -Commit $commit -PreviousTag v0.1.0-rc.4
    Assert-Auto ($content.Contains('離線查詞') -and $content.Contains('先匯入自己的詞典。'))
    Write-NotesFixture 'release-notes/lookup.json' $validNote
    Reject-Auto { Get-SnapBarReleaseContent -Root $source -Commit $commit -PreviousTag nonexistent }
    Assert-Auto ((Get-SnapBarReleaseContent -Root $source -Commit $commit).Contains('離線查詞'))
}
Check-Auto 'invalid and placeholder notes are rejected' {
    Reject-Auto { ConvertFrom-SnapBarReleaseNote '{}' 'fixture.json' }
    Reject-Auto { ConvertFrom-SnapBarReleaseNote ($validNote.Replace('離線查詞', 'TODO')) 'fixture.json' }
    Reject-Auto { ConvertFrom-SnapBarReleaseNote ($validNote.Replace('["多本詞典一起查詢。"]', '[]')) 'fixture.json' }
}
Check-Auto 'website and tooling changes explicitly say there are no desktop feature updates' {
    $null = Invoke-NotesFixtureGit @('tag', 'notes-baseline', $commit)
    Write-NotesFixture 'site/index.html' 'website update'
    Write-NotesFixture 'scripts/build.ps1' '# tooling update'
    $maintenance = Save-NotesFixture
    $content = Get-SnapBarReleaseContent -Root $source -Commit $maintenance -PreviousTag notes-baseline
    Assert-Auto ($content.Contains('本次沒有桌面功能更新') -and $content.Contains('網站') -and $content.Contains('發布工具') -and -not $content.Contains('離線查詞'))
}
Check-Auto 'old notes cannot silently cover new desktop changes' {
    Write-NotesFixture 'src/app.txt' 'another desktop change'
    $uncovered = Save-NotesFixture
    Reject-Auto { Get-SnapBarReleaseContent -Root $source -Commit $uncovered -PreviousTag notes-baseline }
    $null = Invoke-NotesFixtureGit @('tag', 'future', $uncovered)
    Reject-Auto { Get-SnapBarReleaseContent -Root $source -Commit $commit -PreviousTag future }
}
Check-Auto 'new malformed committed notes block publication' {
    Write-NotesFixture 'release-notes/broken.json' '{invalid json'
    $broken = Save-NotesFixture
    Reject-Auto { Get-SnapBarReleaseContent -Root $source -Commit $broken -PreviousTag notes-baseline }
}
function Invoke-AutoPublishFixture {
    & (Join-Path $PSScriptRoot 'publish-auto-release.ps1') -Version v0.1.0-rc.5 -Commit $commit -PackageDirectory $run -SourceDirectory $source -PreviousTag v0.1.0-rc.4 -Repository fixture/repo -RunId 123
}
try {
    Check-Auto 'missing release notes block publishing before any GitHub write' {
        $global:SnapBarFixtureCommands.Clear()
        Reject-Auto {
            & (Join-Path $PSScriptRoot 'publish-auto-release.ps1') -Version v0.1.0-rc.5 -Commit $missing -PackageDirectory $run -SourceDirectory $source -PreviousTag v0.1.0-rc.4 -Repository fixture/repo -RunId 123
        }
        Assert-Auto (-not @($global:SnapBarFixtureCommands | Where-Object { $_ -like 'api --method *' }).Count)
    }
    Check-Auto 'publisher stages complete assets even when its new draft is absent from the release list' {
        $global:SnapBarFixtureHideDraft = $true
        try { Invoke-AutoPublishFixture } finally { $global:SnapBarFixtureHideDraft = $false }
        Assert-Auto (-not $global:SnapBarFixtureRelease.draft -and $global:SnapBarFixtureRelease.assets.Count -eq 2)
        $body = $global:SnapBarFixtureRelease.body
        Assert-Auto ($body.Contains("<!-- snapbar-source: $commit -->") -and $body.Contains('離線查詞'))
        Assert-Auto ($body.IndexOf('## 本次更新') -lt $body.IndexOf('## 下載與更新'))
        Assert-Auto ($body.Contains('https://github.com/fixture/repo/releases/download/v0.1.0-rc.5/SnapBar-v0.1.0-rc.5-win-x64.zip'))
        Assert-Auto ($body.Contains('compare/v0.1.0-rc.4...v0.1.0-rc.5') -and $body.Contains('<details>'))
        Assert-Auto (-not @($global:SnapBarFixtureCommands | Where-Object { $_ -like '*generate-notes*' }).Count)
        Assert-Auto ($global:SnapBarFixtureCommands[$global:SnapBarFixtureCommands.Count - 1] -like 'api --method PATCH repos/fixture/repo/releases/42*')
    }
    Check-Auto 'successful rerun does not create a second Release or overwrite attachments' {
        $global:SnapBarFixtureCommands.Clear()
        Invoke-AutoPublishFixture
        Assert-Auto (-not @($global:SnapBarFixtureCommands | Where-Object { $_ -like 'api --method *' }).Count)
    }
    Check-Auto 'upload failure leaves a draft and failed-job retry completes it' {
        $global:SnapBarFixtureReleases = @(); $global:SnapBarFixtureCommands.Clear(); $global:SnapBarFixtureFailUpload = $true
        Reject-Auto { Invoke-AutoPublishFixture }
        Assert-Auto ($global:SnapBarFixtureRelease.draft -and -not @($global:SnapBarFixtureCommands | Where-Object { $_ -like 'api --method PATCH*' }).Count)
        $global:SnapBarFixtureFailUpload = $false
        # A first attachment may already exist after an interrupted upload.
        $global:SnapBarFixtureRelease.assets = @((New-AutoAsset $archive))
        $global:SnapBarFixtureCommands.Clear()
        Invoke-AutoPublishFixture
        Assert-Auto (-not $global:SnapBarFixtureRelease.draft -and @($global:SnapBarFixtureCommands | Where-Object { $_ -like 'api --method POST https://uploads.github.com/*' }).Count -eq 1)
    }
    Check-Auto 'publisher rejects foreign Releases before any write' {
        $global:SnapBarFixtureRelease.target_commitish = 'b' * 40; $global:SnapBarFixtureCommands.Clear()
        Reject-Auto { Invoke-AutoPublishFixture }
        Assert-Auto (-not @($global:SnapBarFixtureCommands | Where-Object { $_ -like 'api --method *' }).Count)
    }
    Check-Auto 'reserved tag and GitHub query failure never publish a Release' {
        $global:SnapBarFixtureReleases = @(); $global:SnapBarFixtureTags = @([pscustomobject]@{ ref = 'refs/tags/v0.1.0-rc.5' }); $global:SnapBarFixtureCommands.Clear()
        Reject-Auto { Invoke-AutoPublishFixture }
        Assert-Auto (-not @($global:SnapBarFixtureCommands | Where-Object { $_ -like 'release *' }).Count)
        $global:SnapBarFixtureTags = @()
        $global:SnapBarFixtureFailQuery = $true
        Reject-Auto { Invoke-AutoPublishFixture }
        $global:SnapBarFixtureFailQuery = $false
    }
    Check-Auto 'a tag changed to another commit leaves an existing valid draft unpublished' {
        $global:SnapBarFixtureReleases = @(); $global:SnapBarFixtureFailUpload = $true
        Reject-Auto { Invoke-AutoPublishFixture }
        $global:SnapBarFixtureFailUpload = $false
        $global:SnapBarFixtureTags = @([pscustomobject]@{ ref = 'refs/tags/v0.1.0-rc.5' })
        $global:SnapBarFixtureCommands.Clear()
        Reject-Auto { Invoke-AutoPublishFixture }
        Assert-Auto ($global:SnapBarFixtureRelease.draft -and -not @($global:SnapBarFixtureCommands | Where-Object { $_ -like 'api --method PATCH*' }).Count)
        $global:SnapBarFixtureTags = @()
    }
    Check-Auto 'corrupted ZIP fails before any GitHub command' {
        $global:SnapBarFixtureCommands.Clear()
        [IO.File]::AppendAllText($archive, 'changed')
        Reject-Auto { Invoke-AutoPublishFixture }
        Assert-Auto ($global:SnapBarFixtureCommands.Count -eq 0)
    }
}
finally { Remove-Item Function:gh; Remove-Variable -Scope Global -Name SnapBarFixtureReleases, SnapBarFixtureTags, SnapBarFixtureCommands, SnapBarFixtureFailUpload, SnapBarFixtureFailQuery, SnapBarFixtureHideDraft, SnapBarFixtureRelease -ErrorAction SilentlyContinue }
Write-Host "$script:autoPassed passed, 0 failed"
