$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$configureScript = Join-Path $repositoryRoot 'companion\configure.ps1'
$exampleConfig = Join-Path $repositoryRoot 'companion\config.example.json'
$previousSmokeValue = $env:LILITH_CONFIGURE_STARTUP_SMOKE
$previousAppData = $env:APPDATA
$temporaryAppData = Join-Path $env:TEMP ('lilith-configure-smoke-' + [Guid]::NewGuid().ToString('N'))

try {
    $dataRoot = Join-Path $temporaryAppData 'LilithAI'
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    $config = Get-Content -LiteralPath $exampleConfig -Raw -Encoding UTF8 | ConvertFrom-Json
    $gemini = [pscustomobject]@{
        kind = 'gemini'
        base_url = 'https://generativelanguage.googleapis.com/v1beta'
        api_key = 'startup-smoke-placeholder'
        model = 'gemini-3.6-flash'
        wire_api = 'gemini_interactions'
        thinking_mode = 'default'
        reasoning_effort = 'low'
        search_tool = 'web_search'
        timeout_ms = 30000
        temperature = 0.75
        max_tokens = 500
    }
    $config.active_provider = 'gemini'
    $config.provider = $gemini
    $config.PSObject.Properties.Remove('provider_profiles')
    $config.PSObject.Properties.Remove('network')
    $config.web_search.enabled = $true
    $config.web_search.backend = 'provider'
    [IO.File]::WriteAllText(
        (Join-Path $dataRoot 'config.json'),
        (($config | ConvertTo-Json -Depth 10) + [Environment]::NewLine),
        (New-Object Text.UTF8Encoding($false)))
    $env:APPDATA = $temporaryAppData
    $env:LILITH_CONFIGURE_STARTUP_SMOKE = '1'
    & powershell.exe `
        -NoProfile `
        -STA `
        -ExecutionPolicy Bypass `
        -File $configureScript
    if ($LASTEXITCODE -ne 0) {
        throw "configure.ps1 startup smoke test failed with exit code $LASTEXITCODE."
    }
} finally {
    $env:LILITH_CONFIGURE_STARTUP_SMOKE = $previousSmokeValue
    $env:APPDATA = $previousAppData
    Remove-Item -LiteralPath $temporaryAppData -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'Configure startup smoke test passed.'
