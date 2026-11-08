param([switch]$Publish, [string]$DesktopReport)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$repo = $script:SnapBarRepo
$sdk = Get-SnapBarSdk
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$package = Join-Path $repo 'artifacts/p0'
Assert-SnapBarBuildIdle
if (-not $DesktopReport) { $DesktopReport = Join-Path $repo 'artifacts/checks/desktop/latest.json' }
$fingerprint = Get-SnapBarFingerprint
if ($Publish) {
    Assert-SnapBarPublishIdle $package
    Assert-SnapBarDesktopReport $DesktopReport $fingerprint
}
$run = New-SnapBarRun 'build'
$results = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ schema = 1; sourceFingerprint = $fingerprint; publish = [bool]$Publish; success = $false; results = @(); error = $null }
Push-Location $repo
try {
    $result = Invoke-SnapBarCommand 'Build' $sdk @('build', 'SnapBar.slnx', '-c', 'Release', '--nologo') $run
    $results.Add($result); Assert-SnapBarCommand $result
    $result = Invoke-SnapBarCommand 'Core' $sdk @('run', '--project', 'tests/SnapBar.Tests', '-c', 'Release', '--no-build') $run
    $results.Add($result); Assert-SnapBarCommand $result
    $appOutput = Join-Path $repo 'src/SnapBar.App/bin/Release/net10.0-windows'
    $probeOutput = Join-Path $repo 'src/SnapBar.Probe/bin/Release/net10.0-windows'
    $probeDestination = Join-Path $appOutput 'Probe'
    [IO.Directory]::CreateDirectory($probeDestination) | Out-Null
    Copy-Item -Path (Join-Path $probeOutput '*') -Destination $probeDestination -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repo 'packaging/README.md') -Destination (Join-Path $appOutput 'README.md') -Force
    Copy-Item -LiteralPath (Join-Path $repo 'packaging/README.en.md') -Destination (Join-Path $appOutput 'README.en.md') -Force
    if ($Publish) {
        $stagingRoot = Join-Path $repo ('artifacts/publish/' + [guid]::NewGuid().ToString('N'))
        $stage = Assert-SnapBarArtifactPath (Join-Path $stagingRoot 'package')
        [IO.Directory]::CreateDirectory($stage) | Out-Null
        $result = Invoke-SnapBarCommand 'PublishApp' $sdk @('publish', 'src/SnapBar.App', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $stage, '--nologo') $run
        $results.Add($result); Assert-SnapBarCommand $result
        $probe = Join-Path $stage 'Probe'
        $result = Invoke-SnapBarCommand 'PublishProbe' $sdk @('publish', 'src/SnapBar.Probe', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $probe, '--nologo') $run
        $results.Add($result); Assert-SnapBarCommand $result
        $result = Invoke-SnapBarCommand 'ProbeSmoke' (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-probe.ps1'), '-Executable', (Join-Path $probe 'SnapBar.Probe.exe')) $run
        $results.Add($result); Assert-SnapBarCommand $result
        $result = Invoke-SnapBarCommand 'StartupSmoke' (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-startup.ps1'), '-Executable', (Join-Path $stage 'SnapBar.App.exe')) $run
        $results.Add($result); Assert-SnapBarCommand $result
        $result = Invoke-SnapBarCommand 'LanguageSmoke' (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-startup.ps1'), '-Executable', (Join-Path $stage 'SnapBar.App.exe'), '-Check', 'LanguageResources') $run
        $results.Add($result); Assert-SnapBarCommand $result
        Copy-Item -LiteralPath (Join-Path $repo 'packaging/README.md') -Destination (Join-Path $stage 'README.md')
        Copy-Item -LiteralPath (Join-Path $repo 'packaging/README.en.md') -Destination (Join-Path $stage 'README.en.md')
        Copy-Item -LiteralPath (Join-Path $repo 'docs/p0-validation.md') -Destination (Join-Path $stage 'VALIDATION.md')
        Copy-Item -LiteralPath (Join-Path $repo 'docs/2026-10-26-clipboard-backup-recovery.md') -Destination (Join-Path $stage 'CLIPBOARD-RECOVERY.md')
        if ($fingerprint -ne (Get-SnapBarFingerprint)) { throw 'Source changed during publishing; previous package preserved.' }
        Assert-SnapBarDesktopReport $DesktopReport $fingerprint
        Assert-SnapBarPublishIdle $package
        $backup = Join-Path $stagingRoot 'previous'
        Move-SnapBarPackage $stage $package $backup
        Write-Host "Published: $package (previous package retained at $backup if present)"
    }
    $report.success = $true
}
catch { $report.error = $_.Exception.Message; throw }
finally {
    $report.results = @($results.ToArray())
    Write-SnapBarJson (Join-Path $run 'report.json') $report
    Pop-Location
}
