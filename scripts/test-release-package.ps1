param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Commit,
    [ValidateSet('', 'true', 'false')][string]$ReleasePrerelease = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'version-common.ps1')
$identity = Get-SnapBarVersion $Version
if ($ReleasePrerelease) {
    if (-not $identity.IsPublic -or (($ReleasePrerelease -eq 'true') -ne ($identity.Channel -eq 'Preview'))) {
        throw 'Release prerelease flag and version channel do not match.'
    }
}
$expected = ([IO.File]::ReadAllText("$Archive.sha256", [Text.Encoding]::UTF8)).Split(' ')[0]
if ($expected -cnotmatch '^[0-9a-f]{64}$' -or (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ine $expected) {
    throw 'Release ZIP checksum mismatch.'
}
Assert-SnapBarArchiveVersion $Archive $identity.Version $Commit
Write-Host "PASS release package version and source: $Version ($Commit)"
