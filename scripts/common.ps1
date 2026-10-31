$script:SnapBarRepo = Split-Path $PSScriptRoot -Parent

function Get-SnapBarSdk {
    $bundled = Join-Path $script:SnapBarRepo '.tools/dotnet/dotnet.exe'
    if (Test-Path -LiteralPath $bundled) { return $bundled }
    return (Get-Command dotnet -ErrorAction Stop).Source
}

function Write-SnapBarJson([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}

function ConvertFrom-SnapBarUtf8([byte[]]$Bytes, [string]$Name) {
    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 239 -and $Bytes[1] -eq 187 -and $Bytes[2] -eq 191) { throw "UTF-8 BOM is not allowed: $Name" }
    try { return [Text.UTF8Encoding]::new($false, $true).GetString($Bytes) }
    catch { throw "Invalid UTF-8: $Name" }
}

function Assert-SnapBarScript([string]$Content, [string]$Name) {
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseInput($Content, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw "PowerShell syntax error in $Name`: $($errors[0].Message)" }
}

function New-SnapBarRun([string]$Kind) {
    $parent = Join-Path $script:SnapBarRepo "artifacts/checks/$Kind"
    $path = Join-Path $parent ((Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($path) | Out-Null
    return $path
}

function Get-SnapBarFiles {
    $files = & git -C $script:SnapBarRepo -c core.quotepath=false ls-files --cached --others --exclude-standard -z
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository files.' }
    return @($files -split "`0" | Where-Object { $_ } | Sort-Object -Unique)
}

function Get-SnapBarFingerprint {
    $files = @(Get-SnapBarFiles | Where-Object {
        $_ -match '^(src/|tests/|scripts/|\.github/|\.githooks/)' -or $_ -match '^[^/]+\.(props|targets|json|slnx|config)$'
    })
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        foreach ($file in $files) {
            $path = Join-Path $script:SnapBarRepo $file
            if (-not [IO.File]::Exists($path)) { continue }
            $bytes = [IO.File]::ReadAllBytes($path)
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes($file + "`0" + $bytes.Length + "`0"))
            $hash.AppendData($bytes)
        }
        return [Convert]::ToHexString($hash.GetHashAndReset())
    }
    finally { $hash.Dispose() }
}

function Invoke-SnapBarCommand([string]$Name, [string]$Executable, [string[]]$Arguments, [string]$Directory) {
    $log = Join-Path $Directory "$Name.log"
    $writer = [IO.StreamWriter]::new($log, $false, [Text.UTF8Encoding]::new($false))
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $lines = [Collections.Generic.List[string]]::new()
    $exitCode = 1
    $PSNativeCommandUseErrorActionPreference = $false
    Write-Host "RUNNING: $Name"
    try {
        & $Executable @Arguments 2>&1 | ForEach-Object {
            $line = "$_"; $writer.WriteLine($line); $writer.Flush(); $lines.Add($line)
            if ($line -match '^(WAITING|RUNNING|FAIL)') { Write-Host $line }
        }
        $exitCode = $LASTEXITCODE
    }
    catch { $writer.WriteLine($_.Exception.Message); Write-Host $_.Exception.Message }
    finally { $writer.Dispose(); $timer.Stop() }
    $passed = @($lines | Where-Object { $_ -match '^PASS\b' }).Count
    $failed = @($lines | Where-Object { $_ -match '^FAIL\b' }).Count
    foreach ($line in $lines) {
        if ($line -match '^(\d+) passed, (\d+) failed') { $passed = [int]$Matches[1]; $failed = [int]$Matches[2] }
    }
    Write-Host "$Name`: exit $exitCode; $passed passed, $failed failed; log: $log"
    if ($exitCode -ne 0) { $lines | Select-Object -Last 12 | ForEach-Object { Write-Host $_ } }
    return [pscustomobject]@{ name = $Name; exitCode = $exitCode; success = ($exitCode -eq 0 -and $failed -eq 0); passed = $passed; failed = $failed; seconds = [math]::Round($timer.Elapsed.TotalSeconds, 3); log = $log }
}

function Assert-SnapBarCommand($Result) {
    if (-not $Result.success) { throw "$($Result.name) failed. See $($Result.log)" }
}

function Assert-SnapBarPublishIdle([string]$Package, [object[]]$Processes = @()) {
    if (-not $PSBoundParameters.ContainsKey('Processes')) {
        $Processes = @(Get-Process -Name SnapBar.App, SnapBar.Probe -ErrorAction SilentlyContinue)
    }
    $prefix = [IO.Path]::GetFullPath($Package).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    foreach ($process in $Processes) {
        try { $path = $process.Path } catch { $path = $null }
        if (-not $path -or [IO.Path]::GetFullPath($path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Publish blocked: $($process.ProcessName) (PID $($process.Id)) is running from the package or its path cannot be inspected. Exit SnapBar from its tray/settings and retry. No package files were replaced."
        }
    }
}

function Assert-SnapBarDesktopReport([string]$Path, [string]$Fingerprint) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Publish requires desktop evidence: run scripts/test-desktop.ps1 -Suite Release first.' }
    $report = Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
    if ($report.schema -ne 1 -or $report.success -isnot [bool] -or -not $report.success -or $report.sourceFingerprint -ne $Fingerprint) {
        throw 'Desktop evidence failed or is stale. Run scripts/test-desktop.ps1 -Suite Release against the current source.'
    }
    foreach ($suite in @('Lifecycle', 'Appearance', 'CompatibilitySlow')) {
        $results = @($report.results | Where-Object { $_.name -eq $suite })
        if ($results.Count -ne 1 -or $results[0].success -isnot [bool] -or -not $results[0].success -or $results[0].exitCode -ne 0) { throw "Desktop evidence is missing a successful $suite suite." }
    }
}

function Assert-SnapBarArtifactPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath((Join-Path $script:SnapBarRepo 'artifacts')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe artifact path: $full" }
    # Reject junctions/symlinks: lexical containment alone does not establish filesystem containment.
    $current = $full
    while ($current -and $current.StartsWith($root.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Artifact path traverses a link: $current" }
        }
        $current = Split-Path $current -Parent
    }
    return $full
}

function Move-SnapBarPackage([string]$Stage, [string]$Destination, [string]$Backup) {
    $Stage = Assert-SnapBarArtifactPath $Stage
    $Destination = Assert-SnapBarArtifactPath $Destination
    $Backup = Assert-SnapBarArtifactPath $Backup
    if (-not [IO.Directory]::Exists($Stage) -or [IO.Directory]::Exists($Backup) -or $Stage -eq $Destination -or $Backup -eq $Destination) { throw 'Invalid publish staging directories.' }
    $movedOld = $false
    try {
        if ([IO.Directory]::Exists($Destination)) { [IO.Directory]::Move($Destination, $Backup); $movedOld = $true }
        [IO.Directory]::Move($Stage, $Destination)
    }
    catch {
        if ($movedOld -and -not [IO.Directory]::Exists($Destination)) { [IO.Directory]::Move($Backup, $Destination) }
        throw
    }
}
