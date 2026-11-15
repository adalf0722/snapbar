$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'version-common.ps1')
. (Join-Path $PSScriptRoot 'version-fixtures.ps1')
$run = New-SnapBarRun 'version-tests'
$passed = 0
function Reject-Version([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected version rejection.' }
}
$cases = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../tests/fixtures/version-cases.json')) | ConvertFrom-Json
foreach ($case in $cases) {
    if ($case.channel) {
        if ((Get-SnapBarVersion "v$($case.version)").Channel -cne $case.channel) { throw 'Incorrect version channel.' }
    }
    else { Reject-Version { Get-SnapBarVersion "v$($case.version)" | Out-Null } }
    $passed++; Write-Host "PASS version parser $($case.version | ConvertTo-Json -Compress)"
}
$commit = 'a' * 40
$project = Join-Path $run 'fixture'
foreach ($version in @('0.1.0-dev', '0.1.0-rc.12', '0.1.0', '0.0.0-ci.123.g012345abcdef', '0.1.0-rc.13')) {
    $assembly = New-SnapBarVersionFixture $project $version $commit
    Assert-SnapBarAssemblyVersion $assembly $version $commit
    $passed++; Write-Host "PASS real assembly metadata switches to $version on incremental rebuild"
}
$archive = Join-Path $run 'package.zip'
New-SnapBarVersionZip $archive $assembly $assembly
Assert-SnapBarArchiveVersion $archive '0.1.0-rc.13' $commit
Reject-Version { Assert-SnapBarArchiveVersion $archive '0.1.0-rc.12' $commit }
Reject-Version { Assert-SnapBarArchiveVersion $archive '0.1.0-rc.13' ('b' * 40) }
$wrong = New-SnapBarVersionFixture (Join-Path $run 'wrong') '0.1.0-rc.12' $commit
$mixed = Join-Path $run 'mixed.zip'
New-SnapBarVersionZip $mixed $assembly $wrong
Reject-Version { Assert-SnapBarArchiveVersion $mixed '0.1.0-rc.13' $commit }
$missing = Join-Path $run 'missing.zip'
New-SnapBarVersionZip $missing $assembly ''
Reject-Version { Assert-SnapBarArchiveVersion $missing '0.1.0-rc.13' $commit }
$duplicate = Join-Path $run 'duplicate.zip'
New-SnapBarVersionZip $duplicate $assembly $assembly
$zip = [IO.Compression.ZipFile]::Open($duplicate, [IO.Compression.ZipArchiveMode]::Update)
try { $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $assembly, 'SnapBar.App.dll') }
finally { $zip.Dispose() }
Reject-Version { Assert-SnapBarArchiveVersion $duplicate '0.1.0-rc.13' $commit }
$passed += 6; Write-Host 'PASS ZIP verifies actual metadata and rejects wrong version, source, mixed, missing and duplicate assemblies'
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$archive.sha256", "$hash  package.zip", [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'test-release-package.ps1') -Archive $archive -Version v0.1.0-rc.13 -Commit $commit -ReleasePrerelease true
Reject-Version { & (Join-Path $PSScriptRoot 'test-release-package.ps1') -Archive $archive -Version v0.1.0-rc.13 -Commit $commit -ReleasePrerelease false }
$passed += 2
Write-Host "$passed passed, 0 failed"
