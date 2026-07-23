$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$release = Get-Content -LiteralPath (Join-Path $repo 'companion\release-info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$package = Get-Content -LiteralPath (Join-Path $repo 'companion\package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$pluginSource = Get-Content -LiteralPath (Join-Path $repo 'plugin\src\LilithModPlugin.cs') -Raw -Encoding UTF8
if ($pluginSource -notmatch 'PluginVersion\s*=\s*"([^"]+)"') { throw 'PluginVersion was not found.' }
$pluginVersion = $Matches[1]

if ([string]$release.version -ne [string]$package.version `
    -or [string]$release.version -ne $pluginVersion) {
    throw "Version mismatch: release=$($release.version), package=$($package.version), plugin=$pluginVersion"
}
if ([string]$release.version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Public release version is not stable semver: $($release.version)"
}
if ([string]$release.github_repository -notmatch '^[^/\s]+/The-NOexistenceN-of-Lilith-Mod$') {
    throw "Final GitHub repository is not configured: $($release.github_repository)"
}

$tokens = $null
$parseErrors = $null
$configurePath = Join-Path $repo 'companion\configure.ps1'
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $configurePath,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors | ForEach-Object Message | Out-String) }
$definition = $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] `
        -and $node.Name -eq 'ConvertTo-ReleaseVersion'
}, $true) | Select-Object -First 1
if ($null -eq $definition) { throw 'ConvertTo-ReleaseVersion was not found.' }
Invoke-Expression $definition.Extent.Text

if ((ConvertTo-ReleaseVersion 'v0.2.0') -ne [Version]'0.2.0') { throw 'Stable tag parsing failed.' }
if ((ConvertTo-ReleaseVersion 'v0.2.1') -le (ConvertTo-ReleaseVersion '0.2.0')) { throw 'New-version comparison failed.' }
if ($null -ne (ConvertTo-ReleaseVersion 'latest')) { throw 'Invalid release tag was accepted.' }

Write-Host "Release metadata tests passed for v$($release.version)."
