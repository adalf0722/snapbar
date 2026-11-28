$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'release-notes.ps1')
$run = New-SnapBarRun 'release-note-tests'
$source = Join-Path $run 'source'
$null = [IO.Directory]::CreateDirectory($source)
$passed = 0
function Check-Notes([string]$Name, [scriptblock]$Action) { & $Action; $script:passed++; Write-Host "PASS $Name" }
function Assert-Notes([bool]$Condition) { if (-not $Condition) { throw 'Release-note assertion failed.' } }
function Reject-Notes([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected release-note rejection.' }
}
function Git-Notes([string[]]$Arguments) { return Invoke-SnapBarNotesGit $source $Arguments }
function Write-Notes([string]$Path, [string]$Content) {
    $full = Join-Path $source $Path
    $null = [IO.Directory]::CreateDirectory((Split-Path $full -Parent))
    [IO.File]::WriteAllText($full, $Content, [Text.UTF8Encoding]::new($false))
}
function Save-Notes {
    $null = Git-Notes @('add', '.')
    $null = Git-Notes @('-c', 'core.hooksPath=', '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'release note fixture')
    return Git-Notes @('rev-parse', 'HEAD')
}
$null = Git-Notes @('init', '-q')
Write-Notes 'README.md' 'fixture'
$base = Save-Notes
$null = Git-Notes @('tag', 'baseline', $base)
$examples = @('2026-11-15-bilingual-ai-translation.json', '2026-11-15-chinese-script-conversion.json', '2026-11-15-improve-writing-prompt.json')
foreach ($name in $examples) { Write-Notes "release-notes/$name" ([IO.File]::ReadAllText((Join-Path $PSScriptRoot "../release-notes/$name"))) }
Write-Notes 'packaging/RELEASE-NOTE-TRANSLATIONS.json' ([IO.File]::ReadAllText((Join-Path $PSScriptRoot '../packaging/RELEASE-NOTE-TRANSLATIONS.json')))
$three = Save-Notes
$content = Get-SnapBarReleaseContent -Root $source -Commit $three -PreviousTag baseline -AsObject
Check-Notes 'three AI changes have matching concise bilingual bullets and one setup line per language' {
    Assert-Notes ([regex]::Matches($content.Summary, '(?m)^- ').Count -eq 6)
    Assert-Notes ([regex]::Matches($content.Summary, 'AI 功能需先設定').Count -eq 1)
    Assert-Notes ([regex]::Matches($content.Summary, 'AI features require').Count -eq 1)
    Assert-Notes (-not $content.Summary.Contains('128') -and $content.Details.Contains('128'))
    Assert-Notes ($content.Details.Contains('English ↔ Chinese') -and $content.Details.Contains('Traditional ↔ Simplified Chinese'))
}
$preview = New-SnapBarReleaseBody -Root $source -Commit $three -PreviousTag baseline -Version v0.1.0-rc.14 -Repository adalf0722/snapbar -RunId 123
$previewPath = Join-Path $run 'release-preview.md'
[IO.File]::WriteAllText($previewPath, $preview, [Text.UTF8Encoding]::new($false))
function Read-NotesMetadata([string]$Body) {
    $matches = [regex]::Matches($Body, '<!-- snapbar-update-v1: ([A-Za-z0-9+/=]+) -->')
    Assert-Notes ($matches.Count -eq 1)
    $bytes = [Convert]::FromBase64String($matches[0].Groups[1].Value)
    Assert-Notes ($bytes.Length -le 32768)
    return [Text.UTF8Encoding]::new($false, $true).GetString($bytes) | ConvertFrom-Json
}
Check-Notes 'machine summaries roundtrip with exact identity and delivered packaging changes' {
    $metadata = Read-NotesMetadata $preview
    Assert-Notes ($metadata.schemaVersion -eq 1 -and $metadata.version -ceq '0.1.0-rc.14' -and $metadata.sourceCommit -ceq $three)
    Assert-Notes ($metadata.desktopChanged -and $metadata.zh.summaries.Count -eq 3 -and $metadata.en.summaries.Count -eq 3)
    Assert-Notes ($metadata.zh.summaries[0] -ceq $content.Zh.summaries[0])
    Assert-Notes ($metadata.zh.notices.Count -eq 1 -and $metadata.en.notices[0].Contains('API key'))
}
Check-Notes 'metadata accepts all shared public preview versions and rejects private or malformed versions' {
    foreach ($version in @('v1.0.0-alpha', 'v1.0.0-beta.2', 'v1.0.0-rc.10', 'v1.0.0')) {
        $metadata = Read-NotesMetadata (New-SnapBarUpdateComment $content $version $three)
        Assert-Notes ($metadata.version -ceq $version.Substring(1))
    }
    foreach ($version in @('v1.0.0-dev', 'v1.0.0-validation', 'v1.0.0-ci.1.g123456789abc', 'v1.0.0-beta.02', 'v01.0.0-alpha')) {
        Reject-Notes { New-SnapBarUpdateComment $content $version $three }
    }
}
Check-Notes 'contract rejects control characters, malformed Unicode, overlong summaries and total notices without truncation' {
    foreach ($kind in @('control', 'surrogate', 'summary', 'notice', 'total')) {
        $invalid = $content | ConvertTo-Json -Depth 10 | ConvertFrom-Json
        switch ($kind) {
            'control' { $invalid.En.summaries[0] = "invalid`ttext" }
            'surrogate' { $invalid.En.summaries[0] = [string][char]0xd800 }
            'summary' { $invalid.Zh.summaries[0] = 'x' * 141 }
            'notice' { $invalid.En.notices = @('x' * 2001) }
            'total' { $invalid.En.notices = @(('x' * 1001), ('y' * 1000)) }
        }
        Reject-Notes { New-SnapBarUpdateComment $invalid v0.1.0-rc.14 $three }
    }
    $maximum = $content | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $maximum.Zh.notices = @('字' * 2000); $maximum.En.notices = @('x' * 2000)
    $null = Read-NotesMetadata (New-SnapBarUpdateComment $maximum v0.1.0-rc.14 $three)
}
Check-Notes 'download and bilingual preview status precede summaries while usage and verification stay collapsed' {
    Assert-Notes ($preview.IndexOf('[下載 Windows x64 / Download Windows x64]') -lt $preview.IndexOf('## 繁體中文'))
    Assert-Notes ($preview.IndexOf('## 繁體中文') -lt $preview.IndexOf('## English') -and $preview.IndexOf('## English') -lt $preview.IndexOf('<details>'))
    Assert-Notes ($preview.Contains('Preview') -and $preview.Contains('Unsigned') -and $preview.Contains('Manual updates'))
    Assert-Notes ($preview.IndexOf('### 新增 AI') -gt $preview.IndexOf('<details>'))
    Assert-Notes ($preview.Contains('**使用方式：** ') -and -not $preview.Contains('**使用方式：**先'))
    Assert-Notes (-not [regex]::IsMatch($preview, '\{(changes|details|repository|commit|run|version|comparison)\}'))
}
Check-Notes 'committed translations are used even if the working catalog is broken' {
    Write-Notes 'packaging/RELEASE-NOTE-TRANSLATIONS.json' 'invalid working copy'
    $actual = Get-SnapBarReleaseContent -Root $source -Commit $three -PreviousTag baseline -AsObject
    Assert-Notes ($actual.Summary -ceq $content.Summary)
    Write-Notes 'packaging/RELEASE-NOTE-TRANSLATIONS.json' ([IO.File]::ReadAllText((Join-Path $PSScriptRoot '../packaging/RELEASE-NOTE-TRANSLATIONS.json')))
}
$fourth = [ordered]@{
    title = '額外更新'; summary = '另一項功能改善。'; highlights = @('完整保留額外項目。'); usage = '從設定開啟。'; priority = 0
    notice = @{ zh = '舊入口已移除，請先確認替代方式。'; en = 'The old entry is removed; check an alternative before updating.' }
    en = @{ title = 'Another change'; summary = 'An additional feature improvement.'; highlights = @('Keep every additional change.'); usage = 'Open it in settings.' }
}
$null = Git-Notes @('tag', 'notes-only-base', $three)
Write-Notes 'release-notes/fourth.json' ($fourth | ConvertTo-Json -Depth 8)
$four = Save-Notes
Check-Notes 'overflow stays in full details and important notices remain visible in both languages' {
    $actual = Get-SnapBarReleaseContent -Root $source -Commit $four -PreviousTag baseline -AsObject
    Assert-Notes ([regex]::Matches($actual.Summary, '(?m)^- ').Count -eq 6)
    Assert-Notes ($actual.Summary.Contains('另有 1 項') -and $actual.Summary.Contains('1 more update is'))
    Assert-Notes (-not $actual.Summary.Contains('另一項功能改善') -and $actual.Details.Contains('另一項功能改善') -and $actual.Details.Contains('Another change'))
    Assert-Notes ($actual.Summary.Contains($fourth.notice.zh) -and $actual.Summary.Contains($fourth.notice.en))
    $metadata = Read-NotesMetadata (New-SnapBarReleaseBody -Root $source -Commit $four -PreviousTag baseline -Version v0.1.0-rc.14 -Repository fixture/repo -RunId 123)
    Assert-Notes ($metadata.zh.notices -ccontains $fourth.notice.zh -and $metadata.en.notices -ccontains $fourth.notice.en)
    $noteOnly = Read-NotesMetadata (New-SnapBarReleaseBody -Root $source -Commit $four -PreviousTag notes-only-base -Version v0.1.0-rc.14 -Repository fixture/repo -RunId 123)
    Assert-Notes (-not $noteOnly.desktopChanged)
}
Check-Notes 'priority changes both languages together with stable path ordering for ties' {
    $fourth.priority = 100
    Write-Notes 'release-notes/fourth.json' ($fourth | ConvertTo-Json -Depth 8)
    $prioritized = Save-Notes
    $actual = Get-SnapBarReleaseContent -Root $source -Commit $prioritized -PreviousTag baseline -AsObject
    Assert-Notes ($actual.Summary.Contains('另一項功能改善') -and $actual.Summary.Contains('An additional feature improvement.'))
}
Check-Notes 'new notes without English and incomplete bilingual content are rejected' {
    $invalid = $fourth | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.PSObject.Properties.Remove('en')
    Write-Notes 'release-notes/missing-english.json' ($invalid | ConvertTo-Json -Depth 8)
    $missing = Save-Notes
    Reject-Notes { Get-SnapBarReleaseContent -Root $source -Commit $missing -PreviousTag baseline }
    $null = Git-Notes @('rm', '-q', 'release-notes/missing-english.json')
    $null = Save-Notes
    $invalid = $fourth | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $invalid.en.usage = ''
    Reject-Notes { ConvertFrom-SnapBarReleaseNote ($invalid | ConvertTo-Json -Depth 8) 'invalid.json' }
    $invalid.en.usage = 'Complete usage.'; $invalid.en.highlights = @('One', 'Two')
    Reject-Notes { ConvertFrom-SnapBarReleaseNote ($invalid | ConvertTo-Json -Depth 8) 'invalid.json' }
}
Check-Notes 'overlong summaries, invalid priority, unknown prerequisites and one-language notices fail' {
    foreach ($kind in @('summary', 'priority', 'requirements', 'notice')) {
        $invalid = $fourth | ConvertTo-Json -Depth 8 | ConvertFrom-Json
        switch ($kind) {
            'summary' { $invalid.en.summary = 'x' * 241 }
            'priority' { $invalid.priority = 1.5 }
            'requirements' { $invalid | Add-Member -NotePropertyName requirements -NotePropertyValue @('unknown') }
            'notice' { $invalid.notice.en = '' }
        }
        Reject-Notes { ConvertFrom-SnapBarReleaseNote ($invalid | ConvertTo-Json -Depth 8) 'invalid.json' }
    }
}
Check-Notes 'maintenance-only releases explain the same scope in Chinese and English' {
    $current = Git-Notes @('rev-parse', 'HEAD')
    $null = Git-Notes @('tag', 'maintenance-base', $current)
    Write-Notes 'site/index.html' 'maintenance'
    Write-Notes 'README.en.md' 'documentation update'
    $maintenance = Save-Notes
    $actual = Get-SnapBarReleaseContent -Root $source -Commit $maintenance -PreviousTag maintenance-base -AsObject
    Assert-Notes ($actual.Summary.Contains('本次沒有桌面功能更新') -and $actual.Summary.Contains('No desktop feature changes'))
    Assert-Notes ($actual.Details.Contains('網站') -and $actual.Details.Contains('website') -and -not $actual.Details.Contains('release tooling'))
    Assert-Notes (-not $actual.DesktopChanged -and $actual.Details.Contains('文件') -and $actual.Details.Contains('documentation'))
    $metadata = Read-NotesMetadata (New-SnapBarReleaseBody -Root $source -Commit $maintenance -PreviousTag maintenance-base -Version v0.1.0 -Repository fixture/repo -RunId 123)
    Assert-Notes (-not $metadata.desktopChanged -and $metadata.zh.summaries.Count -eq 1 -and $metadata.en.notices.Count -eq 0)
}
Check-Notes 'all repository notes have complete reviewed bilingual content without changing published Chinese files' {
    $catalog = ConvertFrom-SnapBarReleaseCatalog ([IO.File]::ReadAllText((Join-Path $PSScriptRoot '../packaging/RELEASE-NOTE-TRANSLATIONS.json')))
    foreach ($file in Get-ChildItem (Join-Path $PSScriptRoot '../release-notes') -Filter '*.json') {
        $note = ConvertFrom-SnapBarReleaseNote ([IO.File]::ReadAllText($file.FullName)) $file.Name
        $english = if ($note.en) { $note.en } else { $catalog[$file.Name] }
        Assert-Notes ($null -ne $english -and $english.highlights.Count -eq $note.highlights.Count)
    }
}
Check-Notes 'staged bilingual notes and translations are checked independently of working-copy repairs' {
    $savedIndex = $env:GIT_INDEX_FILE
    $realIndex = & git -C $script:SnapBarRepo rev-parse --git-path index
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate index for release-note checks.' }
    if (-not [IO.Path]::IsPathRooted($realIndex)) { $realIndex = Join-Path $script:SnapBarRepo $realIndex }
    $temporaryIndex = Join-Path $run 'release-note-index'
    Copy-Item -LiteralPath $realIndex -Destination $temporaryIndex
    $name = 'guardrail-' + [guid]::NewGuid().ToString('N') + '.json'
    $relative = "release-notes/$name"
    $fixturePath = Join-Path $script:SnapBarRepo $relative
    $catalogRelative = 'packaging/RELEASE-NOTE-TRANSLATIONS.json'
    $catalogPath = Join-Path $script:SnapBarRepo $catalogRelative
    $originalCatalog = [IO.File]::ReadAllBytes($catalogPath)
    $legacy = $fourth | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $legacy.PSObject.Properties.Remove('en')
    function Write-GuardNote($Value) { [IO.File]::WriteAllText($fixturePath, ($Value | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false)) }
    function Stage-Guard([string]$Path) {
        & git -C $script:SnapBarRepo add -- $Path
        if ($LASTEXITCODE -ne 0) { throw 'Cannot stage release-note fixture.' }
    }
    function Check-Guard([string]$Name) { return Invoke-SnapBarCommand $Name (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check.ps1'), '-Staged') $run }
    try {
        $env:GIT_INDEX_FILE = $temporaryIndex
        Stage-Guard $catalogRelative
        Write-GuardNote $legacy; Stage-Guard $relative
        Write-GuardNote $fourth
        $result = Check-Guard 'StagedMissingEnglish'
        Assert-Notes (-not $result.success -and [IO.File]::ReadAllText($result.log).Contains('require reviewed en content'))
        Stage-Guard $relative
        Write-GuardNote $legacy
        Assert-SnapBarCommand (Check-Guard 'StagedValidEnglish')
        $catalog = ConvertFrom-SnapBarReleaseCatalog ([Text.Encoding]::UTF8.GetString($originalCatalog))
        $catalog[$name] = $fourth.en
        [IO.File]::WriteAllText($catalogPath, ($catalog | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
        Stage-Guard $catalogRelative; Stage-Guard $relative
        [IO.File]::WriteAllText($catalogPath, 'invalid working catalog', [Text.UTF8Encoding]::new($false))
        Assert-SnapBarCommand (Check-Guard 'StagedValidCatalog')
        $catalog[$name].usage = ''
        [IO.File]::WriteAllText($catalogPath, ($catalog | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
        Stage-Guard $catalogRelative
        [IO.File]::WriteAllBytes($catalogPath, $originalCatalog)
        $result = Check-Guard 'StagedInvalidCatalog'
        Assert-Notes (-not $result.success -and [IO.File]::ReadAllText($result.log).Contains('single-line usage'))
    }
    finally {
        $env:GIT_INDEX_FILE = $savedIndex
        [IO.File]::WriteAllBytes($catalogPath, $originalCatalog)
        if ([IO.File]::Exists($fixturePath)) { [IO.File]::Delete($fixturePath) }
    }
}
Write-Host "Preview: $previewPath"
Write-Host "$passed passed, 0 failed"
