function New-SnapBarReleaseArchive {
    param(
        [Parameter(Mandatory)][string]$Package,
        [Parameter(Mandatory)]
        [ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$')]
        [string]$Version,
        [Parameter(Mandatory)][string]$OutputDirectory
    )
    $Package = Assert-SnapBarArtifactPath $Package
    $OutputDirectory = Assert-SnapBarArtifactPath $OutputDirectory
    $required = @(
        'SnapBar.App.exe', 'SnapBar.App.dll', 'SnapBar.App.deps.json', 'SnapBar.App.runtimeconfig.json',
        'coreclr.dll', 'hostfxr.dll', 'wpfgfx_cor3.dll', 'README.md', 'VALIDATION.md', 'CLIPBOARD-RECOVERY.md',
        'README.en.md', 'zh-Hant/SnapBar.App.resources.dll',
        'Probe/SnapBar.Probe.exe', 'Probe/SnapBar.Probe.dll', 'Probe/SnapBar.Probe.deps.json',
        'Probe/SnapBar.Probe.runtimeconfig.json', 'Probe/coreclr.dll', 'Probe/hostfxr.dll'
    )
    foreach ($file in $required) {
        $path = Join-Path $Package $file
        if (-not [IO.File]::Exists($path) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Release package is missing a nonempty file: $file"
        }
    }
    # Prevent a package junction from including files outside the staged package.
    foreach ($item in Get-ChildItem -LiteralPath $Package -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Release package contains a link: $($item.Name)" }
    }
    if ([IO.Directory]::Exists($OutputDirectory) -or [IO.File]::Exists($OutputDirectory)) {
        throw 'Release output already exists; use a fresh output directory.'
    }
    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $archive = Join-Path $OutputDirectory "SnapBar-$Version-win-x64.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($Package, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($file in $required) {
            $entry = $zip.GetEntry($file)
            if (-not $entry -or $entry.Length -eq 0) { throw "Release ZIP is missing: $file" }
        }
        $files = @(Get-ChildItem -LiteralPath $Package -Recurse -File -Force)
        if (@($zip.Entries | Where-Object { $_.Name }).Count -ne $files.Count) { throw 'Release ZIP file count differs from the package.' }
    }
    finally { $zip.Dispose() }
    $checksum = "$archive.sha256"
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksum, "$hash  $([IO.Path]::GetFileName($archive))`n", [Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{ archive = $archive; checksum = $checksum; directory = $OutputDirectory }
}
