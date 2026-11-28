param(
    [AllowEmptyString()][string]$Before,
    [Parameter(Mandatory)][string]$After,
    [Parameter(Mandatory)][string]$Ref
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path $PSScriptRoot -Parent
$changes = [pscustomobject]@{ Desktop = $true; Website = $false }

# Tags explicitly request a package. Branch updates and PR merge commits can be compared.
if ($Ref -notmatch '^refs/(heads|pull)/' -or $Before -notmatch '^[0-9a-f]{40,64}$' -or $Before -match '^0+$') {
    $changes
    exit 0
}
& git -C $repo cat-file -e "${Before}^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    $changes
    exit 0
}
& git -C $repo merge-base --is-ancestor $Before $After
if ($LASTEXITCODE -gt 1) { throw 'Cannot verify the push ancestry.' }
if ($LASTEXITCODE -eq 1) {
    $changes
    exit 0
}

# Use the two trees, including deletions and both sides of renames, without parsing path names.
$websitePaths = @('site/', 'scripts/build-site.ps1', 'scripts/site-common.ps1', 'scripts/test-site.ps1')
$lightweightPaths = $websitePaths + @('docs/', '.superpowers/', 'release-notes/', 'README.md', 'README.en.md', 'AGENTS.md')
$exclusions = @($lightweightPaths | ForEach-Object { ":(top,exclude)$_" })
& git -C $repo diff --quiet --no-ext-diff --no-renames --ignore-submodules=none $Before $After -- @exclusions
if ($LASTEXITCODE -gt 1) { throw 'Cannot classify desktop changes in the push.' }
$changes.Desktop = $LASTEXITCODE -eq 1
& git -C $repo diff --quiet --no-ext-diff --no-renames $Before $After -- @websitePaths
if ($LASTEXITCODE -gt 1) { throw 'Cannot classify website changes in the push.' }
$changes.Website = $LASTEXITCODE -eq 1
# Git uses exit code 1 for differences. Actions propagates the last native exit code.
$changes
exit 0
