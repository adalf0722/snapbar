param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
$start = [System.Diagnostics.ProcessStartInfo]::new([System.IO.Path]::GetFullPath($Executable))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WorkingDirectory = [Environment]::SystemDirectory
foreach ($name in @($start.Environment.Keys)) {
    if ($name -match '^(DOTNET_|COREHOST_)') { $start.Environment.Remove($name) | Out-Null }
}
$start.Environment['DOTNET_DISABLE_GUI_ERRORS'] = '1'
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.StandardInputEncoding = [System.Text.UTF8Encoding]::new($false)
$start.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
$probe = [System.Diagnostics.Process]::Start($start)
try {
    $cases = @(
        @{ Input = '{"Operation":"ping"}'; Expected = 'ok' },
        @{ Input = '{"Operation":"invalid"}'; Expected = 'invalid-request' },
        @{ Input = '{bad json'; Expected = 'invalid-request' },
        @{ Input = '{"Operation":"capture","Window":0}'; Expected = 'target-changed' },
        @{ Input = '{"Operation":"validate","SessionId":"expired"}'; Expected = 'expired' },
        @{ Input = '{"Operation":"clipboard-extend","SessionId":"expired","LeaseSeconds":60}'; Expected = 'expired' },
        @{ Input = '{"Operation":"clipboard-extend","LeaseSeconds":0}'; Expected = 'invalid-request' },
        @{ Input = '{"Operation":"clipboard-extend","LeaseSeconds":311}'; Expected = 'invalid-request' },
        @{ Input = '{"Operation":"clipboard-release","SessionId":"expired"}'; Expected = 'expired' }
    )
    foreach ($case in $cases) {
        $probe.StandardInput.WriteLine($case.Input)
        $probe.StandardInput.Flush()
        $pending = $probe.StandardOutput.ReadLineAsync()
        if (-not $pending.Wait(5000)) { throw 'Probe response timed out' }
        $response = $pending.Result | ConvertFrom-Json
        if ($response.Status -ne $case.Expected) { throw "Expected $($case.Expected), got $($response.Status)" }
        if ($null -ne $response.Selection) { throw 'Unexpected selection data in protocol smoke test' }
        Write-Output "PASS probe protocol: $($case.Expected)"
    }
    $probe.StandardInput.Close()
    if (-not $probe.WaitForExit(3000)) { throw 'Probe failed to exit on EOF' }
    if ($probe.ExitCode -ne 0) { throw 'Probe exited with an error' }
    Write-Output 'PASS probe shutdown on EOF'
}
finally {
    if (-not $probe.HasExited) { $probe.Kill($true) }
    $probe.Dispose()
}
