function Invoke-SnapBarNotesGit([string]$Root, [string[]]$Arguments) {
    $output = & git -C $Root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Cannot read release source history: $($Arguments[0]). Fetch the previous release tag and complete history." }
    return ($output -join "`n")
}

function ConvertFrom-SnapBarReleaseNote([string]$Content, [string]$Path) {
    try { $note = ConvertFrom-Json -InputObject $Content -ErrorAction Stop }
    catch { throw "Invalid release note JSON: $Path" }
    foreach ($field in @('title', 'summary', 'usage')) {
        if ($note.$field -isnot [string] -or [string]::IsNullOrWhiteSpace($note.$field) -or
            $note.$field -match '(?i)\b(TODO|TBD)\b|\{[^}]+\}' -or $note.$field.Contains("`n")) {
            throw "Release note requires a completed single-line $field`: $Path"
        }
    }
    if ($note.highlights -isnot [array] -or $note.highlights.Count -lt 1 -or $note.highlights.Count -gt 3) {
        throw "Release note requires one to three highlights: $Path"
    }
    foreach ($highlight in $note.highlights) {
        if ($highlight -isnot [string] -or [string]::IsNullOrWhiteSpace($highlight) -or
            $highlight -match '(?i)\b(TODO|TBD)\b|\{[^}]+\}' -or $highlight.Contains("`n")) {
            throw "Release note contains an incomplete highlight: $Path"
        }
    }
    return $note
}

function Get-SnapBarReleaseContent {
    param(
        [string]$Root = (Split-Path $PSScriptRoot -Parent),
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
        [string]$PreviousTag
    )
    # Read immutable Git objects, never a working copy or commit subject.
    $null = Invoke-SnapBarNotesGit $Root @('rev-parse', '--verify', "$Commit^{commit}")
    if ($PreviousTag) {
        $previous = Invoke-SnapBarNotesGit $Root @('rev-parse', '--verify', "refs/tags/$PreviousTag^{commit}")
        $null = Invoke-SnapBarNotesGit $Root @('merge-base', '--is-ancestor', $previous, $Commit)
        $paths = Invoke-SnapBarNotesGit $Root @('diff', '--name-only', '--no-renames', $previous, $Commit, '--')
        $added = Invoke-SnapBarNotesGit $Root @('diff', '--name-only', '--no-renames', '--diff-filter=A', $previous, $Commit, '--', 'release-notes/*.json')
    }
    else {
        $paths = Invoke-SnapBarNotesGit $Root @('ls-tree', '-r', '--name-only', $Commit)
        $added = $paths
    }
    $files = @($paths -split "`n" | Where-Object { $_ })
    $notePaths = @($added -split "`n" | Where-Object { $_ -cmatch '^release-notes/[a-z0-9][a-z0-9-]*\.json$' } | Sort-Object)
    $sections = @(
        foreach ($path in $notePaths) {
            $content = Invoke-SnapBarNotesGit $Root @('show', "${Commit}:$path")
            $note = ConvertFrom-SnapBarReleaseNote $content $path
            "### $($note.title)`n`n$($note.summary)`n`n" +
                (($note.highlights | ForEach-Object { "- $_" }) -join "`n") + "`n`n**開始使用：**$($note.usage)"
        }
    )
    if ($sections.Count) { return ($sections -join "`n`n") }
    # Fail closed for anything that could change the delivered desktop experience.
    $desktop = @($files | Where-Object {
        $_ -notmatch '^(site/|docs/|tests/|scripts/|\.github/|\.githooks/|release-notes/)' -and
        $_ -notmatch '^(README\.md|AGENTS\.md|LICENSE|\.gitignore)$'
    })
    if ($desktop.Count) {
        throw 'Desktop/package changes have no new user-facing release note. Add release-notes/<unique-name>.json with title, summary, highlights and usage before publishing.'
    }
    $areas = @()
    if (@($files | Where-Object { $_ -like 'site/*' }).Count) { $areas += '網站' }
    if (@($files | Where-Object { $_ -match '^(docs/|README\.md|AGENTS\.md)' }).Count) { $areas += '文件' }
    if (@($files | Where-Object { $_ -match '^(scripts/|tests/|\.github/|\.githooks/)' }).Count) { $areas += '建置、測試或發布工具' }
    $scope = if ($areas.Count) { $areas -join '、' } else { '專案維護內容' }
    return "本次沒有桌面功能更新；變更範圍為$scope。已安裝的使用者不需為了新功能更新；新使用者仍可下載下方套件。"
}
