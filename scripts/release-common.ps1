function Get-SnapBarRequiredNoticeFiles {
    $files = @(
        'LICENSE', 'THIRD-PARTY-NOTICES.md', 'Licenses/README.md',
        'Assets/FluentIcons/LICENSE.txt', 'Assets/FluentIcons/NOTICE.txt', 'Assets/FluentIcons/SOURCE.md',
        'OcrData/TESSERACT-LICENSE.txt', 'OcrData/LEPTONICA-LICENSE.txt', 'OcrData/INTEROPDOTNET-LICENSE.txt', 'OcrData/SOURCE.md',
        'Licenses/Ocr/GIFLIB-LICENSE.txt', 'Licenses/Ocr/LIBJPEG-TURBO-LICENSE.md', 'Licenses/Ocr/README.ijg',
        'Licenses/Ocr/LIBPNG-LICENSE.txt', 'Licenses/Ocr/LIBTIFF-LICENSE.txt', 'Licenses/Ocr/LIBTIFF-SOURCE-NOTICES.txt', 'Licenses/Ocr/ZLIB-README.txt',
        'Licenses/Ocr/LIBLZMA-COPYING.txt', 'Licenses/Ocr/SOURCES.json',
        'Licenses/WindowsDesktop/WPF-THIRD-PARTY-NOTICES.txt', 'Licenses/WindowsDesktop/WINFORMS-THIRD-PARTY-NOTICES.txt',
        'Licenses/WindowsDesktop/SOURCES.json'
    )
    foreach ($prefix in @('', 'Probe/')) {
        $files += "${prefix}Licenses/DotNet/PACKAGES.txt"
        $files += "${prefix}Licenses/DotNet/Microsoft.NETCore.App.Runtime.win-x64/LICENSE.txt"
        $files += "${prefix}Licenses/DotNet/Microsoft.NETCore.App.Runtime.win-x64/THIRD-PARTY-NOTICES.txt"
        $files += "${prefix}Licenses/DotNet/Microsoft.WindowsDesktop.App.Runtime.win-x64/LICENSE.txt"
    }
    return $files
}

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
    $required += Get-SnapBarRequiredNoticeFiles
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
