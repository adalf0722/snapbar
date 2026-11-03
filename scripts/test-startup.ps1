param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($Executable))
$start.ArgumentList.Add('--startup-check')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardError = $true
$start.RedirectStandardOutput = $true
$start.WorkingDirectory = [Environment]::SystemDirectory
foreach ($name in @($start.Environment.Keys)) {
    if ($name -match '^(DOTNET_|COREHOST_)') { $start.Environment.Remove($name) | Out-Null }
}
$start.Environment['DOTNET_DISABLE_GUI_ERRORS'] = '1'
$process = [Diagnostics.Process]::Start($start)
try {
    $errorText = $process.StandardError.ReadToEndAsync()
    $outputText = $process.StandardOutput.ReadToEndAsync()
    if (-not $process.WaitForExit(10000)) { throw 'Standalone startup check timed out' }
    if ($process.ExitCode -ne 0) { throw "Standalone startup failed ($($process.ExitCode)): $($errorText.GetAwaiter().GetResult())" }
    Write-Output 'PASS app starts without DOTNET_ROOT, development runtime overrides or the repository working directory'
}
finally {
    # This process runs only the side-effect-free --startup-check entry point.
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(1000) | Out-Null }
    $process.Dispose()
}
