$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repo 'companion'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $runtimeRoot 'configure.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
foreach ($name in @('Invoke-LilithProviderTest', 'New-ProviderProfile', 'Merge-ProviderProfile', 'Set-JsonProperty')) {
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    Invoke-Expression $definition.Extent.Text
}
$defaults = @{ deepseek = 'deepseek-flash'; volcengine = 'deepseek-v4-1-flash-260910'; kimi = 'kimi-k3'; qwen = 'qwen3.5-flash'; glm = 'glm-4.7-flash'; gemini = 'gemini-3.6-flash' }
foreach ($kind in $defaults.Keys) {
    $profile = New-ProviderProfile $kind
    if ($profile.model -ne $defaults[$kind]) { throw "Wrong default for $kind" }
    $profile = Merge-ProviderProfile $profile ([pscustomobject]@{ model = 'saved-handwritten-model'; api_key = 'saved-private-key' })
    if ($profile.model -ne 'saved-handwritten-model' -or $profile.api_key -ne 'saved-private-key') { throw 'Saved provider settings were overwritten.' }
}
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('lilith-provider-settings-' + [Guid]::NewGuid().ToString('N'))
$oldNode = $env:LILITH_NODE_PATH
$fixture = $null
try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $env:LILITH_NODE_PATH = (Get-Command node).Source
    $portFile = Join-Path $tempRoot 'port.txt'
    $fixture = Start-Process -FilePath $env:LILITH_NODE_PATH -ArgumentList @('"' + (Join-Path $PSScriptRoot 'settings-provider-fixture.js') + '"', '"' + $portFile + '"') -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $portFile)) {
        if ([DateTime]::UtcNow -gt $deadline -or $fixture.HasExited) { throw 'Fixture did not start.' }
        Start-Sleep -Milliseconds 100
    }
    $port = Get-Content -LiteralPath $portFile -Raw
    $inputData = @{
        target = "http://127.0.0.1:$port/test"
        payload = @{ content = [string][char]0x4F60 + [char]0x597D }
        headers = @{ Authorization = 'Bearer settings-private-key' }
        network = @{ provider_dns_fallback_enabled = $true; provider_dns_servers = @(); proxy_url = '' }
        timeout_ms = 3000
    }
    $result = Invoke-LilithProviderTest $inputData $runtimeRoot
    if ($result.content -ne [string][char]0x597D -or $result.calls -ne 2) { throw 'Settings test did not share retry and UTF-8 behavior.' }
    $inputData.target = "http://127.0.0.1:$port/unauthorized"
    $failed = $false
    try { Invoke-LilithProviderTest $inputData $runtimeRoot } catch {
        $failed = $true
        if ($_.Exception.Message -notmatch 'PROVIDER_HTTP \(HTTP 401\)' -or $_.Exception.Message -match 'private-') { throw 'Connection test error was not sanitized.' }
    }
    if (-not $failed) { throw 'Unauthorized connection test unexpectedly succeeded.' }
} finally {
    if ($null -ne $fixture -and -not $fixture.HasExited) { Stop-Process -Id $fixture.Id -Force }
    $env:LILITH_NODE_PATH = $oldNode
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
Write-Output 'Provider settings, saved models, shared Node requests and UTF-8 tests passed.'
