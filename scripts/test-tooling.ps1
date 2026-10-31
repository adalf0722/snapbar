$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
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
$valid = @{ schema = 1; success = $true; sourceFingerprint = 'current'; results = @('Lifecycle', 'Appearance', 'CompatibilitySlow', 'AiRuntime', 'AiPanel') | ForEach-Object { @{ name = $_; success = $true; exitCode = 0 } } }
Check-Tooling 'publish accepts complete current desktop evidence' {
    Write-SnapBarJson $evidence $valid
    Assert-SnapBarDesktopReport $evidence 'current'
}
Check-Tooling 'publish rejects missing stale failed or incomplete evidence' {
    Expect-ToolingFailure { Assert-SnapBarDesktopReport (Join-Path $run 'missing.json') 'current' }
    Expect-ToolingFailure { Assert-SnapBarDesktopReport $evidence 'changed' }
    $valid.success = 'false'; Write-SnapBarJson $evidence $valid
    Expect-ToolingFailure { Assert-SnapBarDesktopReport $evidence 'current' }
    $valid.success = $false; Write-SnapBarJson $evidence $valid
    Expect-ToolingFailure { Assert-SnapBarDesktopReport $evidence 'current' }
    $valid.success = $true; $valid.results = @($valid.results | Where-Object { $_.name -ne 'CompatibilitySlow' }); Write-SnapBarJson $evidence $valid
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
Write-Host "$script:toolingPassed passed, 0 failed"
