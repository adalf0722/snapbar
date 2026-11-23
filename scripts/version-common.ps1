if (-not ('SnapBar.Core.ProductVersion' -as [type])) {
    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/SnapBar.Core/ProductVersion.cs'), [Text.Encoding]::UTF8)
    Add-Type -TypeDefinition ("#nullable enable`n" + $source)
}

function Get-SnapBarVersion([string]$Tag) {
    if ($Tag -cnotmatch '^v') { throw 'Version tag must begin with v.' }
    return [SnapBar.Core.ProductVersion]::Parse($Tag.Substring(1))
}

function Get-SnapBarVersionPrefix([string]$Root = (Split-Path $PSScriptRoot -Parent)) {
    [xml]$props = [IO.File]::ReadAllText((Join-Path $Root 'Version.props'))
    $prefix = [string]$props.Project.PropertyGroup.SnapBarVersionPrefix
    if ((Get-SnapBarVersion "v$prefix").Channel -ne 'Stable') { throw 'Version.props must contain a stable version prefix.' }
    return $prefix
}

function Assert-SnapBarAssemblyVersion([string]$Path, [string]$Version, [string]$Commit) {
    $null = Get-SnapBarVersion "v$Version"
    if ($Commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Expected source must be a full lowercase Git SHA.' }
    if (-not [IO.File]::Exists($Path)) { throw "Missing versioned assembly: $Path" }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path).ProductVersion
    if ($actual -cne "$Version+$Commit") { throw "Package version/source mismatch: $([IO.Path]::GetFileName($Path)). Expected $Version+$Commit; found $actual" }
}

function Assert-SnapBarPackageVersion([string]$Package, [string]$Version, [string]$Commit) {
    foreach ($relative in @('SnapBar.App.dll', 'Probe/SnapBar.Probe.dll')) {
        Assert-SnapBarAssemblyVersion (Join-Path $Package $relative) $Version $Commit
    }
}

function Assert-SnapBarArchiveVersion([string]$Archive, [string]$Version, [string]$Commit) {
    $null = Get-SnapBarVersion "v$Version"
    $temp = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts/checks/version/' + [guid]::NewGuid().ToString('N'))
    $null = [IO.Directory]::CreateDirectory($temp)
    $zip = $null
    try {
        $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
        foreach ($relative in @('SnapBar.App.dll', 'Probe/SnapBar.Probe.dll')) {
            $entries = @($zip.Entries | Where-Object { $_.FullName -ceq $relative })
            if ($entries.Count -ne 1) { throw "Missing or duplicate versioned ZIP entry: $relative" }
            $path = Join-Path $temp ([IO.Path]::GetFileName($relative))
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $path)
            Assert-SnapBarAssemblyVersion $path $Version $Commit
        }
    }
    finally {
        if ($zip) { $zip.Dispose() }
        # Only the two explicitly created files; never recursively delete a computed path.
        foreach ($name in @('SnapBar.App.dll', 'SnapBar.Probe.dll')) {
            $path = Join-Path $temp $name
            if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
        }
        [IO.Directory]::Delete($temp)
    }
}
