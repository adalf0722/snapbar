param([switch]$Staged, [switch]$RequireActionRuntimes, [switch]$SkipDesktop)
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
    $releaseCatalogPath = 'packaging/RELEASE-NOTE-TRANSLATIONS.json'
    $releaseCatalog = $null
    foreach ($file in $files) {
        $releaseNote = $file -match '^release-notes/.*\.json$'
        if ($file -notmatch '\.(md|ps1)$' -and -not $releaseNote -and $file -cne $releaseCatalogPath) { continue }
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
            $note = ConvertFrom-SnapBarReleaseNote $content $file
            if (-not $note.en) {
                if ($null -eq $releaseCatalog) {
                    $catalogBytes = if ($Staged) { Read-SnapBarIndexBlob $releaseCatalogPath } else { [IO.File]::ReadAllBytes((Join-Path $script:SnapBarRepo $releaseCatalogPath)) }
                    $releaseCatalog = ConvertFrom-SnapBarReleaseCatalog (ConvertFrom-SnapBarUtf8 $catalogBytes $releaseCatalogPath)
                }
                $english = $releaseCatalog[[IO.Path]::GetFileName($file)]
                if ($null -eq $english) { throw "New release notes require reviewed en content: $file" }
                if ($english.highlights.Count -ne $note.highlights.Count) { throw "Bilingual highlights must correspond: $file" }
            }
        }
        if ($file -ceq $releaseCatalogPath) { $releaseCatalog = ConvertFrom-SnapBarReleaseCatalog $content }
    }
    & git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Index whitespace check failed.' }
    Invoke-SnapBarWorkflowCheck -ChangedFiles $workflowChanges -Staged:$Staged
    if (-not $Staged) {
        & git diff --check
        if ($LASTEXITCODE -ne 0) { throw 'Working tree whitespace check failed.' }
        & (Join-Path $PSScriptRoot 'test-push.ps1')
        & (Join-Path $PSScriptRoot 'test-release-notes.ps1')
        & (Join-Path $PSScriptRoot 'test-release-baseline.ps1')
        & (Join-Path $PSScriptRoot 'test-site.ps1')
        if (-not $SkipDesktop) {
            & (Join-Path $PSScriptRoot 'test-version.ps1')
            & (Join-Path $PSScriptRoot 'test-tooling.ps1')
            & (Join-Path $PSScriptRoot 'test-auto-release.ps1')
            & (Join-Path $PSScriptRoot 'build.ps1')
            $run = New-SnapBarRun 'action-runner'
            $result = Invoke-SnapBarActionRunner $run -RequireRuntimes:$RequireActionRuntimes
            Write-SnapBarJson (Join-Path $run 'report.json') $result
            Assert-SnapBarCommand $result
            if ($RequireActionRuntimes) { Assert-SnapBarActionRunnerEvidence $result }
        }
    }
    Write-Host 'Repository checks passed.'
}
finally { Pop-Location }

# GitHub Actions reads LASTEXITCODE, which may still reflect an expected test failure.
exit 0
