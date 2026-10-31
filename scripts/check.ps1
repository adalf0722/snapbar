param([switch]$Staged)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Push-Location $script:SnapBarRepo
try {
    if ($Staged) {
        $names = & git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR -z
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect index.' }
        $files = @($names -split "`0" | Where-Object { $_ })
    }
    else { $files = Get-SnapBarFiles }
    foreach ($file in $files) {
        if ($file -notmatch '\.(md|ps1)$') { continue }
        if ($Staged) {
            $start = [Diagnostics.ProcessStartInfo]::new('git')
            $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true
            $start.ArgumentList.Add('cat-file'); $start.ArgumentList.Add('blob'); $start.ArgumentList.Add(":$file")
            $process = [Diagnostics.Process]::Start($start)
            $stream = [IO.MemoryStream]::new()
            try {
                $process.StandardOutput.BaseStream.CopyTo($stream); $process.WaitForExit()
                if ($process.ExitCode -ne 0) { throw "Cannot read index blob: $file" }
                $bytes = $stream.ToArray()
            }
            finally { $stream.Dispose(); $process.Dispose() }
        }
        else {
            if (-not [IO.File]::Exists($file)) { continue }
            $bytes = [IO.File]::ReadAllBytes($file)
        }
        $content = ConvertFrom-SnapBarUtf8 $bytes $file
        if ($file -match '\.ps1$') { Assert-SnapBarScript $content $file }
    }
    & git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Index whitespace check failed.' }
    if (-not $Staged) {
        & git diff --check
        if ($LASTEXITCODE -ne 0) { throw 'Working tree whitespace check failed.' }
        & (Join-Path $PSScriptRoot 'test-tooling.ps1')
        & (Join-Path $PSScriptRoot 'build.ps1')
    }
    Write-Host 'Repository checks passed.'
}
finally { Pop-Location }
