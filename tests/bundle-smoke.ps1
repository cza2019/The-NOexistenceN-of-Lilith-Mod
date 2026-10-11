param(
    [Parameter(Mandatory = $true)]
    [string]$BundleDirectory
)

$ErrorActionPreference = 'Stop'
$powershellExe = Join-Path $PSHOME 'powershell.exe'
if (-not (Test-Path -LiteralPath $powershellExe)) {
    $powershellExe = (Get-Command powershell.exe -ErrorAction Stop).Source
}
# Some automation hosts expose both `Path` and `PATH` in the same Windows
# environment block. Windows PowerShell's Start-Process builds a
# case-insensitive dictionary and otherwise throws before the smoke process
# starts. Keep the canonical `Path` entry for this test process.
$pathKeys = @([Environment]::GetEnvironmentVariables().Keys |
    Where-Object { [string]$_ -ieq 'path' })
if ($pathKeys -contains 'Path' -and $pathKeys -contains 'PATH') {
    [Environment]::SetEnvironmentVariable('PATH', $null, 'Process')
}
$bundle = (Resolve-Path -LiteralPath $BundleDirectory).Path
$localizedFiles = @(
    'BepInEx\plugins\companion\src\provider-request.js',
    'BepInEx\plugins\companion\src\provider-test.js',
    'BepInEx\plugins\companion\src\network.js',
    'BepInEx\plugins\locales\zh-CN.json',
    'BepInEx\plugins\locales\zh-TW.json',
    'BepInEx\plugins\locales\en.json',
    'BepInEx\plugins\locales\ja.json',
    'BepInEx\plugins\character\locales\zh-TW\lilith.json',
    'BepInEx\plugins\character\locales\en\lilith.json',
    'BepInEx\plugins\character\locales\ja\lilith.json',
    'BepInEx\plugins\character\lore\index.zh-TW.json',
    'BepInEx\plugins\character\lore\index.en.json',
    'BepInEx\plugins\character\lore\index.ja.json'
)
foreach ($relative in $localizedFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $bundle $relative))) {
        throw "Localized bundle file is missing: $relative"
    }
}
$viewerRoot = Join-Path $bundle 'BepInEx\plugins\grounding-viewer'
$viewerExe = Join-Path $viewerRoot 'LilithGroundingViewer.exe'
foreach ($viewerFile in @(
    $viewerExe,
    (Join-Path $viewerRoot 'WebView2Loader.dll'),
    (Join-Path $viewerRoot 'WebView2-LICENSE.txt'),
    (Join-Path $viewerRoot 'WebView2-NOTICE.txt')
)) {
    if (-not (Test-Path -LiteralPath $viewerFile)) {
        throw "Grounding viewer bundle file is missing: $viewerFile"
    }
}
$viewerSmoke = Start-Process -FilePath $viewerExe `
    -ArgumentList '--smoke' -WindowStyle Hidden -Wait -PassThru
if ($viewerSmoke.ExitCode -ne 0) { throw 'Bundled grounding viewer smoke test failed.' }
$releaseManifest = Get-Content -LiteralPath (Join-Path $bundle 'RELEASE-MANIFEST.json') `
    -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$releaseManifest.webview2 -notmatch '1\.0\.4078\.44') {
    throw 'Release manifest does not record the expected WebView2 SDK version.'
}
$unityLibraries = Join-Path $bundle 'BepInEx\unity-libs\2021.3.45.zip'
if (-not (Test-Path -LiteralPath $unityLibraries)) {
    throw 'Bundled Unity 2021.3.45 base libraries are missing.'
}
$unityLibrariesHash = (Get-FileHash -LiteralPath $unityLibraries -Algorithm SHA256).Hash.ToLowerInvariant()
if ($unityLibrariesHash -ne '3cfde1613715c966cb8084087c37081ed5896871b39194d45a907a69dd7da9e5') {
    throw "Bundled Unity base-libraries hash mismatch: $unityLibrariesHash"
}
$temp = Join-Path $env:TEMP ('lilith-bundle-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $bundle 'BepInEx\plugins\companion\config.example.json') `
    -Destination (Join-Path $temp 'config.json')
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$testPort = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$configPath = Join-Path $temp 'config.json'
$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$config.listen_port = $testPort
[IO.File]::WriteAllText(
    $configPath,
    (($config | ConvertTo-Json -Depth 10) + [Environment]::NewLine),
    (New-Object Text.UTF8Encoding($false)))

$oldData = $env:LILITH_AI_DATA_DIR
$oldCharacter = $env:LILITH_CHARACTER_FILE
$oldLore = $env:LILITH_LORE_FILE
$oldAppData = $env:APPDATA
$oldConfigureSmoke = $env:LILITH_CONFIGURE_STARTUP_SMOKE
$process = $null
try {
    $env:APPDATA = $temp
    $env:LILITH_CONFIGURE_STARTUP_SMOKE = '1'
    & $powershellExe `
        -NoProfile `
        -STA `
        -ExecutionPolicy Bypass `
        -File (Join-Path $bundle 'BepInEx\plugins\companion\configure.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Bundled configure.ps1 startup smoke test failed with exit code $LASTEXITCODE."
    }

    $env:LILITH_AI_DATA_DIR = $temp
    $env:LILITH_CHARACTER_FILE = $null
    $env:LILITH_LORE_FILE = $null
    $serverScript = Join-Path $bundle 'BepInEx\plugins\companion\src\server.js'
    $process = Start-Process `
        -FilePath (Join-Path $bundle 'BepInEx\plugins\companion\node.exe') `
        -ArgumentList ('"{0}"' -f $serverScript) `
        -WorkingDirectory (Join-Path $bundle 'BepInEx\plugins\companion') `
        -WindowStyle Hidden `
        -PassThru `
        -RedirectStandardOutput (Join-Path $temp 'stdout.log') `
        -RedirectStandardError (Join-Path $temp 'stderr.log')

    $runtime = Join-Path $temp 'runtime.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $runtime) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $runtime)) {
        throw 'Bundled Companion did not create runtime metadata.'
    }

    $meta = Get-Content -LiteralPath $runtime -Raw -Encoding UTF8 | ConvertFrom-Json
    $health = Invoke-RestMethod `
        -Uri "http://$($meta.host):$($meta.port)/health" `
        -TimeoutSec 5
    if (-not [bool]$health.ok) { throw 'Bundled Companion health check failed.' }

    $headers = @{ Authorization = "Bearer $($meta.token)" }
    $japaneseReply = Invoke-RestMethod `
        -Uri "http://$($meta.host):$($meta.port)/v1/session/bundle-smoke/message" `
        -Method Post `
        -Headers $headers `
        -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes((@{
            protocol_version = 1
            request_id = 'bundle-ja'
            player = @{ name = 'LocaleTester' }
            mode = 'in_character'
            locale = 'ja'
            message = 'Please reply briefly.'
        } | ConvertTo-Json -Depth 4 -Compress))) `
        -TimeoutSec 5
    if ([string]::IsNullOrWhiteSpace([string]$japaneseReply.text)) {
        throw 'Bundled Japanese Companion request returned no text.'
    }
    if ([string]$japaneseReply.text -notmatch '[\u3040-\u30ff]') {
        throw 'Bundled Japanese Companion fallback did not use Japanese.'
    }

    $mediaJson = & (Join-Path $bundle 'BepInEx\plugins\media-bridge\LilithMediaBridge.exe') --once
    $media = $mediaJson | ConvertFrom-Json
    if ($null -eq $media.available) { throw 'Bundled media bridge returned invalid JSON.' }

    [pscustomobject]@{
        CompanionOk = [bool]$health.ok
        Protocol = [int]$health.protocol_version
        Character = [string]$health.character_id
        MockMode = [bool]$health.mock_mode
        MediaAvailable = [bool]$media.available
        NodeVersion = (& (Join-Path $bundle 'BepInEx\plugins\companion\node.exe') --version)
        UnityBaseLibraries = '2021.3.45'
    } | Format-List
} finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $process.WaitForExit()
    }
    $env:LILITH_AI_DATA_DIR = $oldData
    $env:LILITH_CHARACTER_FILE = $oldCharacter
    $env:LILITH_LORE_FILE = $oldLore
    $env:APPDATA = $oldAppData
    $env:LILITH_CONFIGURE_STARTUP_SMOKE = $oldConfigureSmoke
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
