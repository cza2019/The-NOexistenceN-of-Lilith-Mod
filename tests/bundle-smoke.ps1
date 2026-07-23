param(
    [Parameter(Mandatory = $true)]
    [string]$BundleDirectory
)

$ErrorActionPreference = 'Stop'
$bundle = (Resolve-Path -LiteralPath $BundleDirectory).Path
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

$oldData = $env:LILITH_AI_DATA_DIR
$oldCharacter = $env:LILITH_CHARACTER_FILE
$oldLore = $env:LILITH_LORE_FILE
$process = $null
try {
    $env:LILITH_AI_DATA_DIR = $temp
    $env:LILITH_CHARACTER_FILE = Join-Path $bundle 'BepInEx\plugins\character\lilith.json'
    $env:LILITH_LORE_FILE = Join-Path $bundle 'BepInEx\plugins\character\lore\index.json'
    $process = Start-Process `
        -FilePath (Join-Path $bundle 'BepInEx\plugins\companion\node.exe') `
        -ArgumentList (Join-Path $bundle 'BepInEx\plugins\companion\src\server.js') `
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
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
