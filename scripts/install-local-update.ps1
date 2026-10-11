param(
    [Parameter(Mandatory = $true)][string]$PackageZip,
    [Parameter(Mandatory = $true)][string]$ExpectedSha256,
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$DataDir = (Join-Path $env:APPDATA 'LilithAI'),
    [switch]$AutomaticDns
)

$ErrorActionPreference = 'Stop'
$GameDir = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
$DataDir = [IO.Path]::GetFullPath($DataDir).TrimEnd('\')
$PackageZip = (Resolve-Path -LiteralPath $PackageZip).Path
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'Lilith.exe'))) { throw 'Game executable not found.' }
if ((Get-FileHash -LiteralPath $PackageZip -Algorithm SHA256).Hash -ne $ExpectedSha256) { throw 'Package SHA-256 mismatch.' }
$jobRoot = Split-Path -Parent $PackageZip
$statusPath = Join-Path $jobRoot 'install-status.json'
$stage = Join-Path $jobRoot ('extracted-' + [Guid]::NewGuid().ToString('N'))
$backupRoot = Join-Path $DataDir ('backups\before-v0.3.4-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))

function Write-InstallStatus([string]$State, [string]$Reason = '') {
    $status = [ordered]@{ state = $State; version = '0.3.4'; updated_at = [DateTime]::UtcNow.ToString('o'); backup_path = $backupRoot; reason = $Reason }
    [IO.File]::WriteAllText($statusPath, ($status | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
}

function Resolve-InstallPath([string]$Root, [string]$Relative) {
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $target = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package path escaped the install root.' }
    return $target
}

function Get-GameProcesses {
    @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($GameDir + '\', [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
}

$changed = New-Object 'Collections.Generic.List[object]'
$configPath = Join-Path $DataDir 'config.json'
$configChanged = $false
try {
    Expand-Archive -LiteralPath $PackageZip -DestinationPath $stage
    $release = Get-Content -LiteralPath (Join-Path $stage 'RELEASE-MANIFEST.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($release.mod_version -ne '0.3.4') { throw 'Unexpected package version.' }
    $files = @(Get-ChildItem -LiteralPath $stage -File -Recurse)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        [void](Resolve-InstallPath $GameDir $relative)
        if ($relative -match '(^|[\\/])(sessions|players|interop|cache)([\\/]|$)' -or $file.Name -in @('Lilith.exe', 'config.json', 'runtime.json')) {
            throw 'Package contains game or player runtime data.'
        }
    }
    Write-InstallStatus 'waiting_for_game_exit'
    while (@(Get-GameProcesses).Count -gt 0) { Start-Sleep -Seconds 2 }
    Write-InstallStatus 'backing_up'
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    # Backup player data separately; it is never copied into the public package.
    foreach ($name in @('config.json', 'sessions', 'players')) {
        $source = Join-Path $DataDir $name
        if (Test-Path -LiteralPath $source) {
            $dataBackup = Join-Path $backupRoot 'user-data'
            New-Item -ItemType Directory -Path $dataBackup -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $dataBackup -Recurse -Force
        }
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        $target = Resolve-InstallPath $GameDir $relative
        $backup = Resolve-InstallPath (Join-Path $backupRoot 'game-files') $relative
        $existed = Test-Path -LiteralPath $target
        if ($existed) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $backup -Force
        }
        $changed.Add([pscustomobject]@{ source = $file.FullName; target = $target; backup = $backup; existed = $existed; installed = $false })
    }
    while (@(Get-GameProcesses).Count -gt 0) { Write-InstallStatus 'waiting_for_game_exit'; Start-Sleep -Seconds 2 }
    Write-InstallStatus 'installing'
    foreach ($item in $changed) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $item.target) -Force | Out-Null
        $item.installed = $true
        Copy-Item -LiteralPath $item.source -Destination $item.target -Force
        if ((Get-FileHash -LiteralPath $item.source).Hash -ne (Get-FileHash -LiteralPath $item.target).Hash) { throw 'Installed file hash mismatch.' }
    }
    if ($AutomaticDns -and (Test-Path -LiteralPath $configPath)) {
        $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -eq $config.network) { $config | Add-Member -NotePropertyName network -NotePropertyValue ([pscustomobject]@{}) -Force }
        $config.network | Add-Member -NotePropertyName provider_dns_fallback_enabled -NotePropertyValue $true -Force
        $config.network | Add-Member -NotePropertyName provider_dns_servers -NotePropertyValue @() -Force
        $temporaryConfig = Join-Path $DataDir ('config.update-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        [IO.File]::WriteAllText($temporaryConfig, (($config | ConvertTo-Json -Depth 64) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temporaryConfig -Destination $configPath -Force
        $configChanged = $true
    }
    Write-InstallStatus 'complete'
} catch {
    # Restore the prior files if the replacement could not complete.
    foreach ($item in $changed) {
        if (-not $item.installed) { continue }
        if ($item.existed) { Copy-Item -LiteralPath $item.backup -Destination $item.target -Force -ErrorAction SilentlyContinue }
        elseif (Test-Path -LiteralPath $item.target) { Remove-Item -LiteralPath $item.target -Force -ErrorAction SilentlyContinue }
    }
    if ($configChanged) { Copy-Item -LiteralPath (Join-Path $backupRoot 'user-data\config.json') -Destination $configPath -Force }
    Write-InstallStatus 'failed' 'INSTALL_FAILED'
    throw
}
