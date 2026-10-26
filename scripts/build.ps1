param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $repo '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $repo
try {
    & $sdk build SnapBar.slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & $sdk run --project tests/SnapBar.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    $appOutput = Join-Path $repo 'src\SnapBar.App\bin\Release\net10.0-windows'
    $probeOutput = Join-Path $repo 'src\SnapBar.Probe\bin\Release\net10.0-windows'
    $probeDestination = Join-Path $appOutput 'Probe'
    New-Item -ItemType Directory -Force -Path $probeDestination | Out-Null
    Copy-Item -Path (Join-Path $probeOutput '*') -Destination $probeDestination -Recurse -Force
    if ($Publish) {
        & $sdk publish src/SnapBar.App -c Release -r win-x64 --self-contained true -o artifacts/p0 --nologo
        if ($LASTEXITCODE -ne 0) { throw 'App publish failed' }
        & $sdk publish src/SnapBar.Probe -c Release -r win-x64 --self-contained true -o artifacts/p0/Probe --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Probe publish failed' }
        & (Join-Path $PSScriptRoot 'test-probe.ps1') -Executable (Join-Path $repo 'artifacts\p0\Probe\SnapBar.Probe.exe')
        Copy-Item -LiteralPath (Join-Path $repo 'packaging\README.md') -Destination (Join-Path $repo 'artifacts\p0\README.md') -Force
        Copy-Item -LiteralPath (Join-Path $repo 'docs\p0-validation.md') -Destination (Join-Path $repo 'artifacts\p0\VALIDATION.md') -Force
        Copy-Item -LiteralPath (Join-Path $repo 'docs\2026-10-26-clipboard-backup-recovery.md') -Destination (Join-Path $repo 'artifacts\p0\CLIPBOARD-RECOVERY.md') -Force
    }
}
finally { Pop-Location }
