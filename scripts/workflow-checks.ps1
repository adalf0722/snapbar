function Get-SnapBarActionlint {
    $version = '1.7.12'
    $root = Join-Path $script:SnapBarRepo ".tools/actionlint/$version"
    $executable = Join-Path $root 'actionlint.exe'
    if (-not [IO.File]::Exists($executable)) {
        [IO.Directory]::CreateDirectory($root) | Out-Null
        $archive = Join-Path $root 'actionlint.zip'
        $expected = '6e7241b51e6817ea6a047693d8e6fed13b31819c9a0dd6c5a726e1592d22f6e9'
        Invoke-WebRequest -Uri "https://github.com/rhysd/actionlint/releases/download/v$version/actionlint_${version}_windows_amd64.zip" -OutFile $archive
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
            throw 'actionlint download checksum mismatch.'
        }
        Expand-Archive -LiteralPath $archive -DestinationPath $root -Force
    }
    $description = & $executable -version
    if ($LASTEXITCODE -ne 0 -or @($description)[0] -ne $version) { throw "Expected actionlint $version." }
    return $executable
}

function Invoke-SnapBarWorkflowCheck([string[]]$ChangedFiles, [switch]$Staged) {
    if ($Staged -and -not @($ChangedFiles | Where-Object { $_ -match '^\.github/(workflows/[^/]+\.ya?ml|actionlint\.ya?ml)$' }).Count) {
        return
    }
    if ($Staged) {
        $listed = & git -C $script:SnapBarRepo -c core.quotepath=false ls-files --cached -z
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate staged workflows.' }
        $files = @($listed -split "`0" | Where-Object { $_ })
    }
    else { $files = Get-SnapBarFiles }
    $workflows = @($files | Where-Object { $_ -match '^\.github/workflows/[^/]+\.ya?ml$' })
    if (-not $workflows.Count) { return }
    $run = New-SnapBarRun 'workflows'
    $snapshot = Join-Path $run 'snapshot'
    $config = @($files | Where-Object { $_ -match '^\.github/actionlint\.ya?ml$' } | Sort-Object | Select-Object -First 1)
    foreach ($file in @($workflows) + @($config)) {
        $bytes = if ($Staged) { Read-SnapBarIndexBlob $file } else { [IO.File]::ReadAllBytes((Join-Path $script:SnapBarRepo $file)) }
        $null = ConvertFrom-SnapBarUtf8 $bytes $file
        $destination = Join-Path $snapshot $file
        [IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
        [IO.File]::WriteAllBytes($destination, $bytes)
    }
    $configPath = Join-Path $snapshot '.github/actionlint.yaml'
    if ($config.Count) { $configPath = Join-Path $snapshot $config[0] }
    else { [IO.File]::WriteAllText($configPath, '{}', [Text.UTF8Encoding]::new($false)) }
    $arguments = @('-no-color', '-oneline', '-shellcheck=', '-pyflakes=', '-config-file', $configPath)
    $arguments += @($workflows | ForEach-Object { Join-Path $snapshot $_ })
    $result = Invoke-SnapBarCommand 'Actionlint' (Get-SnapBarActionlint) $arguments $run
    Assert-SnapBarCommand $result
}
