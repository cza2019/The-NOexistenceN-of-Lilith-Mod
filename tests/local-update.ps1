param([Parameter(Mandatory = $true)][string]$PackageZip)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $env:TEMP ('lilith-update-test-' + [Guid]::NewGuid().ToString('N'))
$game = Join-Path $testRoot 'game'
$data = Join-Path $testRoot 'data'
$job = Join-Path $testRoot 'job'
try {
    foreach ($folder in @($game, $data, $job, (Join-Path $game 'BepInEx\plugins'), (Join-Path $data 'sessions'), (Join-Path $data 'players'))) {
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
    }
    [IO.File]::WriteAllText((Join-Path $game 'Lilith.exe'), 'native-game-marker')
    [IO.File]::WriteAllText((Join-Path $game 'BepInEx\plugins\LilithMod.dll'), 'old-plugin-marker')
    [IO.File]::WriteAllText((Join-Path $game 'BepInEx\plugins\another-mod.dll'), 'other-mod-marker')
    [IO.File]::WriteAllText((Join-Path $data 'sessions\history.json'), 'private-history-marker')
    [IO.File]::WriteAllText((Join-Path $data 'players\memory.json'), 'private-memory-marker')
    $config = @{ provider = @{ model = 'saved-model'; api_key = 'private-key-marker' }; network = @{ provider_dns_servers = @('223.5.5.5'); proxy_url = '' }; extra = @{ preserve = 'saved-setting' } }
    [IO.File]::WriteAllText((Join-Path $data 'config.json'), ($config | ConvertTo-Json -Depth 10))
    $copy = Join-Path $job 'update.zip'
    Copy-Item -LiteralPath $PackageZip -Destination $copy
    $hash = (Get-FileHash -LiteralPath $copy).Hash
    & (Join-Path $repo 'scripts\install-local-update.ps1') -PackageZip $copy -ExpectedSha256 $hash -GameDir $game -DataDir $data -AutomaticDns
    $status = Get-Content -LiteralPath (Join-Path $job 'install-status.json') -Raw | ConvertFrom-Json
    if ($status.state -ne 'complete') { throw 'Local update did not complete.' }
    $saved = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json
    if ($saved.provider.api_key -ne 'private-key-marker' -or $saved.provider.model -ne 'saved-model' -or $saved.extra.preserve -ne 'saved-setting' -or
        -not $saved.network.provider_dns_fallback_enabled -or @($saved.network.provider_dns_servers).Count -ne 0) { throw 'Configuration was not preserved/migrated correctly.' }
    foreach ($entry in @(
        @((Join-Path $game 'Lilith.exe'), 'native-game-marker'),
        @((Join-Path $game 'BepInEx\plugins\another-mod.dll'), 'other-mod-marker'),
        @((Join-Path $data 'sessions\history.json'), 'private-history-marker'),
        @((Join-Path $data 'players\memory.json'), 'private-memory-marker'),
        @((Join-Path $status.backup_path 'game-files\BepInEx\plugins\LilithMod.dll'), 'old-plugin-marker')
    )) {
        if ([IO.File]::ReadAllText($entry[0]) -ne $entry[1]) { throw 'An unrelated file or backup changed.' }
    }
    $before = Get-Content -LiteralPath (Join-Path $status.backup_path 'user-data\config.json') -Raw | ConvertFrom-Json
    if ($before.provider.api_key -ne 'private-key-marker' -or @($before.network.provider_dns_servers).Count -ne 1) { throw 'Previous settings were not backed up.' }
    $installed = Get-Content -LiteralPath (Join-Path $game 'BepInEx\plugins\companion\release-info.json') -Raw | ConvertFrom-Json
    if ($installed.version -ne '0.3.4') { throw 'Incorrect installed version.' }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
Write-Output 'Full package local update, backups and player-data preservation passed.'
