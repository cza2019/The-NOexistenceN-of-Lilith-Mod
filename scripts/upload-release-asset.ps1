param(
    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [string]$Proxy = ''
)

$ErrorActionPreference = 'Stop'
$resolvedFile = (Resolve-Path -LiteralPath $FilePath).Path
$assetName = [IO.Path]::GetFileName($resolvedFile)
$tempDirectory = Join-Path $env:TEMP ('lilith-release-upload-' + [Guid]::NewGuid().ToString('N'))
$uploadFile = Join-Path $tempDirectory $assetName
$oldHttpProxy = $env:HTTP_PROXY
$oldHttpsProxy = $env:HTTPS_PROXY

try {
    New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
    Copy-Item -LiteralPath $resolvedFile -Destination $uploadFile -Force
    $sourceHash = (Get-FileHash -LiteralPath $resolvedFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $uploadHash = (Get-FileHash -LiteralPath $uploadFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sourceHash -ne $uploadHash) {
        throw 'Temporary upload copy failed its SHA-256 check.'
    }

    if (-not [string]::IsNullOrWhiteSpace($Proxy)) {
        $env:HTTP_PROXY = $Proxy
        $env:HTTPS_PROXY = $Proxy
    }

    $releaseJson = & gh api "repos/$Repository/releases" --paginate
    if ($LASTEXITCODE -ne 0) { throw 'Could not list GitHub releases.' }
    $release = ($releaseJson | ConvertFrom-Json) |
        Where-Object { $_.tag_name -eq $Tag } |
        Select-Object -First 1
    if ($null -eq $release) { throw "Release not found: $Tag" }

    $existing = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    if ($null -ne $existing) {
        & gh api --method DELETE "repos/$Repository/releases/assets/$($existing.id)"
        if ($LASTEXITCODE -ne 0) { throw "Could not replace existing asset: $assetName" }
    }

    $token = (& gh auth token).Trim()
    if ([string]::IsNullOrWhiteSpace($token)) { throw 'GitHub CLI did not return an authentication token.' }

    $uploadUrl = "https://uploads.github.com/repos/$Repository/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($assetName))"
    $curlFile = $uploadFile.Replace('\', '/')
    $curlConfig = @(
        $(if (-not [string]::IsNullOrWhiteSpace($Proxy)) { "proxy = `"$Proxy`"" }),
        'http1.1',
        'retry = 3',
        'retry-all-errors',
        'connect-timeout = 30',
        'max-time = 1800',
        'header = "Accept: application/vnd.github+json"',
        'header = "X-GitHub-Api-Version: 2022-11-28"',
        "header = `"Authorization: Bearer $token`"",
        'header = "Content-Type: application/zip"',
        "data-binary = `"@$curlFile`"",
        "url = `"$uploadUrl`"",
        'request = "POST"',
        'silent',
        'show-error',
        'fail-with-body'
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'curl.exe'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = '--config -'
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw 'Could not start curl.exe.' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.Write(($curlConfig -join "`n"))
    $process.StandardInput.Close()
    $process.WaitForExit()
    $stdout = $stdoutTask.Result
    $stderr = $stderrTask.Result
    if ($process.ExitCode -ne 0) {
        throw "GitHub asset upload failed (curl $($process.ExitCode)): $stderr"
    }

    $asset = $stdout | ConvertFrom-Json
    if ([int64]$asset.size -ne (Get-Item -LiteralPath $resolvedFile).Length) {
        throw "GitHub reported an unexpected asset size: $($asset.size)"
    }

    [pscustomobject]@{
        ReleaseId = $release.id
        AssetId = $asset.id
        Name = $asset.name
        Size = $asset.size
        State = $asset.state
        Digest = $asset.digest
        LocalSHA256 = $sourceHash
    }
}
finally {
    $token = $null
    $curlConfig = $null
    $env:HTTP_PROXY = $oldHttpProxy
    $env:HTTPS_PROXY = $oldHttpsProxy
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
