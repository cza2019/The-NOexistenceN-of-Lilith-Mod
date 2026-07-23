param(
    [string]$GameDir = '',
    [string]$OutputDirectory = '',
    [string]$NodePath = '',
    [switch]$SkipArchive
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$releaseInfoPath = Join-Path $repo 'companion\release-info.json'
$releaseInfo = Get-Content -LiteralPath $releaseInfoPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = ([string]$releaseInfo.version).Trim().TrimStart('v', 'V')
$repository = ([string]$releaseInfo.github_repository).Trim().Trim('/')
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "release-info.json contains an invalid stable version: $version"
}
if ($repository -notmatch '^[^/\s]+/[^/\s]+$') {
    throw 'release-info.json must contain the final GitHub owner/repository before building a release.'
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = [Environment]::GetEnvironmentVariable('LILITH_GAME_DIR', 'Process')
}
if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = [Environment]::GetEnvironmentVariable('LILITH_GAME_DIR', 'User')
}
if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = [Environment]::GetEnvironmentVariable('LILITH_GAME_DIR', 'Machine')
}
if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw 'Game directory is required. Pass -GameDir or set LILITH_GAME_DIR.'
}
$GameDir = [IO.Path]::GetFullPath($GameDir)

$packageName = "The-NOexistenceN-of-Lilith-Mod-v$version-win-x64"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repo ("dist\$packageName")
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$distRoot = Join-Path $repo 'dist'
$distRootFull = [IO.Path]::GetFullPath($distRoot).TrimEnd('\') + '\'
if (-not ($OutputDirectory.TrimEnd('\') + '\').StartsWith($distRootFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Bundle output must stay inside the repository dist directory: $OutputDirectory"
}

$dotnet = Join-Path $repo 'vendor\dotnet-sdk\dotnet.exe'
$dotnetLicense = Join-Path $repo 'vendor\dotnet-sdk\LICENSE.txt'
$dotnetNotices = Join-Path $repo 'vendor\dotnet-sdk\ThirdPartyNotices.txt'
$bepInExRoot = Join-Path $repo 'vendor\bepinex-769'
$bepInExLicense = Join-Path $bepInExRoot 'LICENSE'
foreach ($required in @(
    $dotnet,
    $dotnetLicense,
    $dotnetNotices,
    $bepInExLicense,
    (Join-Path $bepInExRoot 'winhttp.dll'),
    (Join-Path $bepInExRoot 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'),
    (Join-Path $bepInExRoot 'dotnet\coreclr.dll'),
    (Join-Path $GameDir 'BepInEx\interop\Assembly-CSharp.dll')
)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required build input not found: $required" }
}

if ([string]::IsNullOrWhiteSpace($NodePath)) {
    foreach ($scope in @('Process', 'Machine', 'User')) {
        $NodePath = [Environment]::GetEnvironmentVariable('LILITH_NODE_PATH', $scope)
        if (-not [string]::IsNullOrWhiteSpace($NodePath)) { break }
    }
}
if ([string]::IsNullOrWhiteSpace($NodePath)) {
    $nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
    if ($null -ne $nodeCommand) { $NodePath = $nodeCommand.Source }
}
if ([string]::IsNullOrWhiteSpace($NodePath) -or -not (Test-Path -LiteralPath $NodePath)) {
    throw 'Node.js runtime not found. Pass -NodePath or set LILITH_NODE_PATH.'
}
$NodePath = (Resolve-Path -LiteralPath $NodePath).Path
$nodeVersion = (& $NodePath --version).Trim()
if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v24\.') {
    throw "The Windows bundle requires Node.js 24 LTS; found: $nodeVersion"
}
$nodeLicense = Join-Path (Split-Path -Parent $NodePath) 'LICENSE'
if (-not (Test-Path -LiteralPath $nodeLicense)) {
    throw "Node.js license is missing beside the runtime: $nodeLicense"
}

& $dotnet build (Join-Path $repo 'plugin\LilithMod.csproj') `
    -c Release --nologo -p:GameDir=$GameDir
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

& $dotnet publish (Join-Path $repo 'media-bridge\LilithMediaBridge.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=true --nologo
if ($LASTEXITCODE -ne 0) { throw 'Media bridge publish failed.' }

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Get-ChildItem -LiteralPath $bepInExRoot -Force |
    Where-Object { $_.Name -ne 'LICENSE' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $OutputDirectory -Recurse }
Copy-Item -LiteralPath $bepInExLicense -Destination (Join-Path $OutputDirectory 'BepInEx-LICENSE.txt')
Copy-Item -LiteralPath $dotnetLicense -Destination (Join-Path $OutputDirectory 'DotNet-LICENSE.txt')
Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $OutputDirectory 'DotNet-ThirdPartyNotices.txt')

$bepInExConfigOutput = Join-Path $OutputDirectory 'BepInEx\config'
New-Item -ItemType Directory -Path $bepInExConfigOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'packaging\BepInEx.cfg') `
    -Destination (Join-Path $bepInExConfigOutput 'BepInEx.cfg') -Force

$pluginRoot = Join-Path $OutputDirectory 'BepInEx\plugins'
New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'plugin\bin\Release\net6.0\LilithMod.dll') -Destination $pluginRoot

$companionOutput = Join-Path $pluginRoot 'companion'
$companionSourceOutput = Join-Path $companionOutput 'src'
$companionAssetsOutput = Join-Path $companionOutput 'assets'
New-Item -ItemType Directory -Path $companionSourceOutput,$companionAssetsOutput -Force | Out-Null
foreach ($file in @('config.example.json', 'configure.ps1', 'release-info.json')) {
    Copy-Item -LiteralPath (Join-Path $repo "companion\$file") -Destination $companionOutput
}
foreach ($file in @('lore.js', 'server.js')) {
    Copy-Item -LiteralPath (Join-Path $repo "companion\src\$file") -Destination $companionSourceOutput
}
Copy-Item -LiteralPath (Join-Path $repo 'companion\assets\wechat-reward-code.png') -Destination $companionAssetsOutput
Copy-Item -LiteralPath $NodePath -Destination (Join-Path $companionOutput 'node.exe')
Copy-Item -LiteralPath $nodeLicense -Destination (Join-Path $companionOutput 'node-LICENSE.txt')

$characterOutput = Join-Path $pluginRoot 'character'
$loreOutput = Join-Path $characterOutput 'lore'
New-Item -ItemType Directory -Path $loreOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'character\lilith.json') -Destination $characterOutput
Copy-Item -LiteralPath (Join-Path $repo 'character\lore\index.json') -Destination $loreOutput
Copy-Item -LiteralPath (Join-Path $repo 'plugin\assets') -Destination $pluginRoot -Recurse

$mediaOutput = Join-Path $repo 'media-bridge\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\LilithMediaBridge.exe'
if (-not (Test-Path -LiteralPath $mediaOutput)) { throw "Media bridge output not found: $mediaOutput" }
$mediaDestination = Join-Path $pluginRoot 'media-bridge'
New-Item -ItemType Directory -Path $mediaDestination -Force | Out-Null
Copy-Item -LiteralPath $mediaOutput -Destination (Join-Path $mediaDestination 'LilithMediaBridge.exe')

foreach ($file in @('README.md', 'LICENSE', 'ASSET-NOTICE.md', 'THIRD-PARTY-NOTICES.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $OutputDirectory
}
Copy-Item -LiteralPath (Join-Path $repo 'docs\installation.md') -Destination (Join-Path $OutputDirectory 'INSTALL.md')
$publicDocsOutput = Join-Path $OutputDirectory 'docs'
New-Item -ItemType Directory -Path $publicDocsOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'docs\installation.md') -Destination $publicDocsOutput

$releaseManifest = [ordered]@{
    schema_version = 1
    mod_version = $version
    github_repository = $repository
    platform = 'win-x64'
    game = 'The NOexistenceN of Lilith'
    bep_in_ex = '6.0.0-be.769 IL2CPP'
    node = $nodeVersion
    media_bridge = 'self-contained net8.0 win-x64'
    built_at_utc = [DateTime]::UtcNow.ToString('o')
}
[IO.File]::WriteAllText(
    (Join-Path $OutputDirectory 'RELEASE-MANIFEST.json'),
    (($releaseManifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine),
    (New-Object Text.UTF8Encoding($false)))

$forbiddenFiles = Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Where-Object {
    $_.FullName -match '[\\/](interop|cache|tests?|node_modules)[\\/]' `
        -or $_.Name -match '\.(pdb|dmp|log)$' `
        -or $_.Name -in @('runtime.json', 'config.json', 'auth.json')
}
if ($forbiddenFiles) {
    throw "Release contains forbidden development/runtime files:`n$($forbiddenFiles.FullName -join "`n")"
}

$textFiles = Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Where-Object {
    $_.Length -lt 5MB -and $_.Extension -in @('.json', '.js', '.ps1', '.md', '.txt', '.cfg')
}
$secretMatches = $textFiles | Select-String -Pattern @(
    'sk-[A-Za-z0-9_-]{20,}',
    'testvideo\.site',
    'OPENAI_API_KEY',
    'C:\\Users\\',
    'E:\\SteamLibrary'
) -ErrorAction SilentlyContinue
if ($secretMatches) {
    throw "Release audit found a secret or machine-specific literal:`n$($secretMatches | Out-String)"
}

$manifestLines = foreach ($file in Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Sort-Object FullName) {
    $relative = $file.FullName.Substring($OutputDirectory.TrimEnd('\').Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$relative"
}
[IO.File]::WriteAllLines(
    (Join-Path $OutputDirectory 'MANIFEST.sha256'),
    $manifestLines,
    (New-Object Text.UTF8Encoding($false)))

Write-Host "Bundle created: $OutputDirectory"
Write-Host "Version: $version"
Write-Host 'Bundled BepInEx: 6.0.0-be.769'
Write-Host "Bundled Node.js: $nodeVersion"
Write-Host 'No original-game binaries, local API configuration, runtime memory, tests, or debug symbols were copied.'

if (-not $SkipArchive) {
    New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
    $archivePath = Join-Path $distRoot ($packageName + '.zip')
    $checksumPath = $archivePath + '.sha256.txt'
    Remove-Item -LiteralPath $archivePath,$checksumPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        $checksumPath,
        "$archiveHash *$([IO.Path]::GetFileName($archivePath))$([Environment]::NewLine)",
        (New-Object Text.UTF8Encoding($false)))
    Write-Host "Archive: $archivePath"
    Write-Host "SHA-256: $archiveHash"
}
