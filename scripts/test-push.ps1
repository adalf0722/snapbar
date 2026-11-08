$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'desktop-evidence.ps1')
$originalRepo = $script:SnapBarRepo
$run = New-SnapBarRun 'push-tests'
$fixture = Join-Path $run 'checkout'
$remote = Join-Path $run 'remote.git'
$script:pushPassed = 0
function Check-Push([string]$Name, [scriptblock]$Test) {
    & $Test
    $script:pushPassed++
    Write-Host "PASS $Name"
}
function Assert-Push([bool]$Condition) { if (-not $Condition) { throw 'Push assertion failed.' } }
function Reject-Push([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected push rejection.' }
}
function Invoke-FixtureGit([string[]]$Arguments) {
    $output = & git -C $fixture @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $($Arguments -join ' '): $output" }
    return $output
}
function Write-PushFixture([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}
[IO.Directory]::CreateDirectory((Join-Path $fixture 'scripts')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture '.githooks')) | Out-Null
try {
    $null = & git init --bare $remote 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create bare push fixture.' }
    $null = Invoke-FixtureGit @('init', '-b', 'main')
    $null = Invoke-FixtureGit @('config', 'user.name', 'Push Fixture')
    $null = Invoke-FixtureGit @('config', 'user.email', 'fixture@example.invalid')
    $null = Invoke-FixtureGit @('config', 'core.autocrlf', 'false')
    $null = Invoke-FixtureGit @('config', 'core.hooksPath', '.githooks')
    foreach ($file in @('common.ps1', 'desktop-evidence.ps1', 'pre-push.ps1', 'import-desktop-evidence.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $fixture "scripts/$file")
    }
    Write-PushFixture (Join-Path $fixture '.githooks/pre-push') ([IO.File]::ReadAllText((Join-Path $originalRepo '.githooks/pre-push')))
    Write-PushFixture (Join-Path $fixture '.gitignore') "artifacts/`n"
    # Controlled stand-in for expensive interactive tests, only inside this disposable repository.
    $desktopStub = @'
param([string]$Suite)
. (Join-Path $PSScriptRoot 'common.ps1')
$run = New-SnapBarRun 'desktop'
$report = @{
    schema = 1; sourceFingerprint = Get-SnapBarFingerprint; success = $true
    startedUtc = [DateTime]::UtcNow.ToString('o'); finishedUtc = [DateTime]::UtcNow.ToString('o')
    results = (@('Build', 'Preflight') + $script:SnapBarReleaseSuites) | ForEach-Object {
        @{ name = $_; success = $true; exitCode = 0; passed = $(if ($_ -eq 'Build') { 0 } else { 1 }); failed = 0; seconds = 0; log = 'private-local-path' }
    }
}
if (Test-Path (Join-Path $script:SnapBarRepo 'artifacts/fail')) { exit 7 }
Write-SnapBarJson (Join-Path (Split-Path $run -Parent) 'latest.json') $report
'@
    Write-PushFixture (Join-Path $fixture 'scripts/test-desktop.ps1') $desktopStub
    $null = Invoke-FixtureGit @('add', '.')
    $null = Invoke-FixtureGit @('-c', 'core.hooksPath=', 'commit', '-m', 'fixture: initialize push tests')
    $script:SnapBarRepo = $fixture
    $commit = Get-SnapBarCommit 'HEAD'
    $zero = '0' * 40
    Check-Push 'deletions and evidence-only pushes need no desktop run' {
        Assert-Push (@(Get-SnapBarPushCommits @("(delete) $zero refs/heads/old $commit")).Count -eq 0)
        Assert-Push (@(Get-SnapBarPushCommits @("refs/notes/local $commit refs/notes/snapbar-desktop $zero")).Count -eq 0)
    }
    Check-Push 'push input rejects malformed updates and deduplicates branch/tag commits' {
        Reject-Push { Get-SnapBarPushCommits @('broken') }
        $null = Invoke-FixtureGit @('tag', '-a', 'v1.2.3', '-m', 'fixture tag')
        $tag = (Invoke-FixtureGit @('rev-parse', 'v1.2.3')) -join ''
        Assert-Push (@(Get-SnapBarPushCommits @("refs/heads/main $commit refs/heads/main $zero", "refs/tags/v1.2.3 $tag refs/tags/v1.2.3 $zero")).Count -eq 1)
    }
    Check-Push 'clean gate rejects unstaged staged untracked and hidden tracked changes' {
        Assert-SnapBarCleanCommit $commit
        $path = Join-Path $fixture '.gitignore'
        $bytes = [IO.File]::ReadAllBytes($path)
        try {
            Write-PushFixture $path "artifacts/`n# changed`n"
            Reject-Push { Assert-SnapBarCleanCommit $commit }
            $null = Invoke-FixtureGit @('add', '.gitignore')
            Reject-Push { Assert-SnapBarCleanCommit $commit }
            $null = Invoke-FixtureGit @('reset', 'HEAD', '--', '.gitignore')
            [IO.File]::WriteAllBytes($path, $bytes)
            Write-PushFixture (Join-Path $fixture 'new.txt') 'untracked'
            Reject-Push { Assert-SnapBarCleanCommit $commit }
            Remove-Item -LiteralPath (Join-Path $fixture 'new.txt')
            $null = Invoke-FixtureGit @('update-index', '--assume-unchanged', '.gitignore')
            Reject-Push { Assert-SnapBarCleanCommit $commit }
        }
        finally {
            $null = Invoke-FixtureGit @('update-index', '--no-assume-unchanged', '.gitignore')
            [IO.File]::WriteAllBytes($path, $bytes)
        }
    }
    Check-Push 'real pre-push hook uploads sanitized commit evidence before branch push' {
        $null = Invoke-FixtureGit @('push', $remote, 'main')
        $remoteCommit = & git --git-dir=$remote rev-parse refs/heads/main
        Assert-Push ($LASTEXITCODE -eq 0 -and $remoteCommit -eq $commit)
        $json = & git --git-dir=$remote notes --ref=refs/notes/snapbar-desktop show $commit
        Assert-Push ($LASTEXITCODE -eq 0)
        $receipt = ($json -join "`n") | ConvertFrom-Json
        $tree = (Invoke-FixtureGit @('rev-parse', 'HEAD^{tree}')) -join ''
        Assert-SnapBarDesktopReceipt $receipt $commit $tree
        Assert-Push (-not ($json -join "`n").Contains('private-local-path'))
        Assert-Push (@(Invoke-FixtureGit @('for-each-ref', '--format=%(refname)', 'refs/notes/snapbar-push-*')).Count -eq 0)
    }
    Check-Push 'receipt rejects wrong commit tree failed preflight missing suites and duplicate suites' {
        $json = & git --git-dir=$remote notes --ref=refs/notes/snapbar-desktop show $commit
        $receipt = ($json -join "`n") | ConvertFrom-Json
        Reject-Push { Assert-SnapBarDesktopReceipt $receipt $zero $receipt.tree }
        Reject-Push { Assert-SnapBarDesktopReceipt $receipt $commit $zero }
        $receipt.report.results[1].success = $false
        Reject-Push { Assert-SnapBarDesktopReceipt $receipt $commit $receipt.tree }
        $receipt.report.results[1].success = $true
        $saved = $receipt.report.results
        $receipt.report.results = @($saved | Where-Object name -ne 'CompatibilitySlow')
        Reject-Push { Assert-SnapBarDesktopReceipt $receipt $commit $receipt.tree }
        $receipt.report.results = @($saved) + @($saved[1])
        Reject-Push { Assert-SnapBarDesktopReceipt $receipt $commit $receipt.tree }
    }
    Check-Push 'cloud import accepts exact clean commit and rejects missing notes' {
        $null = Invoke-FixtureGit @('fetch', '--no-tags', $remote, 'refs/notes/snapbar-desktop:refs/notes/snapbar-desktop')
        $path = & (Get-Command pwsh).Source -NoProfile -File (Join-Path $fixture 'scripts/import-desktop-evidence.ps1')
        Assert-Push ($LASTEXITCODE -eq 0)
        Assert-SnapBarDesktopReport ($path | Select-Object -Last 1) (Get-SnapBarFingerprint)
        $null = Invoke-FixtureGit @('notes', '--ref=refs/notes/snapbar-desktop', 'remove', $commit)
        $null = & (Get-Command pwsh).Source -NoProfile -File (Join-Path $fixture 'scripts/import-desktop-evidence.ps1') 2>&1
        Assert-Push ($LASTEXITCODE -ne 0)
    }
    Check-Push 'fresh CRLF checkout imports LF-tested commit evidence without weakening fingerprint checks' {
        $cloud = Join-Path $run 'cloud-checkout'
        $null = & git -c core.autocrlf=true clone --branch main $remote $cloud 2>&1
        Assert-Push ($LASTEXITCODE -eq 0)
        $null = & git -C $cloud fetch --no-tags origin refs/notes/snapbar-desktop:refs/notes/snapbar-desktop 2>&1
        Assert-Push ($LASTEXITCODE -eq 0)
        $path = & (Get-Command pwsh).Source -NoProfile -File (Join-Path $cloud 'scripts/import-desktop-evidence.ps1')
        Assert-Push ($LASTEXITCODE -eq 0)
        try {
            $script:SnapBarRepo = $cloud
            $cloudFingerprint = Get-SnapBarFingerprint
            $json = & git -C $cloud notes --ref=refs/notes/snapbar-desktop show $commit
            $receipt = ($json -join "`n") | ConvertFrom-Json
            Assert-Push ($cloudFingerprint -ne $receipt.report.sourceFingerprint)
            Assert-SnapBarDesktopReport ($path | Select-Object -Last 1) $cloudFingerprint
            [IO.File]::AppendAllText((Join-Path $cloud 'scripts/pre-push.ps1'), "`n# changed", [Text.UTF8Encoding]::new($false))
            Reject-Push { Assert-SnapBarDesktopReport ($path | Select-Object -Last 1) (Get-SnapBarFingerprint) }
        }
        finally { $script:SnapBarRepo = $fixture }
    }
    Check-Push 'failed desktop tests block actual push and leave remote branch unchanged' {
        Write-PushFixture (Join-Path $fixture 'change.txt') 'next commit'
        $null = Invoke-FixtureGit @('add', 'change.txt')
        $null = Invoke-FixtureGit @('-c', 'core.hooksPath=', 'commit', '-m', 'fixture: next commit')
        Write-PushFixture (Join-Path $fixture 'artifacts/fail') 'fail'
        $null = & git -C $fixture push $remote main 2>&1
        Assert-Push ($LASTEXITCODE -ne 0)
        Assert-Push ((& git --git-dir=$remote rev-parse refs/heads/main) -eq $commit)
        Remove-Item -LiteralPath (Join-Path $fixture 'artifacts/fail')
    }
    Check-Push 'notes upload rejection blocks actual branch push' {
        Write-PushFixture (Join-Path $remote 'hooks/pre-receive') "#!/bin/sh`nwhile read old new ref; do`n  if [ `"`$ref`" = refs/notes/snapbar-desktop ]; then exit 1; fi`ndone`n"
        $null = & git -C $fixture push $remote main 2>&1
        Assert-Push ($LASTEXITCODE -ne 0)
        Assert-Push ((& git --git-dir=$remote rev-parse refs/heads/main) -eq $commit)
        Remove-Item -LiteralPath (Join-Path $remote 'hooks/pre-receive')
    }
    Check-Push 'subsequent push preserves earlier commit receipts and accepts annotated tags' {
        $null = Invoke-FixtureGit @('push', $remote, 'main')
        $old = & git --git-dir=$remote notes --ref=refs/notes/snapbar-desktop show $commit
        Assert-Push ($LASTEXITCODE -eq 0 -and $old)
        $null = Invoke-FixtureGit @('tag', '-a', 'v1.2.4', '-m', 'next tag')
        $null = Invoke-FixtureGit @('push', $remote, 'v1.2.4')
        Assert-Push ((& git --git-dir=$remote rev-parse 'v1.2.4^{commit}') -eq (Get-SnapBarCommit 'HEAD'))
        Reject-Push { Assert-SnapBarCleanCommit $commit }
    }
}
finally { $script:SnapBarRepo = $originalRepo }
Write-Host "$script:pushPassed passed, 0 failed"
