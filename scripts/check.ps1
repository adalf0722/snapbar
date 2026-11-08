param([switch]$Staged)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'workflow-checks.ps1')
. (Join-Path $PSScriptRoot 'release-notes.ps1')
Push-Location $script:SnapBarRepo
try {
    if ($Staged) {
        $names = & git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR -z
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect index.' }
        $files = @($names -split "`0" | Where-Object { $_ })
        $changedNames = & git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMRD -z
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect workflow changes.' }
        $workflowChanges = @($changedNames -split "`0" | Where-Object { $_ })
    }
    else { $files = Get-SnapBarFiles; $workflowChanges = $files }
    foreach ($file in $files) {
        $releaseNote = $file -match '^release-notes/.*\.json$'
        if ($file -notmatch '\.(md|ps1)$' -and -not $releaseNote) { continue }
        if ($Staged) {
            $bytes = Read-SnapBarIndexBlob $file
        }
        else {
            if (-not [IO.File]::Exists($file)) { continue }
            $bytes = [IO.File]::ReadAllBytes($file)
        }
        $content = ConvertFrom-SnapBarUtf8 $bytes $file
        if ($file -match '\.ps1$') { Assert-SnapBarScript $content $file }
        if ($releaseNote) {
            if ($file -cnotmatch '^release-notes/[a-z0-9][a-z0-9-]*\.json$') { throw "Invalid release note filename: $file" }
            $null = ConvertFrom-SnapBarReleaseNote $content $file
        }
    }
    & git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Index whitespace check failed.' }
    Invoke-SnapBarWorkflowCheck -ChangedFiles $workflowChanges -Staged:$Staged
    if (-not $Staged) {
        & git diff --check
        if ($LASTEXITCODE -ne 0) { throw 'Working tree whitespace check failed.' }
        & (Join-Path $PSScriptRoot 'test-tooling.ps1')
        & (Join-Path $PSScriptRoot 'test-push.ps1')
        & (Join-Path $PSScriptRoot 'test-auto-release.ps1')
        & (Join-Path $PSScriptRoot 'build.ps1')
    }
    Write-Host 'Repository checks passed.'
}
finally { Pop-Location }
