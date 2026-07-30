$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$configureScript = Join-Path $repositoryRoot 'companion\configure.ps1'
$previousSmokeValue = $env:LILITH_CONFIGURE_STARTUP_SMOKE

try {
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
}

Write-Output 'Configure startup smoke test passed.'
