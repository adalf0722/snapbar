$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'auto-release-common.ps1')
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
        if ($endpoint -like '*/releases/generate-notes') { return '{"body":"Controlled generated changes"}' }
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
$commit = 'a' * 40
function Invoke-AutoPublishFixture {
    & (Join-Path $PSScriptRoot 'publish-auto-release.ps1') -Version v0.1.0-rc.5 -Commit $commit -PackageDirectory $run -PreviousTag v0.1.0-rc.4 -Repository fixture/repo -RunId 123
}
try {
    Check-Auto 'publisher stages complete assets even when its new draft is absent from the release list' {
        $global:SnapBarFixtureHideDraft = $true
        try { Invoke-AutoPublishFixture } finally { $global:SnapBarFixtureHideDraft = $false }
        Assert-Auto (-not $global:SnapBarFixtureRelease.draft -and $global:SnapBarFixtureRelease.assets.Count -eq 2)
        Assert-Auto ($global:SnapBarFixtureRelease.body.Contains("<!-- snapbar-source: $commit -->") -and $global:SnapBarFixtureRelease.body.Contains('Controlled generated changes'))
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
