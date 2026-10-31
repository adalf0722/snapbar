param(
    [switch]$List,
    [ValidateSet('Release', 'All', 'Lifecycle', 'Appearance', 'Compatibility', 'CompatibilitySlow', 'Clipboard', 'ClipboardRaces', 'UiaReadiness', 'UiaDeepTree', 'Actions', 'ActionRunner', 'Diagnostics', 'Layout', 'Keyboard', 'PathWorker', 'PathActions', 'WebActions', 'Exclusions', 'AiRuntime', 'AiPanel')]
    [string[]]$Suite = @('Release')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$catalog = [ordered]@{
    Lifecycle = '--tray-lifecycle'; Appearance = '--ui-appearance'; Compatibility = '--clipboard-compatibility'
    CompatibilitySlow = '--clipboard-compatibility-long'; Clipboard = '--clipboard-roundtrip'; ClipboardRaces = '--clipboard-races'
    UiaReadiness = '--uia-readiness'; UiaDeepTree = '--uia-deep-tree'; Actions = '--custom-actions'
    ActionRunner = '--custom-action-runner'; Diagnostics = '--action-diagnostics'; Layout = '--action-layout'
    Keyboard = '--menu-keyboard'; PathWorker = '--path-worker-tests'; PathActions = '--path-actions'
    WebActions = '--web-actions'; Exclusions = '--app-exclusions'; AiRuntime = '--ai-runtime'; AiPanel = '--ai-panel'
}
if ($List) {
    Write-Host 'Release = Lifecycle, Appearance, CompatibilitySlow, AiRuntime, AiPanel (includes a real 35-second script).'
    Write-Host 'All = every isolated suite below, except redundant fast Compatibility. Tests run serially.'
    $catalog.GetEnumerator() | Format-Table Key, Value -AutoSize
    Write-Host 'External Sublime/browser/Explorer smoke tests: test runner --list-tests; see docs/development.md.'
    exit 0
}
$selected = [Collections.Generic.List[string]]::new()
foreach ($name in $Suite) {
    $expanded = switch ($name) {
        'Release' { @('Lifecycle', 'Appearance', 'CompatibilitySlow', 'AiRuntime', 'AiPanel') }
        'All' { @($catalog.Keys | Where-Object { $_ -ne 'Compatibility' }) }
        default { @($name) }
    }
    foreach ($item in $expanded) { if (-not $selected.Contains($item)) { $selected.Add($item) } }
}
$run = New-SnapBarRun 'desktop'
$results = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ schema = 1; startedUtc = [DateTime]::UtcNow.ToString('o'); sourceFingerprint = Get-SnapBarFingerprint; success = $false; suites = @($selected); results = @(); error = $null }
$sdk = Get-SnapBarSdk
$oldRoot = $env:DOTNET_ROOT
$env:DOTNET_ROOT = Split-Path $sdk -Parent
$mutex = [Threading.Mutex]::new($false, 'Local\SnapBar.DesktopTests')
$ownsMutex = $false
Push-Location $script:SnapBarRepo
try {
    try { $ownsMutex = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (-not $ownsMutex) { throw 'Another desktop test run is active. Clipboard suites must run serially.' }
    $build = Invoke-SnapBarCommand 'Build' (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'build.ps1')) $run
    $results.Add($build); Assert-SnapBarCommand $build
    $runner = Join-Path $script:SnapBarRepo 'tests/SnapBar.Windows.Tests/bin/Release/net10.0-windows/SnapBar.Windows.Tests.dll'
    $preflight = Invoke-SnapBarCommand 'Preflight' $sdk @($runner, '--desktop-preflight') $run
    $results.Add($preflight); Assert-SnapBarCommand $preflight
    Write-Host 'Desktop tests temporarily use the clipboard and foreground windows. Avoid typing/copying until completion.'
    foreach ($name in $selected) {
        $result = Invoke-SnapBarCommand $name $sdk @($runner, $catalog[$name]) $run
        $results.Add($result); Assert-SnapBarCommand $result
    }
    if ($report.sourceFingerprint -ne (Get-SnapBarFingerprint)) { throw 'Source changed during desktop tests; results cannot authorize publishing.' }
    $report.success = $true
}
catch { $report.error = $_.Exception.Message; throw }
finally {
    $report.results = @($results.ToArray()); $report.finishedUtc = [DateTime]::UtcNow.ToString('o')
    Write-SnapBarJson (Join-Path $run 'report.json') $report
    Copy-Item -LiteralPath (Join-Path $run 'report.json') -Destination (Join-Path (Split-Path $run -Parent) 'latest.json') -Force
    Write-Host "Desktop report: $run/report.json"
    $env:DOTNET_ROOT = $oldRoot
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
    Pop-Location
}
