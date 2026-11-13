. (Join-Path $PSScriptRoot 'common.ps1')
$script:SnapBarDesktopNotes = 'refs/notes/snapbar-desktop'

function Get-SnapBarCommit([string]$Revision) {
    $commit = & git -C $script:SnapBarRepo rev-parse --verify "$Revision^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'Push target must resolve to a commit.' }
    return "$commit"
}

function Assert-SnapBarCleanCommit([string]$Commit) {
    if ((Get-SnapBarCommit 'HEAD') -ne $Commit) { throw 'Push only the checked-out HEAD commit; test other branches in their own checkout.' }
    $status = & git -C $script:SnapBarRepo status --porcelain --untracked-files=all --ignore-submodules=none
    if ($LASTEXITCODE -ne 0 -or $status) { throw 'Desktop push validation requires a clean working tree and index. Commit or stash changes first.' }
    # Dirty submodules and locally skipped tracked files must not masquerade as the tested commit.
    $flags = & git -C $script:SnapBarRepo ls-files -v
    if ($LASTEXITCODE -ne 0 -or @($flags | Where-Object { $_ -cmatch '^[a-zS] ' }).Count) {
        throw 'Desktop push validation does not support assume-unchanged or skip-worktree files.'
    }
}

function Get-SnapBarPushCommits([string[]]$Lines) {
    $commits = [Collections.Generic.HashSet[string]]::new()
    foreach ($line in $Lines) {
        if (-not $line.Trim()) { continue }
        $parts = $line -split '\s+'
        if ($parts.Count -ne 4 -or $parts[1] -notmatch '^[0-9a-f]{40,64}$' -or $parts[3] -notmatch '^[0-9a-f]{40,64}$') { throw 'Invalid pre-push input.' }
        if ($parts[2] -eq $script:SnapBarDesktopNotes -or $parts[1] -match '^0+$') { continue }
        if ($parts[2] -notmatch '^refs/(heads|tags)/') { throw 'Push validation supports branches, tags and the desktop notes ref only.' }
        $null = $commits.Add((Get-SnapBarCommit $parts[1]))
    }
    return @($commits)
}

function Assert-SnapBarDesktopReceipt($Receipt, [string]$Commit, [string]$Tree) {
    if ($Receipt.schema -ne 1 -or $Receipt.kind -ne 'snapbar-desktop' -or $Receipt.commit -ne $Commit -or $Receipt.tree -ne $Tree) {
        throw 'Desktop receipt does not match this exact commit and tree.'
    }
    $report = $Receipt.report
    if ($report.schema -ne 1 -or $report.success -isnot [bool] -or -not $report.success -or
        $report.sourceFingerprint -notmatch '^[0-9A-Fa-f]{64}$' -or -not $report.startedUtc -or -not $report.finishedUtc) {
        throw 'Desktop receipt is incomplete or failed.'
    }
    foreach ($name in (@('Build', 'Preflight') + $script:SnapBarReleaseSuites)) {
        $results = @($report.results | Where-Object { $_.name -eq $name })
        if ($results.Count -ne 1 -or $results[0].success -isnot [bool] -or -not $results[0].success -or
            ($results[0].exitCode -isnot [long] -and $results[0].exitCode -isnot [int]) -or $results[0].exitCode -ne 0 -or
            $results[0].failed -ne 0 -or ($name -ne 'Build' -and $results[0].passed -lt 1)) {
            throw "Desktop receipt is missing a successful $name result."
        }
        if ($name -eq 'ActionRunner') { Assert-SnapBarActionRunnerEvidence $results[0] }
    }
}

function New-SnapBarDesktopReceipt([string]$Commit, [string]$ReportPath) {
    Assert-SnapBarCleanCommit $Commit
    Assert-SnapBarDesktopReport $ReportPath (Get-SnapBarFingerprint)
    $report = Get-Content -LiteralPath $ReportPath -Raw -Encoding utf8 | ConvertFrom-Json
    $tree = & git -C $script:SnapBarRepo rev-parse "$Commit^{tree}"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve tested tree.' }
    $receipt = [pscustomobject]@{
        schema = 1; kind = 'snapbar-desktop'; commit = $Commit; tree = "$tree"
        report = [pscustomobject]@{
            schema = 1; success = $true; sourceFingerprint = $report.sourceFingerprint
            startedUtc = $report.startedUtc; finishedUtc = $report.finishedUtc
            results = @($report.results | Select-Object name, success, exitCode, passed, failed, skipped, runtimesRequired, seconds)
        }
    }
    Assert-SnapBarDesktopReceipt $receipt $Commit "$tree"
    return $receipt
}

function Send-SnapBarDesktopReceipt([string]$Remote, $Receipt) {
    $temporaryRef = 'refs/notes/snapbar-push-' + [guid]::NewGuid().ToString('N')
    $run = New-SnapBarRun 'push'
    $path = Join-Path $run 'receipt.json'
    Write-SnapBarJson $path $Receipt
    try {
        $null = & git -C $script:SnapBarRepo ls-remote --exit-code --refs -- $Remote $script:SnapBarDesktopNotes
        $remoteStatus = $LASTEXITCODE
        if ($remoteStatus -eq 0) {
            & git -C $script:SnapBarRepo fetch --no-tags -- $Remote "${script:SnapBarDesktopNotes}:$temporaryRef"
            if ($LASTEXITCODE -ne 0) { throw 'Cannot fetch desktop notes; push blocked.' }
        }
        elseif ($remoteStatus -ne 2) { throw 'Cannot inspect remote desktop notes; push blocked.' }
        & git -C $script:SnapBarRepo notes "--ref=$temporaryRef" add -f -F $path $Receipt.commit
        if ($LASTEXITCODE -ne 0) { throw 'Cannot record desktop receipt.' }
        # Publish evidence before the outer branch/tag push triggers Actions. Never overwrite concurrent notes.
        & git -C $script:SnapBarRepo push -- $Remote "${temporaryRef}:$script:SnapBarDesktopNotes"
        if ($LASTEXITCODE -ne 0) { throw 'Desktop receipt upload failed or another push updated the notes. Retry git push.' }
    }
    finally { & git -C $script:SnapBarRepo update-ref -d $temporaryRef }
}
