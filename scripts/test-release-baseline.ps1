$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'release-baseline.ps1')
$run = New-SnapBarRun 'baseline-tests'
$root = Join-Path $run 'source'
$null = [IO.Directory]::CreateDirectory($root)
function Git-Baseline([string[]]$Arguments) { return Invoke-SnapBarNotesGit $root $Arguments }
function Save-Baseline([string]$Value) {
    [IO.File]::WriteAllText((Join-Path $root 'fixture.txt'), $Value, [Text.UTF8Encoding]::new($false))
    $null = Git-Baseline @('add', '.')
    $null = Git-Baseline @('-c', 'core.hooksPath=', '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', $Value)
    return Git-Baseline @('rev-parse', 'HEAD')
}
function Release-Baseline([string]$Tag, [string]$Commit, [string]$Published = '2026-11-16T00:00:00Z') {
    $null = Git-Baseline @('tag', $Tag, $Commit)
    $filename = "SnapBar-$Tag-win-x64.zip"
    $assets = @($filename, "$filename.sha256") | ForEach-Object {
        [pscustomobject]@{ name = $_; state = 'uploaded'; size = 100; browser_download_url = "https://github.com/fixture/repo/releases/download/$Tag/$_" }
    }
    return [pscustomobject]@{ tag_name = $Tag; draft = $false; prerelease = (Get-SnapBarVersion $Tag).Channel -eq 'Preview'; published_at = $Published; assets = @($assets) }
}
$passed = 0
function Check-Baseline([string]$Name, [scriptblock]$Action) { & $Action; $script:passed++; Write-Host "PASS $Name" }
function Equal-Baseline([string]$Actual, [string]$Expected) { if ($Actual -cne $Expected) { throw "Baseline mismatch: $Actual != $Expected" } }
function Reject-Baseline([scriptblock]$Action) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw 'Expected baseline rejection.' }
}
function Select-Baseline([object[]]$Releases, [string]$Commit, [string]$PreviousTag) {
    return Get-SnapBarReleaseBaseline -Releases $Releases -Root $root -Commit $Commit -Repository fixture/repo -PreviousTag $PreviousTag
}
$null = Git-Baseline @('init', '-b', 'main', '-q')
$base = Save-Baseline 'base'
$rc = Release-Baseline 'v0.1.0-rc.12' $base
$stable = Release-Baseline 'v0.1.0' $base
$next = Save-Baseline 'next'
Check-Baseline 'cross-series baseline prefers stable on the same source commit' {
    Equal-Baseline (Select-Baseline @($rc, $stable) $next) 'v0.1.0'
}
$closer = Release-Baseline 'v0.2.0-rc.1' $next
Check-Baseline 'nearest ancestry outranks stable and publication date' {
    Equal-Baseline (Select-Baseline @($rc, $stable, $closer) $next) 'v0.2.0-rc.1'
}
$older = Release-Baseline 'v0.2.0-beta.1' $next '2026-11-15T00:00:00Z'
$tie = Release-Baseline 'v0.2.0-alpha.1' $next
Check-Baseline 'same-commit preview ties use UTC publication and ordinal tag order' {
    Equal-Baseline (Select-Baseline @($closer, $older, $tie) $next) 'v0.2.0-alpha.1'
}
Check-Baseline 'explicit baseline must be a complete published ancestor' {
    Equal-Baseline (Select-Baseline @($rc, $stable, $closer) $next 'v0.1.0') 'v0.1.0'
    Reject-Baseline { Select-Baseline @($stable) $base 'v0.2.0-rc.99' }
}
$null = Git-Baseline @('checkout', '-qb', 'side', $base)
$sideCommit = Save-Baseline 'side'
$side = Release-Baseline 'v0.1.1-rc.1' $sideCommit
$null = Git-Baseline @('checkout', '-q', 'main')
$null = Git-Baseline @('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'merge', '--no-ff', '-s', 'ours', 'side', '-qm', 'merge fixture')
$merged = Git-Baseline @('rev-parse', 'HEAD')
Check-Baseline 'incomparable merged releases require an explicit verified baseline' {
    Reject-Baseline { Select-Baseline @($closer, $side) $merged }
    Equal-Baseline (Select-Baseline @($closer, $side) $merged 'v0.1.1-rc.1') 'v0.1.1-rc.1'
}
Check-Baseline 'published unrelated history never falls back to first release' {
    Reject-Baseline { Select-Baseline @($side) $next }
    Reject-Baseline { Select-Baseline @($side) $next 'v0.1.1-rc.1' }
}
Check-Baseline 'only the first complete publication permits an empty baseline' {
    Equal-Baseline (Select-Baseline @() $base) ''
    $draft = $stable.PSObject.Copy(); $draft.draft = $true
    Equal-Baseline (Select-Baseline @($draft) $base) ''
    $incomplete = $stable.PSObject.Copy(); $incomplete.assets = @()
    Equal-Baseline (Select-Baseline @($incomplete) $base) ''
}
Check-Baseline 'invalid timestamps and missing history fail closed' {
    $invalid = $stable.PSObject.Copy(); $invalid.published_at = 'invalid'
    Reject-Baseline { Select-Baseline @($invalid) $next }
    $invalid = $stable.PSObject.Copy(); $invalid.tag_name = 'v8.0.0'
    $filename = 'SnapBar-v8.0.0-win-x64.zip'
    $invalid.assets = @($filename, "$filename.sha256") | ForEach-Object {
        [pscustomobject]@{ name = $_; state = 'uploaded'; size = 100; browser_download_url = "https://github.com/fixture/repo/releases/download/v8.0.0/$_" }
    }
    Reject-Baseline { Select-Baseline @($invalid) $next }
}
Write-Host "$passed passed, 0 failed"
