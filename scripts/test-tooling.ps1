$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'release-common.ps1')
$run = New-SnapBarRun 'tooling'
$script:toolingPassed = 0
function Check-Tooling([string]$Name, [scriptblock]$Test) {
    & $Test
    $script:toolingPassed++
    Write-Host "PASS $Name"
}
function Expect-ToolingFailure([scriptblock]$Action) {
    $caught = $false
    try { & $Action | Out-Null } catch { $caught = $true }
    if (-not $caught) { throw 'Expected rejection did not occur.' }
}
function Assert-Tooling([bool]$Value) { if (-not $Value) { throw 'Tooling assertion failed.' } }

Check-Tooling 'valid Unicode text is preserved' {
    Assert-Tooling ((ConvertFrom-SnapBarUtf8 ([byte[]]@(228, 184, 173)) 'fixture') -eq [string][char]0x4E2D)
}
Check-Tooling 'BOM and malformed UTF-8 are rejected' {
    Expect-ToolingFailure { ConvertFrom-SnapBarUtf8 ([byte[]]@(239, 187, 191, 65)) 'fixture' }
    Expect-ToolingFailure { ConvertFrom-SnapBarUtf8 ([byte[]]@(255)) 'fixture' }
}
Check-Tooling 'PowerShell parser accepts valid code and rejects incomplete code' {
    Assert-SnapBarScript '$value = 1' 'fixture'
    Expect-ToolingFailure { Assert-SnapBarScript 'if (' 'fixture' }
}
Check-Tooling 'fingerprint includes new nonignored source files and file contents' {
    $before = Get-SnapBarFingerprint
    $fixture = Join-Path $PSScriptRoot ('.fingerprint-' + [guid]::NewGuid().ToString('N') + '.ps1')
    try {
        [IO.File]::WriteAllText($fixture, '# first', [Text.UTF8Encoding]::new($false))
        $first = Get-SnapBarFingerprint
        [IO.File]::WriteAllText($fixture, '# second', [Text.UTF8Encoding]::new($false))
        Assert-Tooling ($before -ne $first -and $first -ne (Get-SnapBarFingerprint))
    }
    finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture } }
    Assert-Tooling ($before -eq (Get-SnapBarFingerprint))
}
$package = Join-Path $run 'package'
Check-Tooling 'package process blocks publishing even without a visible window' {
    Expect-ToolingFailure { Assert-SnapBarPublishIdle $package -Processes @([pscustomobject]@{ Path = Join-Path $package 'SnapBar.App.exe'; ProcessName = 'SnapBar.App'; Id = 123; MainWindowHandle = 0 }) }
    Expect-ToolingFailure { Assert-SnapBarPublishIdle $package -Processes @([pscustomobject]@{ Path = Join-Path $package 'Probe/SnapBar.Probe.exe'; ProcessName = 'SnapBar.Probe'; Id = 124 }) }
    Expect-ToolingFailure { Assert-SnapBarPublishIdle $package -Processes @([pscustomobject]@{ Path = $null; ProcessName = 'SnapBar.App'; Id = 125 }) }
    Assert-SnapBarPublishIdle $package -Processes @([pscustomobject]@{ Path = Join-Path $run 'other/SnapBar.App.exe'; ProcessName = 'SnapBar.App'; Id = 126 })
}
$evidence = Join-Path $run 'desktop.json'
Check-Tooling 'Release build detects App and Probe output locks before MSBuild' {
    $appOutput = Join-Path $script:SnapBarRepo 'src/SnapBar.App/bin/Release/net10.0-windows'
    $probeOutput = Join-Path $script:SnapBarRepo 'src/SnapBar.Probe/bin/Release/net10.0-windows'
    Expect-ToolingFailure { Assert-SnapBarBuildIdle -Processes @([pscustomobject]@{ Path = Join-Path $appOutput 'SnapBar.App.exe'; ProcessName = 'SnapBar.App'; Id = 123 }) }
    Expect-ToolingFailure { Assert-SnapBarBuildIdle -Processes @([pscustomobject]@{ Path = Join-Path $probeOutput 'SnapBar.Probe.exe'; ProcessName = 'SnapBar.Probe'; Id = 124 }) }
    Assert-SnapBarBuildIdle -Processes @([pscustomobject]@{ Path = Join-Path "$appOutput-other" 'SnapBar.App.exe'; ProcessName = 'SnapBar.App'; Id = 125 })
}
$valid = @{ schema = 1; success = $true; sourceFingerprint = 'current'; results = @($script:SnapBarReleaseSuites) | ForEach-Object { @{ name = $_; success = $true; exitCode = 0; passed = 1; failed = 0; skipped = 0; runtimesRequired = ($_ -eq 'ActionRunner') } } }
Check-Tooling 'publish accepts complete current desktop evidence' {
    Write-SnapBarJson $evidence $valid
    Assert-SnapBarDesktopReport $evidence 'current'
}
Check-Tooling 'publish rejects missing optional skipped and legacy ActionRunner evidence' {
    foreach ($variant in @('missing', 'optional', 'skipped', 'legacy')) {
        $report = $valid | ConvertTo-Json -Depth 10 | ConvertFrom-Json
        $entry = $report.results | Where-Object name -eq 'ActionRunner'
        switch ($variant) {
            'missing' { $report.results = @($report.results | Where-Object name -ne 'ActionRunner') }
            'optional' { $entry.runtimesRequired = $false }
            'skipped' { $entry.skipped = 1 }
            'legacy' { $entry.PSObject.Properties.Remove('skipped') }
        }
        Write-SnapBarJson $evidence $report
        Expect-ToolingFailure { Assert-SnapBarDesktopReport $evidence 'current' }
    }
}
Check-Tooling 'native command records skips separately from failures' {
    $result = Invoke-SnapBarCommand 'SkippedFixture' (Get-Command pwsh).Source @('-NoProfile', '-Command', "Write-Output 'SKIP fixture'; Write-Output '1 passed, 0 failed'; exit 0") $run
    Assert-Tooling ($result.success -and $result.passed -eq 1 -and $result.failed -eq 0 -and $result.skipped -eq 1)
}
Check-Tooling 'publish rejects missing stale failed or incomplete evidence' {
    Expect-ToolingFailure { Assert-SnapBarDesktopReport (Join-Path $run 'missing.json') 'current' }
    Expect-ToolingFailure { Assert-SnapBarDesktopReport $evidence 'changed' }
    $valid.success = 'false'; Write-SnapBarJson $evidence $valid
    Expect-ToolingFailure { Assert-SnapBarDesktopReport $evidence 'current' }
    $valid.success = $false; Write-SnapBarJson $evidence $valid
    Expect-ToolingFailure { Assert-SnapBarDesktopReport $evidence 'current' }
    $valid.success = $true; $valid.results = @($valid.results | Where-Object { $_.name -ne 'Lookup' }); Write-SnapBarJson $evidence $valid
    Expect-ToolingFailure { Assert-SnapBarDesktopReport $evidence 'current' }
}
Check-Tooling 'publish rejects evidence without bilingual desktop validation' {
    $withoutLanguage = @{ schema = 1; success = $true; sourceFingerprint = 'current'; results = @($script:SnapBarReleaseSuites | Where-Object { $_ -ne 'Language' }) | ForEach-Object { @{ name = $_; success = $true; exitCode = 0 } } }
    Write-SnapBarJson $evidence $withoutLanguage
    Expect-ToolingFailure { Assert-SnapBarDesktopReport $evidence 'current' }
}
Check-Tooling 'artifact move guard rejects paths outside artifacts' {
    Expect-ToolingFailure { Assert-SnapBarArtifactPath (Join-Path $script:SnapBarRepo 'src') }
    Expect-ToolingFailure { Assert-SnapBarArtifactPath (Join-Path $script:SnapBarRepo 'artifacts/../src') }
}
Check-Tooling 'failed promotion rolls the original package back' {
    $nested = Join-Path $package 'next'
    [IO.Directory]::CreateDirectory($nested) | Out-Null
    [IO.File]::WriteAllText((Join-Path $package 'original.txt'), 'original', [Text.UTF8Encoding]::new($false))
    # Moving the parent relocates the stage; its second rename fails after the old package moved.
    Expect-ToolingFailure { Move-SnapBarPackage $nested $package (Join-Path $run 'rollback') }
    Assert-Tooling ([IO.File]::ReadAllText((Join-Path $package 'original.txt')) -eq 'original')
    Assert-Tooling (-not (Test-Path -LiteralPath (Join-Path $run 'rollback')))
}
Check-Tooling 'successful promotion retains the previous package' {
    $stage = Join-Path $run 'new'
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    [IO.File]::WriteAllText((Join-Path $stage 'new.txt'), 'new', [Text.UTF8Encoding]::new($false))
    $backup = Join-Path $run 'previous'
    Move-SnapBarPackage $stage $package $backup
    Assert-Tooling ((Test-Path -LiteralPath (Join-Path $package 'new.txt')) -and (Test-Path -LiteralPath (Join-Path $backup 'original.txt')))
}
Check-Tooling 'native command failure is logged and remains a failure' {
    $result = Invoke-SnapBarCommand 'ExpectedFailure' (Get-Command pwsh).Source @('-NoProfile', '-Command', 'Write-Output fixture; exit 7') $run
    Assert-Tooling ($result.exitCode -eq 7 -and -not $result.success -and (Test-Path -LiteralPath $result.log))
    Expect-ToolingFailure { Assert-SnapBarCommand $result }
}
Check-Tooling 'staged checks inspect index blobs even when working files are repaired' {
    $oldIndex = $env:GIT_INDEX_FILE
    $indexPath = & git -C $script:SnapBarRepo rev-parse --git-path index
    if (-not [IO.Path]::IsPathRooted($indexPath)) { $indexPath = Join-Path $script:SnapBarRepo $indexPath }
    $temporaryIndex = Join-Path $run 'index'
    Copy-Item -LiteralPath $indexPath -Destination $temporaryIndex
    $env:GIT_INDEX_FILE = $temporaryIndex
    Push-Location $script:SnapBarRepo
    try {
        $fixture = Join-Path $run 'staged.md'
        [IO.File]::WriteAllBytes($fixture, [byte[]]@(239, 187, 191, 65))
        & git add -f -- $fixture
        if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare isolated index fixture.' }
        [IO.File]::WriteAllText($fixture, 'valid working tree', [Text.UTF8Encoding]::new($false))
        $result = Invoke-SnapBarCommand 'StagedBom' (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check.ps1'), '-Staged') $run
        Assert-Tooling (-not $result.success)
        & git add -f -- $fixture
        if ($LASTEXITCODE -ne 0) { throw 'Cannot update isolated index fixture.' }
        $result = Invoke-SnapBarCommand 'StagedValid' (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check.ps1'), '-Staged') $run
        Assert-SnapBarCommand $result
        $fixture = Join-Path $run 'staged.ps1'
        [IO.File]::WriteAllText($fixture, 'if (', [Text.UTF8Encoding]::new($false))
        & git add -f -- $fixture
        if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare isolated syntax fixture.' }
        [IO.File]::WriteAllText($fixture, '$value = 1', [Text.UTF8Encoding]::new($false))
        $result = Invoke-SnapBarCommand 'StagedSyntax' (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check.ps1'), '-Staged') $run
        Assert-Tooling (-not $result.success)
    }
    finally { $env:GIT_INDEX_FILE = $oldIndex; Pop-Location }
}
$releaseFixture = Join-Path $run 'release-fixture'
[IO.Directory]::CreateDirectory((Join-Path $releaseFixture 'Probe')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $releaseFixture 'zh-Hant')) | Out-Null
$releaseFiles = @(
    'SnapBar.App.exe', 'SnapBar.App.dll', 'SnapBar.App.deps.json', 'SnapBar.App.runtimeconfig.json',
    'coreclr.dll', 'hostfxr.dll', 'wpfgfx_cor3.dll', 'README.md', 'VALIDATION.md', 'CLIPBOARD-RECOVERY.md',
    'README.en.md', 'zh-Hant/SnapBar.App.resources.dll',
    'Probe/SnapBar.Probe.exe', 'Probe/SnapBar.Probe.dll', 'Probe/SnapBar.Probe.deps.json',
    'Probe/SnapBar.Probe.runtimeconfig.json', 'Probe/coreclr.dll', 'Probe/hostfxr.dll'
)
foreach ($file in $releaseFiles) {
    [IO.File]::WriteAllText((Join-Path $releaseFixture $file), "fixture: $file", [Text.UTF8Encoding]::new($false))
}
Check-Tooling 'release archive rejects unsafe version names before creating output' {
    $output = Join-Path $run 'invalid-release'
    foreach ($version in @('../escape', 'v1/2/3', 'v1.2', 'v01.2.3', 'v1.2.3 trailing', '-v1.2.3')) {
        Expect-ToolingFailure { New-SnapBarReleaseArchive $releaseFixture $version $output }
    }
    Assert-Tooling (-not (Test-Path -LiteralPath $output))
}
Check-Tooling 'release archive refuses incomplete self-contained App and Probe packages' {
    foreach ($file in @('SnapBar.App.exe', 'coreclr.dll', 'Probe/SnapBar.Probe.exe', 'Probe/coreclr.dll', 'README.md', 'README.en.md', 'zh-Hant/SnapBar.App.resources.dll')) {
        $path = Join-Path $releaseFixture $file
        $bytes = [IO.File]::ReadAllBytes($path)
        try {
            [IO.File]::WriteAllBytes($path, [byte[]]@())
            Expect-ToolingFailure { New-SnapBarReleaseArchive $releaseFixture 'v1.2.3' (Join-Path $run 'incomplete-release') }
        }
        finally { [IO.File]::WriteAllBytes($path, $bytes) }
    }
}
Check-Tooling 'release ZIP preserves root layout and Probe content with a matching SHA-256' {
    $result = New-SnapBarReleaseArchive $releaseFixture 'v1.2.3-rc.1' (Join-Path $run 'release-output')
    $zip = [IO.Compression.ZipFile]::OpenRead($result.archive)
    try {
        Assert-Tooling ($zip.Entries.Count -eq $releaseFiles.Count)
        foreach ($file in $releaseFiles) {
            $reader = [IO.StreamReader]::new($zip.GetEntry($file).Open())
            try { Assert-Tooling ($reader.ReadToEnd() -eq "fixture: $file") }
            finally { $reader.Dispose() }
        }
    }
    finally { $zip.Dispose() }
    $hash = (Get-FileHash -LiteralPath $result.archive -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Tooling ([IO.File]::ReadAllText($result.checksum) -eq "$hash  SnapBar-v1.2.3-rc.1-win-x64.zip`n")
    Expect-ToolingFailure { New-SnapBarReleaseArchive $releaseFixture 'v1.2.3-rc.1' $result.directory }
    Assert-Tooling ((Get-FileHash -LiteralPath $result.archive -Algorithm SHA256).Hash.ToLowerInvariant() -eq $hash)
}
$workflowRelative = '.github/workflows/guardrail-' + [guid]::NewGuid().ToString('N') + '.yml'
$workflowPath = Join-Path $script:SnapBarRepo $workflowRelative
$configRelative = '.github/actionlint.yaml'
$configPath = Join-Path $script:SnapBarRepo $configRelative
$originalConfig = [IO.File]::ReadAllBytes($configPath)
$savedWorkflowIndex = $env:GIT_INDEX_FILE
$realIndex = & git -C $script:SnapBarRepo rev-parse --git-path index
if ($LASTEXITCODE -ne 0) { throw 'Cannot find index for workflow fixtures.' }
if (-not [IO.Path]::IsPathRooted($realIndex)) { $realIndex = Join-Path $script:SnapBarRepo $realIndex }
$workflowIndex = Join-Path $run 'workflow-index'
Copy-Item -LiteralPath $realIndex -Destination $workflowIndex
$validWorkflow = @('name: Guardrail fixture', 'on: push', 'jobs:', '  check:', '    runs-on: windows-latest', '    steps:', '      - run: echo fixture') -join "`n"
$invalidContext = $validWorkflow.Replace('    runs-on:', ('    env:' + "`n" + '      BAD: ${{ runner.temp }}' + "`n" + '    runs-on:'))
function Stage-WorkflowFixture([string]$File) {
    & git -C $script:SnapBarRepo add -- $File
    if ($LASTEXITCODE -ne 0) { throw "Cannot stage workflow fixture: $File" }
}
function Write-WorkflowFixture([string]$Content) { [IO.File]::WriteAllText($workflowPath, $Content, [Text.UTF8Encoding]::new($false)) }
function Invoke-WorkflowFixtureCheck([string]$Name) {
    return Invoke-SnapBarCommand $Name (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check.ps1'), '-Staged') $run
}
try {
    $env:GIT_INDEX_FILE = $workflowIndex
    Check-Tooling 'staged workflow context errors fail even after an unstaged repair' {
        Write-WorkflowFixture $invalidContext; Stage-WorkflowFixture $workflowRelative
        Write-WorkflowFixture $validWorkflow
        $result = Invoke-WorkflowFixtureCheck 'StagedInvalidContext'
        Assert-Tooling (-not $result.success -and $result.exitCode -ne 0)
        Assert-Tooling ([IO.File]::ReadAllText($result.log).Contains('context "runner" is not allowed here'))
        Assert-Tooling (-not [IO.File]::ReadAllText($result.log).Contains('Repository checks passed.'))
    }
    Check-Tooling 'valid staged workflows pass independently of unstaged YAML errors' {
        Write-WorkflowFixture $validWorkflow; Stage-WorkflowFixture $workflowRelative
        Write-WorkflowFixture 'on: [push'
        Assert-SnapBarCommand (Invoke-WorkflowFixtureCheck 'StagedValidWorkflow')
    }
    Check-Tooling 'malformed staged YAML fails independently of a valid working file' {
        Write-WorkflowFixture 'on: [push'; Stage-WorkflowFixture $workflowRelative
        Write-WorkflowFixture $validWorkflow
        $result = Invoke-WorkflowFixtureCheck 'StagedMalformedWorkflow'
        Assert-Tooling (-not $result.success -and $result.exitCode -ne 0)
    }
    Check-Tooling 'staged actionlint configuration controls custom runner labels' {
        $customConfig = @('self-hosted-runner:', '  labels:', '    - snapbar-desktop', '    - index-only') -join "`n"
        Write-WorkflowFixture ($validWorkflow.Replace('windows-latest', '[self-hosted, Windows, X64, index-only]'))
        Stage-WorkflowFixture $workflowRelative
        [IO.File]::WriteAllText($configPath, $customConfig, [Text.UTF8Encoding]::new($false)); Stage-WorkflowFixture $configRelative
        [IO.File]::WriteAllBytes($configPath, $originalConfig)
        Assert-SnapBarCommand (Invoke-WorkflowFixtureCheck 'StagedCustomLabel')
        Stage-WorkflowFixture $configRelative
        [IO.File]::WriteAllText($configPath, $customConfig, [Text.UTF8Encoding]::new($false))
        $result = Invoke-WorkflowFixtureCheck 'StagedMissingLabel'
        Assert-Tooling (-not $result.success -and $result.exitCode -ne 0)
    }
    Check-Tooling 'deleting staged actionlint configuration revalidates all remaining workflows' {
        Write-WorkflowFixture ($validWorkflow.Replace('windows-latest', '[self-hosted, Windows, X64, snapbar-desktop]')); Stage-WorkflowFixture $workflowRelative
        & git -C $script:SnapBarRepo update-index --force-remove -- $configRelative
        if ($LASTEXITCODE -ne 0) { throw 'Cannot remove configuration from fixture index.' }
        [IO.File]::WriteAllBytes($configPath, $originalConfig)
        $result = Invoke-WorkflowFixtureCheck 'StagedDeletedConfig'
        Assert-Tooling (-not $result.success -and $result.exitCode -ne 0)
        Assert-Tooling ([IO.File]::ReadAllText($result.log).Contains('label "snapbar-desktop" is unknown'))
    }
}
finally {
    $env:GIT_INDEX_FILE = $savedWorkflowIndex
    [IO.File]::WriteAllBytes($configPath, $originalConfig)
    if (Test-Path -LiteralPath $workflowPath) { Remove-Item -LiteralPath $workflowPath }
}
Write-Host "$script:toolingPassed passed, 0 failed"
