$ErrorActionPreference = 'Stop'

$root = Join-Path $env:APPDATA 'LilithAI'
$statePath = Join-Path $root 'game-state.json'
$requestPath = Join-Path $root 'player-name-request.json'
$resultPath = Join-Path $root 'player-name-result.json'
if (-not (Test-Path -LiteralPath $statePath)) { throw 'Game state is unavailable.' }
$state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not [bool]$state.running) { throw 'Game is not running.' }

function Invoke-BridgeRequest([string] $Operation, [string] $RequestedName) {
    $id = [Guid]::NewGuid().ToString('N')
    $request = [pscustomobject]@{
        schema_version = 1
        request_id = $id
        session_id = [string]$state.session_id
        operation = $Operation
        expected_name = [string]$state.player_name
        requested_name = $RequestedName
        created_at = [DateTime]::UtcNow.ToString('o')
    }
    Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
    $temporaryPath = $requestPath + '.' + $id + '.tmp'
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            ($request | ConvertTo-Json -Depth 5),
            (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $requestPath) {
            [IO.File]::Replace($temporaryPath, $requestPath, $null)
        } else {
            [IO.File]::Move($temporaryPath, $requestPath)
        }

        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 100
            if (-not (Test-Path -LiteralPath $resultPath)) { continue }
            $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ([string]$result.request_id -eq $id) { return $result }
        }
        throw "No bridge result for $Operation."
    } finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $requestPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
    }
}

$shortName = ([string][char]0x89C9) + ([string][char]0x7687)
$validation = Invoke-BridgeRequest 'validate' $shortName
$invalid = Invoke-BridgeRequest 'validate' ''
$noOp = Invoke-BridgeRequest 'apply' ([string]$state.player_name)
$currentState = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json

if (-not [bool]$validation.success -or [string]$validation.applied_name -ne $shortName) {
    throw 'Native player-name validation failed.'
}
if ([bool]$invalid.success) {
    throw 'An empty player name was unexpectedly accepted.'
}
if (-not [bool]$noOp.success -or [bool]$noOp.changed) {
    throw 'No-op player-name apply failed.'
}
if ([string]$currentState.player_name -ne [string]$state.player_name) {
    throw 'The no-op bridge test unexpectedly changed the player name.'
}

[pscustomobject]@{
    ValidateSuccess = [bool]$validation.success
    ValidatedName = [string]$validation.applied_name
    EmptyNameRejected = -not [bool]$invalid.success
    NoOpSuccess = [bool]$noOp.success
    NoOpChanged = [bool]$noOp.changed
    CurrentName = [string]$currentState.player_name
} | Format-List
