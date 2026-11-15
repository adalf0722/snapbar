# Real SDK-generated version resources, shared by release tooling regression tests.
function New-SnapBarVersionFixture([string]$Directory, [string]$Version, [string]$Commit) {
    $null = [IO.Directory]::CreateDirectory($Directory)
    $project = Join-Path $Directory 'VersionFixture.csproj'
    [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
    $output = Join-Path $Directory 'out'
    $result = Invoke-SnapBarCommand 'VersionFixture' (Get-SnapBarSdk) @('build', $project, '-c', 'Release', '-o', $output, "-p:Version=$Version", "-p:SourceRevisionId=$Commit", '-p:IncludeSourceRevisionInInformationalVersion=true', '--nologo') $Directory
    Assert-SnapBarCommand $result
    return Join-Path $output 'VersionFixture.dll'
}

function New-SnapBarVersionZip([string]$Archive, [string]$App, [string]$Probe) {
    $zip = [IO.Compression.ZipFile]::Open($Archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        if ($App) { $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $App, 'SnapBar.App.dll') }
        if ($Probe) { $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $Probe, 'Probe/SnapBar.Probe.dll') }
    }
    finally { $zip.Dispose() }
}
