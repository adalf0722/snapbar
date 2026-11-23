. (Join-Path $PSScriptRoot 'version-common.ps1')

function Invoke-SnapBarNotesGit([string]$Root, [string[]]$Arguments) {
    $output = & git -C $Root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Cannot read release source history: $($Arguments[0]). Fetch the previous release tag and complete history." }
    return ($output -join "`n")
}

function Assert-SnapBarReleaseText($Note, [string]$Path) {
    foreach ($field in @('title', 'summary', 'usage')) {
        if ($Note.$field -isnot [string] -or [string]::IsNullOrWhiteSpace($Note.$field) -or
            $Note.$field -match '(?i)\b(TODO|TBD)\b|\{[^}]+\}|[\r\n<]') {
            throw "Release note requires a completed single-line $field`: $Path"
        }
    }
    if ($Note.highlights -isnot [array] -or $Note.highlights.Count -lt 1 -or $Note.highlights.Count -gt 3) {
        throw "Release note requires one to three highlights: $Path"
    }
    foreach ($highlight in $Note.highlights) {
        if ($highlight -isnot [string] -or [string]::IsNullOrWhiteSpace($highlight) -or
            $highlight -match '(?i)\b(TODO|TBD)\b|\{[^}]+\}|[\r\n<]') {
            throw "Release note contains an incomplete highlight: $Path"
        }
    }
}

function Assert-SnapBarReleaseMetadata($Note, [string]$Path) {
    if ($null -ne $Note.priority -and (($Note.priority -isnot [long] -and $Note.priority -isnot [int]) -or $Note.priority -lt 0 -or $Note.priority -gt 100)) {
        throw "Release note priority must be an integer from 0 to 100: $Path"
    }
    if ($null -ne $Note.requirements) {
        if ($Note.requirements -isnot [array]) { throw "Requirements must be an array: $Path" }
        foreach ($requirement in $Note.requirements) {
            if ($requirement -cnotin @('ai', 'ocr', 'python', 'node', 'dictionary')) { throw "Unknown release prerequisite: $Path" }
        }
    }
    if ($null -ne $Note.notice) {
        foreach ($language in @('zh', 'en')) {
            $value = $Note.notice.$language
            if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value) -or $value -match '[\r\n<]|(?i)\b(TODO|TBD)\b|\{[^}]+\}') {
                throw "Important notices require both zh and en text: $Path"
            }
        }
    }
}

function ConvertFrom-SnapBarReleaseNote([string]$Content, [string]$Path) {
    try { $note = ConvertFrom-Json -InputObject $Content -ErrorAction Stop }
    catch { throw "Invalid release note JSON: $Path" }
    Assert-SnapBarReleaseText $note $Path
    if ($null -ne $note.en) {
        Assert-SnapBarReleaseText $note.en "$Path (en)"
        if ($note.en.highlights.Count -ne $note.highlights.Count) { throw "Bilingual highlights must correspond: $Path" }
    }
    if ($note.summary.Length -gt 140 -or ($note.en -and $note.en.summary.Length -gt 240)) { throw "Release summary is too long: $Path" }
    Assert-SnapBarReleaseMetadata $note $Path
    return $note
}

function ConvertFrom-SnapBarReleaseCatalog([string]$Content) {
    try { $catalog = ConvertFrom-Json -InputObject $Content -AsHashtable -ErrorAction Stop }
    catch { throw 'Invalid release-note translation catalog JSON.' }
    if ($catalog -isnot [Collections.IDictionary] -or -not $catalog.Count) { throw 'Release-note translation catalog must be a nonempty object.' }
    foreach ($name in $catalog.Keys) {
        if ($name -cnotmatch '^[a-z0-9][a-z0-9-]*\.json$') { throw 'Invalid release-note translation key.' }
        Assert-SnapBarReleaseText $catalog[$name] "$name (English catalog)"
        Assert-SnapBarReleaseMetadata $catalog[$name] "$name (English catalog)"
        if ($catalog[$name].summary.Length -gt 240) { throw "English release summary is too long: $name" }
    }
    return ,$catalog
}

function Get-SnapBarReleaseContent {
    param(
        [string]$Root = (Split-Path $PSScriptRoot -Parent),
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
        [string]$PreviousTag,
        [switch]$AsObject
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
    # Classification is independent of whether an authored note was added.
    $desktop = @($files | Where-Object {
        $_ -notmatch '^(site/|docs/|tests/|scripts/|\.github/|\.githooks/|release-notes/)' -and
        $_ -notmatch '^(README\.md|AGENTS\.md|LICENSE|\.gitignore)$'
    })
    $notePaths = @($added -split "`n" | Where-Object { $_ -cmatch '^release-notes/[a-z0-9][a-z0-9-]*\.json$' } | Sort-Object)
    $catalog = $null
    $notes = @(
        foreach ($path in $notePaths) {
            $content = Invoke-SnapBarNotesGit $Root @('show', "${Commit}:$path")
            $note = ConvertFrom-SnapBarReleaseNote $content $path
            $english = $note.en
            $metadata = $note
            if ($null -eq $english) {
                if ($null -eq $catalog) {
                    $catalogText = Invoke-SnapBarNotesGit $Root @('show', "${Commit}:packaging/RELEASE-NOTE-TRANSLATIONS.json")
                    $catalog = ConvertFrom-SnapBarReleaseCatalog $catalogText
                }
                $english = $catalog[[IO.Path]::GetFileName($path)]
                if ($null -eq $english) { throw "Release note is missing reviewed English content: $path" }
                Assert-SnapBarReleaseText $english "$path (English catalog)"
                Assert-SnapBarReleaseMetadata $english "$path (English catalog)"
                if ($english.highlights.Count -ne $note.highlights.Count) { throw "Bilingual highlights must correspond: $path" }
                $metadata = $english
            }
            if ($note.summary.Length -gt 140 -or $english.summary.Length -gt 240) { throw "Release summary is too long: $path" }
            [pscustomobject]@{ Path = $path; Zh = $note; En = $english; Priority = [int]$metadata.priority; Requirements = @($metadata.requirements); Notice = $metadata.notice }
        }
    )
    if ($notes.Count) {
        $result = Format-SnapBarReleaseContent $notes
        $result | Add-Member -NotePropertyName DesktopChanged -NotePropertyValue ([bool]$desktop.Count)
        if ($AsObject) { return $result }
        return "$($result.Summary)`n`n<details>`n<summary>完整說明 / Full release notes</summary>`n`n$($result.Details)`n`n</details>"
    }
    # Fail closed for anything that could change the delivered desktop experience.
    if ($desktop.Count) {
        throw 'Desktop/package changes have no new user-facing release note. Add release-notes/<unique-name>.json with title, summary, highlights and usage before publishing.'
    }
    $areas = @(); $englishAreas = @()
    if (@($files | Where-Object { $_ -like 'site/*' }).Count) { $areas += '網站'; $englishAreas += 'website' }
    if (@($files | Where-Object { $_ -match '^(docs/|README\.md|AGENTS\.md)' }).Count) { $areas += '文件'; $englishAreas += 'documentation' }
    if (@($files | Where-Object { $_ -match '^(scripts/|tests/|\.github/|\.githooks/)' }).Count) { $areas += '建置、測試或發布工具'; $englishAreas += 'build, test or release tooling' }
    $scope = if ($areas.Count) { $areas -join '、' } else { '專案維護內容' }
    $englishScope = if ($englishAreas.Count) { $englishAreas -join '; ' } else { 'project maintenance' }
    $summary = "## 繁體中文`n`n本次沒有桌面功能更新，已安裝的使用者不需為了新功能更新。`n`n---`n`n## English`n`nNo desktop feature changes. Existing users do not need to update for new features."
    $result = [pscustomobject]@{
        Summary = $summary; Details = "變更範圍：$scope。`n`nChange scope: $englishScope."
        DesktopChanged = $false
        Zh = [ordered]@{ summaries = @('本次沒有桌面功能更新，已安裝的使用者不需為了新功能更新。'); notices = @() }
        En = [ordered]@{ summaries = @('No desktop feature changes. Existing users do not need to update for new features.'); notices = @() }
    }
    if ($AsObject) { return $result }
    return "$($result.Summary)`n`n<details>`n<summary>完整說明 / Full release notes</summary>`n`n$($result.Details)`n`n</details>"
}

function Format-SnapBarReleaseContent([object[]]$Notes) {
    $featured = @($Notes | Sort-Object @{ Expression = 'Priority'; Descending = $true }, Path | Select-Object -First 3)
    $requirements = @($Notes.Requirements | Where-Object { $_ } | Sort-Object -Unique)
    $requirementText = @{
        ai = @('AI 功能需先設定服務、模型與必要的 API 金鑰。', 'AI features require a configured service, model and any required API key.')
        ocr = @('OCR 需安裝 Microsoft Visual C++ x64 執行階段。', 'OCR requires the Microsoft Visual C++ x64 runtime.')
        python = @('Python 動作需自行安裝 Python 3 與所需模組。', 'Python actions require Python 3 and any modules used by the action.')
        node = @('JavaScript 動作需自行安裝 Node.js。', 'JavaScript actions require Node.js.')
        dictionary = @('離線字典需自行匯入 StarDict 詞典。', 'Offline lookup requires your own imported StarDict dictionaries.')
    }
    $summaries = @(); $details = @(); $localized = @{}
    foreach ($language in @('Zh', 'En')) {
        $isChinese = $language -eq 'Zh'
        $heading = if ($isChinese) { '## 繁體中文' } else { '## English' }
        $bullets = @($featured | ForEach-Object { '- ' + $_.$language.summary }) -join "`n"
        $more = if ($Notes.Count -gt 3) {
            if ($isChinese) { "`n`n另有 $($Notes.Count - 3) 項更新，請展開下方完整說明。" }
            elseif ($Notes.Count -eq 4) { "`n`n1 more update is included in the full details below." }
            else { "`n`n$($Notes.Count - 3) more updates are included in the full details below." }
        } else { '' }
        $start = if ($isChinese) { '**開始使用：** 結束舊版，完整解壓縮後執行 `SnapBar.App.exe`；個人設定會沿用。' }
            else { '**Get started:** Quit the old version, extract the full ZIP, and run `SnapBar.App.exe`. Your settings are kept.' }
        $needed = @($requirements | ForEach-Object { $requirementText[$_][$(if ($isChinese) { 0 } else { 1 })] }) -join ' '
        $notices = @($Notes | Where-Object { $_.Notice } | ForEach-Object {
            if ($isChinese) { '> **更新前請留意：** ' + $_.Notice.zh }
            else { '> **Before updating:** ' + $_.Notice.en }
        }) -join "`n`n"
        $localized[$language] = [ordered]@{
            summaries = @($featured | ForEach-Object { $_.$language.summary })
            notices = @($requirements | ForEach-Object { $requirementText[$_][$(if ($isChinese) { 0 } else { 1 })] }) +
                @($Notes | Where-Object { $_.Notice } | ForEach-Object { if ($isChinese) { $_.Notice.zh } else { $_.Notice.en } })
        }
        $summaries += "$heading`n`n$bullets$more`n`n$start" + $(if ($needed) { "`n`n$needed" }) + $(if ($notices) { "`n`n$notices" })
        $sections = @($Notes | ForEach-Object {
            $text = $_.$language
            $label = if ($isChinese) { '**使用方式：** ' } else { '**Usage:** ' }
            "### $($text.title)`n`n$($text.summary)`n`n" + (($text.highlights | ForEach-Object { '- ' + $_ }) -join "`n") + "`n`n$label$($text.usage)"
        })
        $details += "$heading`n`n" + ($sections -join "`n`n")
    }
    return [pscustomobject]@{ Summary = $summaries -join "`n`n---`n`n"; Details = $details -join "`n`n---`n`n"; Zh = $localized.Zh; En = $localized.En }
}

function New-SnapBarUpdateComment($Content, [string]$Version, [string]$Commit) {
    $identity = Get-SnapBarVersion $Version
    if (-not $identity.IsPublic -or
        $Commit -cnotmatch '^[0-9a-f]{40}$' -or $Content.DesktopChanged -isnot [bool]) { throw 'Invalid update metadata identity.' }
    foreach ($language in @('Zh', 'En')) {
        $text = $Content.$language
        $limit = if ($language -eq 'Zh') { 140 } else { 240 }
        if ($text.summaries -isnot [array] -or $text.summaries.Count -lt 1 -or $text.summaries.Count -gt 3 -or
            $text.notices -isnot [array]) { throw 'Invalid update metadata text arrays.' }
        $noticeLength = 0
        foreach ($field in @('summaries', 'notices')) {
            foreach ($value in $text.$field) {
                $maximum = if ($field -eq 'summaries') { $limit } else { 2000 }
                if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value) -or $value.Length -gt $maximum -or
                    $value -match '\p{Cc}') { throw 'Update metadata contains invalid or overlong text.' }
                # Strict UTF-8 rejects unpaired UTF-16 surrogates rather than replacing them.
                $null = [Text.UTF8Encoding]::new($false, $true).GetBytes($value)
                if ($field -eq 'notices') { $noticeLength += $value.Length }
            }
        }
        if ($noticeLength -gt 2000) { throw 'Update metadata notices exceed 2000 characters; shorten the notices without dropping important information.' }
    }
    $metadata = [ordered]@{ schemaVersion = 1; version = $identity.Version; sourceCommit = $Commit; desktopChanged = $Content.DesktopChanged; zh = $Content.Zh; en = $Content.En }
    $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes(($metadata | ConvertTo-Json -Depth 8 -Compress))
    if ($bytes.Length -gt 32768) { throw 'Update metadata exceeds 32 KiB.' }
    return '<!-- snapbar-update-v1: ' + [Convert]::ToBase64String($bytes) + ' -->'
}

function New-SnapBarReleaseBody {
    param([string]$Root, [string]$Commit, [string]$PreviousTag, [string]$Version, [string]$Repository, [string]$RunId)
    $content = Get-SnapBarReleaseContent -Root $Root -Commit $Commit -PreviousTag $PreviousTag -AsObject
    $comparison = if ($PreviousTag) { "[$PreviousTag → $Version](https://github.com/$Repository/compare/$PreviousTag...$Version)" }
        else { "[原始碼 / Source](https://github.com/$Repository/tree/$Commit)" }
    $template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../packaging/AUTO-RELEASE-NOTES.md'), [Text.Encoding]::UTF8)
    # Substitute only template tokens, never rescan authored text as a template.
    $values = @{ version = $Version; commit = $Commit; run = $RunId; repository = $Repository; comparison = $comparison; changes = $content.Summary; details = $content.Details }
    $body = "<!-- snapbar-previous: $PreviousTag -->`n" + [regex]::Replace($template, '\{(version|commit|run|repository|comparison|changes|details)\}', [Text.RegularExpressions.MatchEvaluator]{ param($match) $values[$match.Groups[1].Value] })
    if ($body.Contains('snapbar-update-v1')) { throw 'Release body contains a reserved or duplicate update metadata marker.' }
    return (New-SnapBarUpdateComment $content $Version $Commit) + "`n" + $body
}

function Assert-SnapBarDraftUpdateMetadata($Release, [string]$Root, [string]$Commit) {
    if (-not $Release.draft) { return }
    # Only immutable source predating the generator is grandfathered. Missing or
    # damaged metadata on a newly generated draft must never bypass validation.
    $path = Invoke-SnapBarNotesGit $Root @('ls-tree', '--name-only', $Commit, '--', 'scripts/release-notes.ps1')
    if (-not $path) { return }
    $source = Invoke-SnapBarNotesGit $Root @('show', "${Commit}:scripts/release-notes.ps1")
    if (-not $source.Contains('function New-SnapBarUpdateComment(')) { return }
    $body = [string]$Release.body
    if ([regex]::Matches($body, 'snapbar-update-v1').Count -ne 1) { throw 'Draft requires one valid update metadata contract.' }
    $previous = [regex]::Matches($body, '<!-- snapbar-previous: (.*?) -->')
    if ($previous.Count -ne 1) { throw 'Draft requires an unambiguous release-note baseline.' }
    $content = Get-SnapBarReleaseContent -Root $Root -Commit $Commit -PreviousTag $previous[0].Groups[1].Value -AsObject
    $expected = New-SnapBarUpdateComment $content $Release.tag_name $Commit
    if (-not $body.Contains($expected)) { throw 'Draft update metadata does not match its validated source, version and release content.' }
}
