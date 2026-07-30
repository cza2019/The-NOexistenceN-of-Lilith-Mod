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
$unityVersion = '2021.3.45'
$unityBaseLibraries = Join-Path $GameDir "BepInEx\unity-libs\$unityVersion.zip"
$unityBaseLibrariesSha256 = '3cfde1613715c966cb8084087c37081ed5896871b39194d45a907a69dd7da9e5'
foreach ($required in @(
    $dotnet,
    $dotnetLicense,
    $dotnetNotices,
    $bepInExLicense,
    (Join-Path $bepInExRoot 'winhttp.dll'),
    (Join-Path $bepInExRoot 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'),
    (Join-Path $bepInExRoot 'dotnet\coreclr.dll'),
    $unityBaseLibraries,
    (Join-Path $GameDir 'BepInEx\interop\Assembly-CSharp.dll')
)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required build input not found: $required" }
}
$actualUnityBaseLibrariesSha256 = (Get-FileHash -LiteralPath $unityBaseLibraries -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualUnityBaseLibrariesSha256 -ne $unityBaseLibrariesSha256) {
    throw "Unity $unityVersion base-libraries hash mismatch: $actualUnityBaseLibrariesSha256"
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

& $dotnet publish (Join-Path $repo 'grounding-viewer\LilithGroundingViewer.csproj') `
    -c Release -r win-x64 --self-contained true --nologo
if ($LASTEXITCODE -ne 0) { throw 'Grounding viewer publish failed.' }

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

# BepInEx normally downloads these reference assemblies on the first IL2CPP
# launch. Bundle the exact, verified archive so clean installations do not
# depend on unity.bepinex.dev being reachable.
$unityLibrariesOutput = Join-Path $OutputDirectory 'BepInEx\unity-libs'
New-Item -ItemType Directory -Path $unityLibrariesOutput -Force | Out-Null
Copy-Item -LiteralPath $unityBaseLibraries `
    -Destination (Join-Path $unityLibrariesOutput "$unityVersion.zip") -Force

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
foreach ($file in @('localization.js', 'lore.js', 'server.js')) {
    Copy-Item -LiteralPath (Join-Path $repo "companion\src\$file") -Destination $companionSourceOutput
}
Copy-Item -LiteralPath (Join-Path $repo 'companion\assets\wechat-reward-code.png') -Destination $companionAssetsOutput
Copy-Item -LiteralPath $NodePath -Destination (Join-Path $companionOutput 'node.exe')
Copy-Item -LiteralPath $nodeLicense -Destination (Join-Path $companionOutput 'node-LICENSE.txt')

$localesOutput = Join-Path $pluginRoot 'locales'
Copy-Item -LiteralPath (Join-Path $repo 'locales') -Destination $pluginRoot -Recurse

$characterOutput = Join-Path $pluginRoot 'character'
$loreOutput = Join-Path $characterOutput 'lore'
New-Item -ItemType Directory -Path $loreOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'character\lilith.json') -Destination $characterOutput
Copy-Item -LiteralPath (Join-Path $repo 'character\locales') -Destination $characterOutput -Recurse
Copy-Item -Path (Join-Path $repo 'character\lore\index*.json') -Destination $loreOutput
Copy-Item -LiteralPath (Join-Path $repo 'plugin\assets') -Destination $pluginRoot -Recurse

$mediaOutput = Join-Path $repo 'media-bridge\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\LilithMediaBridge.exe'
if (-not (Test-Path -LiteralPath $mediaOutput)) { throw "Media bridge output not found: $mediaOutput" }
$mediaDestination = Join-Path $pluginRoot 'media-bridge'
New-Item -ItemType Directory -Path $mediaDestination -Force | Out-Null
Copy-Item -LiteralPath $mediaOutput -Destination (Join-Path $mediaDestination 'LilithMediaBridge.exe')

$viewerOutput = Join-Path $repo 'grounding-viewer\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish'
$viewerExe = Join-Path $viewerOutput 'LilithGroundingViewer.exe'
$viewerDll = Join-Path $viewerOutput 'LilithGroundingViewer.dll'
if (-not (Test-Path -LiteralPath $viewerExe)) { throw "Grounding viewer output not found: $viewerExe" }
& $dotnet $viewerDll --smoke
if ($LASTEXITCODE -ne 0) { throw 'Grounding viewer smoke test failed.' }
& $dotnet $viewerDll --smoke-https 'https://example.com/'
if ($LASTEXITCODE -ne 0) { throw 'Grounding viewer rejected an HTTPS link.' }
& $dotnet $viewerDll --smoke-https 'http://example.com/'
if ($LASTEXITCODE -eq 0) { throw 'Grounding viewer accepted a non-HTTPS external link.' }
$viewerDestination = Join-Path $pluginRoot 'grounding-viewer'
New-Item -ItemType Directory -Path $viewerDestination -Force | Out-Null
Get-ChildItem -LiteralPath $viewerOutput -Force | Where-Object { $_.Extension -ne '.pdb' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $viewerDestination -Recurse }
$nugetPackages = [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'Process')
if ([string]::IsNullOrWhiteSpace($nugetPackages)) {
    $nugetPackages = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
}
$webViewPackage = Join-Path $nugetPackages 'microsoft.web.webview2\1.0.4078.44'
$webViewLicense = Join-Path $webViewPackage 'LICENSE.txt'
$webViewNotice = Join-Path $webViewPackage 'NOTICE.txt'
foreach ($requiredWebViewFile in @($webViewLicense, $webViewNotice)) {
    if (-not (Test-Path -LiteralPath $requiredWebViewFile)) {
        throw "WebView2 license file not found after restore: $requiredWebViewFile"
    }
}
Copy-Item -LiteralPath $webViewLicense -Destination (Join-Path $viewerDestination 'WebView2-LICENSE.txt')
Copy-Item -LiteralPath $webViewNotice -Destination (Join-Path $viewerDestination 'WebView2-NOTICE.txt')

foreach ($file in @('README.md', 'README.zh-TW.md', 'README.en.md', 'README.ja.md', 'LICENSE', 'ASSET-NOTICE.md', 'THIRD-PARTY-NOTICES.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $OutputDirectory
}
Copy-Item -LiteralPath (Join-Path $repo 'docs\installation.md') -Destination (Join-Path $OutputDirectory 'INSTALL.md')
$publicDocsOutput = Join-Path $OutputDirectory 'docs'
New-Item -ItemType Directory -Path $publicDocsOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'docs\installation.md') -Destination $publicDocsOutput
foreach ($localizedInstallation in @('installation.zh-TW.md', 'installation.en.md', 'installation.ja.md')) {
    Copy-Item -LiteralPath (Join-Path $repo "docs\$localizedInstallation") -Destination $publicDocsOutput
}
$releaseNotesPath = Join-Path $repo "docs\release-notes-v$version.md"
if (-not (Test-Path -LiteralPath $releaseNotesPath)) {
    throw "Release notes not found for v$version`: $releaseNotesPath"
}
Copy-Item -LiteralPath $releaseNotesPath -Destination $publicDocsOutput

$releaseManifest = [ordered]@{
    schema_version = 1
    mod_version = $version
    github_repository = $repository
    platform = 'win-x64'
    game = 'The NOexistenceN of Lilith'
    bep_in_ex = '6.0.0-be.769 IL2CPP'
    unity_base_libraries = $unityVersion
    unity_base_libraries_sha256 = $unityBaseLibrariesSha256
    node = $nodeVersion
    media_bridge = 'self-contained net8.0 win-x64'
    grounding_viewer = 'self-contained net8.0 win-x64'
    webview2 = '1.0.4078.44 (Evergreen Runtime required)'
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
Write-Host "Bundled Unity base libraries: $unityVersion ($unityBaseLibrariesSha256)"
Write-Host "Bundled Node.js: $nodeVersion"
Write-Host 'No original-game binaries, local API configuration, runtime memory, tests, or debug symbols were copied.'

if (-not $SkipArchive) {
    New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
    $archivePath = Join-Path $distRoot ($packageName + '.zip')
    $checksumPath = $archivePath + '.sha256.txt'
    Remove-Item -LiteralPath $archivePath,$checksumPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $unityEntryName = "BepInEx/unity-libs/$unityVersion.zip"
        $unityEntry = $archive.Entries | Where-Object {
            $_.FullName.Replace('\', '/') -eq $unityEntryName
        } | Select-Object -First 1
        if ($null -eq $unityEntry -or $unityEntry.Length -ne (Get-Item -LiteralPath $unityBaseLibraries).Length) {
            throw "Release archive is missing the verified Unity base libraries: $unityEntryName"
        }
    }
    finally {
        $archive.Dispose()
    }
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        $checksumPath,
        "$archiveHash *$([IO.Path]::GetFileName($archivePath))$([Environment]::NewLine)",
        (New-Object Text.UTF8Encoding($false)))
    Write-Host "Archive: $archivePath"
    Write-Host "SHA-256: $archiveHash"
}
