$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$configurePath = Join-Path $repo 'companion\configure.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $configurePath,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw ($parseErrors | ForEach-Object Message | Out-String)
}

$requiredFunctions = @(
    'Get-TextElementCount',
    'Get-PlayerMemoryPath',
    'Merge-PlayerMemoryFacts',
    'Assert-PlayerMemoryLimits'
)
$definitions = $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true)
foreach ($name in $requiredFunctions) {
    $definition = $definitions | Where-Object Name -eq $name | Select-Object -First 1
    if ($null -eq $definition) { throw "Missing function: $name" }
    Invoke-Expression $definition.Extent.Text
}

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) {
        throw "$Message Expected=[$Expected] Actual=[$Actual]"
    }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('lilith-player-name-test-' + [Guid]::NewGuid().ToString('N'))
$playersRoot = Join-Path $testRoot 'players'
$memoryMaxItems = 24
$memoryMaxChars = 1600
try {
    New-Item -ItemType Directory -Path $playersRoot -Force | Out-Null

    $longName = 'abcdefghijklmnop-not-truncated-by-settings'
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $expectedHash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($longName))
    } finally {
        $sha.Dispose()
    }
    $expectedFile = (($expectedHash | ForEach-Object { $_.ToString('x2') }) -join '') + '.json'
    Assert-Equal $expectedFile ([IO.Path]::GetFileName((Get-PlayerMemoryPath $longName))) 'Memory path must use the exact game-applied name.'

    $target = @(
        [pscustomobject]@{ key = 'target-only'; value = 'keep'; updated_at = '2026-01-01T00:00:00Z' },
        [pscustomobject]@{ key = 'hobby'; value = 'old'; updated_at = '2026-01-01T00:00:00Z' }
    )
    $current = @(
        [pscustomobject]@{ key = 'hobby'; value = 'current-editor-value'; updated_at = '2026-07-23T00:00:00Z' },
        [pscustomobject]@{ key = 'current-only'; value = 'migrate'; updated_at = '2026-07-23T00:00:00Z' }
    )
    $merged = @(Merge-PlayerMemoryFacts -TargetFacts $target -CurrentFacts $current)
    Assert-Equal 3 $merged.Count 'Merge must retain target-only and current-only items.'
    Assert-Equal 'current-editor-value' (($merged | Where-Object key -eq 'hobby').value) 'Current editor value must win conflicts.'
    Assert-PlayerMemoryLimits $merged

    $memoryMaxItems = 2
    $limitRejected = $false
    try {
        Assert-PlayerMemoryLimits $merged
    } catch {
        $limitRejected = $true
    }
    Assert-Equal $true $limitRejected 'An over-limit merge must be rejected instead of truncated.'

    Write-Host 'Player-name settings tests passed.'
} finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
