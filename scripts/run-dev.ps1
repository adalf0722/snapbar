param([string]$AppDirectory)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not $AppDirectory) {
    $AppDirectory = Join-Path $script:SnapBarRepo 'src/SnapBar.App/bin/Release/net10.0-windows'
}
$app = Join-Path ($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($AppDirectory)) 'SnapBar.App.exe'
$probe = Join-Path (Split-Path $app -Parent) 'Probe/SnapBar.Probe.exe'
foreach ($file in @($app, $probe)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Missing development output: $file. Run scripts/build.ps1 first or pass a prepared directory with -AppDirectory."
    }
}
if (Test-SnapBarInstanceRunning) {
    throw 'SnapBar is already running. Exit it from the system tray or settings, then rerun this command.'
}

$sdk = Get-SnapBarSdk
$oldRoot = $env:DOTNET_ROOT
try {
    $env:DOTNET_ROOT = Split-Path $sdk -Parent
    Write-Host "Launching $app"
    Start-Process -FilePath $app -WorkingDirectory (Split-Path $app -Parent)
}
finally { $env:DOTNET_ROOT = $oldRoot }
