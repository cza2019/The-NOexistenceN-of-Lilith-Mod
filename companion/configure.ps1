$ErrorActionPreference = 'Stop'

# Steam tracks processes spawned by a running game. Re-parent the long-lived
# settings window through WMI so keeping it open does not keep the game marked
# as running after Lilith exits. A WMI-created process does not inherit the
# Steam game environment, so this block runs only once.
if ($env:SteamAppId -or $env:SteamGameId) {
    $quotedScript = '"' + $PSCommandPath.Replace('"', '\"') + '"'
    $commandLine = "powershell.exe -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File $quotedScript"
    try {
        $created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
            CommandLine = $commandLine
            CurrentDirectory = $PSScriptRoot
        }
        if ([int]$created.ReturnValue -eq 0) {
            exit 0
        }
    } catch {
        # Fall back to the current process if WMI is unavailable.
    }
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms -WarningAction SilentlyContinue -TypeDefinition @'
using System.Windows.Forms;

public sealed class LilithComboBox : ComboBox
{
    private const int WM_MOUSEWHEEL = 0x020A;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WM_MOUSEWHEEL)
        {
            return;
        }
        base.WndProc(ref message);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // Intentionally ignore wheel input so hovering a setting cannot
        // silently change its selected value.
    }
}
'@
[System.Windows.Forms.Application]::EnableVisualStyles()

$dataRoot = Join-Path $env:APPDATA 'LilithAI'
$configPath = Join-Path $dataRoot 'config.json'
$uiPath = Join-Path $dataRoot 'ui.json'
$gameStatePath = Join-Path $dataRoot 'game-state.json'
$playerNameRequestPath = Join-Path $dataRoot 'player-name-request.json'
$playerNameResultPath = Join-Path $dataRoot 'player-name-result.json'
$examplePath = Join-Path $PSScriptRoot 'config.example.json'
$releaseInfoPath = Join-Path $PSScriptRoot 'release-info.json'
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null

$releaseInfo = [pscustomobject]@{
    version = '0.3.4'
    github_repository = ''
}
if (Test-Path -LiteralPath $releaseInfoPath) {
    try {
        $loadedReleaseInfo = Get-Content -LiteralPath $releaseInfoPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace([string]$loadedReleaseInfo.version)) {
            $releaseInfo.version = [string]$loadedReleaseInfo.version
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$loadedReleaseInfo.github_repository)) {
            $releaseInfo.github_repository = [string]$loadedReleaseInfo.github_repository
        }
    } catch {
        # Keep the bundled fallback values if release metadata is malformed.
    }
}

$config = if (Test-Path -LiteralPath $configPath) {
    Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    Get-Content -LiteralPath $examplePath -Raw -Encoding UTF8 | ConvertFrom-Json
}
$musicPlayerValues = @('qqmusic', 'netease', 'spotify', 'applemusic', 'amazonmusic')
$defaultMusicSources = @(
    'cloudmusic', 'netease', 'qqmusic', 'spotify', 'music.ui', 'applemusic',
    'appleinc.applemusicwin', 'amazonmusic', 'amazon music', 'amazonmobilellc.amazonmusic'
)
$uiPreferences = if (Test-Path -LiteralPath $uiPath) {
    Get-Content -LiteralPath $uiPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    [pscustomobject]@{ HasPosition = $false; X = 0.5; Y = 0.08; Scale = 1.0 }
}

function Set-JsonProperty {
    param(
        [Parameter(Mandatory = $true)] [object] $Object,
        [Parameter(Mandatory = $true)] [string] $Name,
        [AllowNull()] [object] $Value
    )
    if ($null -eq $Object.PSObject.Properties[$Name]) {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    } else {
        $Object.$Name = $Value
    }
}

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [object] $Value,
        [int] $Depth = 8
    )
    $temporaryPath = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            (($Value | ConvertTo-Json -Depth $Depth) + [Environment]::NewLine),
            (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $Path) {
            [IO.File]::Replace($temporaryPath, $Path, $null)
        } else {
            [IO.File]::Move($temporaryPath, $Path)
        }
    } finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($null -eq $uiPreferences.PSObject.Properties['EnhancedCrossScreenDrag']) {
    Set-JsonProperty $uiPreferences 'EnhancedCrossScreenDrag' $false
}

if ($null -eq $config.provider) {
    Set-JsonProperty $config 'provider' ([pscustomobject]@{})
}
if ($null -eq $config.ui) {
    Set-JsonProperty $config 'ui' ([pscustomobject]@{ language_mode = 'follow_game' })
} elseif ($null -eq $config.ui.PSObject.Properties['language_mode']) {
    Set-JsonProperty $config.ui 'language_mode' 'follow_game'
}
if ($null -eq $config.web_search) {
    Set-JsonProperty $config 'web_search' ([pscustomobject]@{})
}
if ($null -eq $config.network) {
    Set-JsonProperty $config 'network' ([pscustomobject]@{ proxy_url = '' })
} elseif ($null -eq $config.network.PSObject.Properties['proxy_url']) {
    Set-JsonProperty $config.network 'proxy_url' ''
}
if ($null -eq $config.network.PSObject.Properties['provider_dns_servers']) {
    Set-JsonProperty $config.network 'provider_dns_servers' @()
}
if ($null -eq $config.network.PSObject.Properties['provider_dns_fallback_enabled']) {
    Set-JsonProperty $config.network 'provider_dns_fallback_enabled' $true
}
if ($null -eq $config.conversation) {
    Set-JsonProperty $config 'conversation' ([pscustomobject]@{
        reply_max_chars = 100
        allow_parenthetical_actions = $false
    })
} elseif ($null -eq $config.conversation.PSObject.Properties['allow_parenthetical_actions']) {
    Set-JsonProperty $config.conversation 'allow_parenthetical_actions' $false
}
if ($null -eq $config.music) {
    Set-JsonProperty $config 'music' ([pscustomobject]@{ preferred_player = 'qqmusic' })
} elseif ($null -eq $config.music.PSObject.Properties['preferred_player'] -or
    [string]$config.music.preferred_player -notin $musicPlayerValues) {
    Set-JsonProperty $config.music 'preferred_player' 'qqmusic'
}
if ($null -eq $config.media) {
    Set-JsonProperty $config 'media' ([pscustomobject]@{
        enabled = $true
        bridge_path = ''
        allowed_sources = $defaultMusicSources
    })
} else {
    $configuredSources = @(
        @($config.media.allowed_sources) |
            ForEach-Object { [string]$_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    foreach ($source in $defaultMusicSources) {
        if ($configuredSources -notcontains $source) {
            $configuredSources += $source
        }
    }
    Set-JsonProperty $config.media 'allowed_sources' $configuredSources
}
if ($null -eq $config.local_context) {
    Set-JsonProperty $config 'local_context' ([pscustomobject]@{
        enabled = $true
        time_enabled = $true
        media_enabled = $true
    })
} else {
    if ($null -eq $config.local_context.PSObject.Properties['enabled']) {
        Set-JsonProperty $config.local_context 'enabled' $true
    }
    if ($null -eq $config.local_context.PSObject.Properties['time_enabled']) {
        Set-JsonProperty $config.local_context 'time_enabled' $true
    }
    if ($null -eq $config.local_context.PSObject.Properties['media_enabled']) {
        Set-JsonProperty $config.local_context 'media_enabled' $true
    }
}
if ($null -eq $config.memory) {
    Set-JsonProperty $config 'memory' ([pscustomobject]@{
        max_messages = 24
        max_chars_per_message = 2000
        long_term_max_items = 24
        long_term_max_chars = 1600
    })
} else {
    if ($null -eq $config.memory.PSObject.Properties['long_term_max_items']) {
        Set-JsonProperty $config.memory 'long_term_max_items' 24
    }
    if ($null -eq $config.memory.PSObject.Properties['long_term_max_chars']) {
        Set-JsonProperty $config.memory 'long_term_max_chars' 1600
    }
}

$provider = $config.provider
$webSearch = $config.web_search
$network = $config.network
$conversation = $config.conversation
$music = $config.music
$localContext = $config.local_context
$memory = $config.memory
$uiConfig = $config.ui
$playersRoot = Join-Path $dataRoot 'players'
$memoryMaxItems = [Math]::Max(1, [Math]::Min(50, [int]$memory.long_term_max_items))
$memoryMaxChars = [Math]::Max(200, [Math]::Min(5000, [int]$memory.long_term_max_chars))

$localeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\locales'))
$script:localeCatalogs = @{}
function Normalize-LilithLocale([string] $Value, [string] $Fallback = 'zh-CN') {
    $raw = ([string]$Value).Trim().Replace('_', '-').ToLowerInvariant()
    if ($raw -in @('zh-tw', 'zh-hant', 'zh-hk', 'zh-mo', 'tw', 'tc') -or $raw -like '*traditional*') { return 'zh-TW' }
    if ($raw -in @('zh-cn', 'zh-hans', 'zh-sg', 'cn', 'sc') -or $raw -like '*simplified*') { return 'zh-CN' }
    if ($raw -eq 'ja' -or $raw -eq 'jp' -or $raw -like 'ja-*' -or $raw -like '*japanese*') { return 'ja' }
    if ($raw -eq 'en' -or $raw -like 'en-*' -or $raw -like '*english*') { return 'en' }
    return $Fallback
}
function Get-LocaleCatalog([string] $Locale) {
    $normalized = Normalize-LilithLocale $Locale
    if ($script:localeCatalogs.ContainsKey($normalized)) { return $script:localeCatalogs[$normalized] }
    $catalog = @{}
    try {
        $json = Get-Content -LiteralPath (Join-Path $localeRoot ($normalized + '.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($property in $json.strings.PSObject.Properties) { $catalog[$property.Name] = [string]$property.Value }
    } catch {}
    $script:localeCatalogs[$normalized] = $catalog
    return $catalog
}
function Resolve-LilithLocale {
    $mode = ([string]$uiConfig.language_mode).Trim()
    if ($mode -ne 'follow_game') { return Normalize-LilithLocale $mode }
    try {
        if (Test-Path -LiteralPath $gameStatePath) {
            $state = Get-Content -LiteralPath $gameStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
            if (-not [string]::IsNullOrWhiteSpace([string]$state.game_language)) {
                return Normalize-LilithLocale ([string]$state.game_language) (Normalize-LilithLocale ([Globalization.CultureInfo]::CurrentUICulture.Name))
            }
        }
    } catch {}
    return Normalize-LilithLocale ([Globalization.CultureInfo]::CurrentUICulture.Name)
}
$script:currentLocale = Resolve-LilithLocale
function L([string] $Key, [object[]] $Arguments = @()) {
    $catalog = Get-LocaleCatalog $script:currentLocale
    $fallback = Get-LocaleCatalog 'zh-CN'
    $value = if ($catalog.ContainsKey($Key)) { [string]$catalog[$Key] } elseif ($fallback.ContainsKey($Key)) { [string]$fallback[$Key] } else { $Key }
    if ($Arguments.Count -gt 0) { try { return [string]::Format($value, $Arguments) } catch {} }
    return $value
}

function Get-TextElementCount([string] $Value) {
    if ([string]::IsNullOrEmpty($Value)) { return 0 }
    return [Globalization.StringInfo]::ParseCombiningCharacters($Value).Count
}

function Get-PlayerMemoryPath([string] $PlayerName) {
    $normalized = $PlayerName.Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) { return $null }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($normalized)
        $hash = ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
        return Join-Path $playersRoot ($hash + '.json')
    } finally {
        $sha.Dispose()
    }
}

function Get-StoredPlayerNames {
    if (-not (Test-Path -LiteralPath $playersRoot)) { return @() }
    $names = foreach ($file in Get-ChildItem -LiteralPath $playersRoot -Filter '*.json' -File) {
        try {
            $profile = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $name = [string]$profile.player_name
            if (-not [string]::IsNullOrWhiteSpace($name)) { $name.Trim() }
        } catch {
            # Ignore malformed profiles; the editor will never overwrite them implicitly.
        }
    }
    return @($names | Sort-Object -Unique)
}

function Get-RecentPlayerName {
    $logPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\LogOutput.log'))
    if (-not (Test-Path -LiteralPath $logPath)) { return '' }
    try {
        $match = Get-Content -LiteralPath $logPath -Encoding UTF8 |
            Select-String -Pattern 'Player name resolved:\s*(.+)$' |
            Select-Object -Last 1
        if ($null -eq $match) { return '' }
        $name = $match.Matches[0].Groups[1].Value.Trim()
        if ((Get-TextElementCount $name) -le 16) { return $name }
    } catch {
        # A missing or locked diagnostic log should not block manual editing.
    }
    return ''
}

function Get-LilithGameState {
    $fallbackName = Get-RecentPlayerName
    $result = [pscustomobject]@{
        available = $false
        player_name = $fallbackName
        session_id = ''
        process_id = 0
        status = L 'settings.state_not_running'
    }
    if (-not (Test-Path -LiteralPath $gameStatePath)) { return $result }

    try {
        $state = Get-Content -LiteralPath $gameStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $stateName = ([string]$state.player_name).Trim()
        if (-not [string]::IsNullOrWhiteSpace($stateName)) {
            $result.player_name = $stateName
        }
        $result.session_id = [string]$state.session_id
        $result.process_id = [int]$state.process_id

        $updatedAt = [DateTime]::MinValue
        $hasTimestamp = [DateTime]::TryParse(
            [string]$state.updated_at,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$updatedAt)
        $isFresh = $hasTimestamp -and (([DateTime]::UtcNow - $updatedAt.ToUniversalTime()).Duration().TotalSeconds -le 5)
        $processAlive = $false
        if ([int]$state.process_id -gt 0) {
            $processAlive = $null -ne (Get-Process -Id ([int]$state.process_id) -ErrorAction SilentlyContinue)
        }
        $result.available = [bool]$state.running `
            -and $isFresh `
            -and $processAlive `
            -and -not [string]::IsNullOrWhiteSpace($result.session_id) `
            -and -not [string]::IsNullOrWhiteSpace($result.player_name)
        if ($result.available) {
            $result.status = L 'settings.state_running_hint'
        }
    } catch {
        $result.status = L 'settings.state_unreadable'
    }
    return $result
}

function Invoke-PlayerNameBridge {
    param(
        [Parameter(Mandatory = $true)] [ValidateSet('validate', 'apply')] [string] $Operation,
        [Parameter(Mandatory = $true)] [object] $GameState,
        [Parameter(Mandatory = $true)] [string] $ExpectedName,
        [Parameter(Mandatory = $true)] [string] $RequestedName
    )

    $freshState = Get-LilithGameState
    if (-not $freshState.available -or $freshState.session_id -ne $GameState.session_id) {
        throw (L 'settings.bridge_unavailable')
    }

    $requestId = [Guid]::NewGuid().ToString('N')
    $request = [pscustomobject]@{
        schema_version = 1
        request_id = $requestId
        session_id = [string]$freshState.session_id
        operation = $Operation
        expected_name = $ExpectedName
        requested_name = $RequestedName
        created_at = [DateTime]::UtcNow.ToString('o')
    }
    Remove-Item -LiteralPath $playerNameResultPath -Force -ErrorAction SilentlyContinue
    Write-JsonAtomic -Path $playerNameRequestPath -Value $request

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    try {
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 100
            if (-not (Test-Path -LiteralPath $playerNameResultPath)) { continue }
            try {
                $result = Get-Content -LiteralPath $playerNameResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
                if ([string]$result.request_id -ne $requestId) { continue }
                if ([string]$result.session_id -ne [string]$freshState.session_id) {
                    throw (L 'settings.bridge_session_changed')
                }
                if (-not [bool]$result.success) {
                    $message = [string]$result.error
                    if ([string]::IsNullOrWhiteSpace($message)) { $message = L 'settings.bridge_rejected' }
                    throw $message
                }
                return $result
            } catch [System.Management.Automation.RuntimeException] {
                throw
            } catch {
                # The plugin may be replacing the result file; retry until timeout.
            }
        }
        throw (L 'settings.bridge_timeout')
    } finally {
        Remove-Item -LiteralPath $playerNameRequestPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $playerNameResultPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-PlayerMemoryFacts([string] $PlayerName) {
    $path = Get-PlayerMemoryPath $PlayerName
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) { return @() }
    $profile = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    return @($profile.facts)
}

function Merge-PlayerMemoryFacts {
    param(
        [object[]] $TargetFacts,
        [object[]] $CurrentFacts
    )
    $values = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    $order = [Collections.Generic.List[string]]::new()
    foreach ($fact in @($TargetFacts) + @($CurrentFacts)) {
        $key = ([string]$fact.key).Trim()
        $value = ([string]$fact.value).Trim()
        if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($value)) { continue }
        if (-not $values.ContainsKey($key)) { $order.Add($key) }
        $values[$key] = [pscustomobject]@{
            key = $key
            value = $value
            updated_at = $(if ([string]::IsNullOrWhiteSpace([string]$fact.updated_at)) {
                [DateTime]::UtcNow.ToString('o')
            } else {
                [string]$fact.updated_at
            })
        }
    }
    return @($order | ForEach-Object { $values[$_] })
}

function Assert-PlayerMemoryLimits([object[]] $Facts) {
    if (@($Facts).Count -gt $memoryMaxItems) { throw (L 'settings.memory_limit_items' @($memoryMaxItems)) }
    $totalChars = 0
    foreach ($fact in @($Facts)) {
        $totalChars += Get-TextElementCount (([string]$fact.key) + ([string]$fact.value))
    }
    if ($totalChars -gt $memoryMaxChars) {
        throw (L 'settings.memory_limit_chars' @($memoryMaxChars))
    }
}
$existingBackend = [string]$webSearch.backend
$existingSearchKey = [string]$webSearch.api_key
$existingSearchBase = [string]$webSearch.base_url
$providerKind = [string]$config.active_provider
if ($providerKind -notin @('custom', 'volcengine', 'deepseek', 'kimi', 'qwen', 'glm', 'gemini')) {
    $providerKind = [string]$provider.kind
}
if ($providerKind -notin @('custom', 'volcengine', 'deepseek', 'kimi', 'qwen', 'glm', 'gemini')) {
    $providerKind = if ([string]$provider.base_url -match 'ark\.cn-beijing\.volces\.com') {
        'volcengine'
    } elseif ([string]$provider.base_url -match 'api\.deepseek\.com') {
        'deepseek'
    } elseif ([string]$provider.base_url -match 'api\.moonshot\.(cn|ai)') {
        'kimi'
    } elseif ([string]$provider.base_url -match '(dashscope|maas)\.aliyuncs\.com') {
        'qwen'
    } elseif ([string]$provider.base_url -match 'open\.bigmodel\.cn') {
        'glm'
    } elseif ([string]$provider.base_url -match 'generativelanguage\.googleapis\.com') {
        'gemini'
    } else {
        'custom'
    }
}

function New-ProviderProfile([string] $Kind) {
    $values = switch ($Kind) {
        'volcengine' { @{ base_url = 'https://ark.cn-beijing.volces.com/api/v3'; model = 'deepseek-v4-1-flash-260910'; wire_api = 'responses'; thinking_mode = 'disabled' } }
        'deepseek' { @{ base_url = 'https://api.deepseek.com'; model = 'deepseek-flash'; wire_api = 'chat_completions'; thinking_mode = 'disabled' } }
        'kimi' { @{ base_url = 'https://api.moonshot.cn/v1'; model = 'kimi-k3'; wire_api = 'chat_completions'; thinking_mode = 'disabled' } }
        'qwen' { @{ base_url = 'https://dashscope.aliyuncs.com/compatible-mode/v1'; model = 'qwen3.5-flash'; wire_api = 'chat_completions'; thinking_mode = 'disabled' } }
        'glm' { @{ base_url = 'https://open.bigmodel.cn/api/paas/v4'; model = 'glm-4.7-flash'; wire_api = 'chat_completions'; thinking_mode = 'disabled' } }
        'gemini' { @{ base_url = 'https://generativelanguage.googleapis.com/v1beta'; model = 'gemini-3.6-flash'; wire_api = 'gemini_interactions'; thinking_mode = 'default' } }
        default { @{ base_url = ''; model = ''; wire_api = 'chat_completions'; thinking_mode = 'default' } }
    }
    return [pscustomobject]@{
        kind = $Kind
        base_url = $values.base_url
        api_key = ''
        model = $values.model
        wire_api = $values.wire_api
        thinking_mode = $values.thinking_mode
        reasoning_effort = $(if ($Kind -eq 'gemini') { 'low' } else { '' })
        search_tool = 'web_search'
        timeout_ms = 30000
        temperature = 0.75
        max_tokens = 500
    }
}

function Merge-ProviderProfile([object] $Target, [object] $Source) {
    if ($null -eq $Source) { return $Target }
    foreach ($property in $Source.PSObject.Properties) {
        Set-JsonProperty $Target $property.Name $property.Value
    }
    return $Target
}

$profiles = @{}
$storedProfiles = $config.provider_profiles
$geminiProfileWasStored = ($null -ne $storedProfiles `
    -and $null -ne $storedProfiles.PSObject.Properties['gemini']) `
    -or $providerKind -eq 'gemini'
foreach ($kind in @('custom', 'volcengine', 'deepseek', 'kimi', 'qwen', 'glm', 'gemini')) {
    $profile = New-ProviderProfile $kind
    $stored = if ($null -ne $storedProfiles) { $storedProfiles.PSObject.Properties[$kind].Value } else { $null }
    $profile = Merge-ProviderProfile $profile $stored
    if ($kind -eq $providerKind) {
        $profile = Merge-ProviderProfile $profile $config.provider
        Set-JsonProperty $profile 'kind' $kind
    }
    if ($kind -eq 'volcengine') {
        if ([string]$profile.model -eq 'deepseek-v4-flash') {
            Set-JsonProperty $profile 'model' 'deepseek-v4-flash-ga-260731'
        } elseif ([string]$profile.model -eq 'deepseek-v4-pro') {
            Set-JsonProperty $profile 'model' 'deepseek-v4-pro-ga-260813'
        }
    }
    $profiles[$kind] = $profile
}
$provider = $profiles[$providerKind]
$currentProviderKind = $providerKind

$form = New-Object System.Windows.Forms.Form
$form.Text = L 'settings.title'
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.ClientSize = New-Object System.Drawing.Size(620, 640)
$form.AutoScaleMode = 'Dpi'

$tabs = New-Object System.Windows.Forms.TabControl
$tabs.Location = New-Object System.Drawing.Point(4, 4)
$tabs.Size = New-Object System.Drawing.Size(612, 580)
$apiPage = New-Object System.Windows.Forms.TabPage
$apiPage.Text = L 'settings.tab_api'
$apiPage.AutoScroll = $true
$musicPage = New-Object System.Windows.Forms.TabPage
$musicPage.Text = L 'settings.tab_other'
$musicPage.AutoScroll = $true
$localContextPage = New-Object System.Windows.Forms.TabPage
$localContextPage.Text = L 'settings.tab_local'
$updatePage = New-Object System.Windows.Forms.TabPage
$updatePage.Text = L 'settings.tab_update'
[void]$tabs.TabPages.Add($apiPage)
[void]$tabs.TabPages.Add($musicPage)
[void]$tabs.TabPages.Add($localContextPage)
[void]$tabs.TabPages.Add($updatePage)
$form.Controls.Add($tabs)

$intro = New-Object System.Windows.Forms.Label
$intro.Location = New-Object System.Drawing.Point(20, 16)
$intro.Size = New-Object System.Drawing.Size(550, 32)
$intro.Text = L 'settings.api_intro'
$apiPage.Controls.Add($intro)

function Add-Label([string] $text, [int] $x, [int] $y, [int] $width = 260) {
    $label = New-Object System.Windows.Forms.Label
    $label.Location = New-Object System.Drawing.Point($x, $y)
    $label.Size = New-Object System.Drawing.Size($width, 20)
    $label.Text = $text
    $apiPage.Controls.Add($label)
}

Add-Label (L 'settings.provider') 20 54
$providerPreset = New-Object LilithComboBox
$providerPreset.Location = New-Object System.Drawing.Point(20, 75)
$providerPreset.Size = New-Object System.Drawing.Size(550, 24)
$providerPreset.DropDownStyle = 'DropDownList'
[void]$providerPreset.Items.Add((L 'settings.custom'))
[void]$providerPreset.Items.Add((L 'settings.volcengine'))
[void]$providerPreset.Items.Add((L 'settings.deepseek'))
[void]$providerPreset.Items.Add((L 'settings.kimi'))
[void]$providerPreset.Items.Add((L 'settings.qwen'))
[void]$providerPreset.Items.Add((L 'settings.glm'))
[void]$providerPreset.Items.Add((L 'settings.gemini'))
$providerPreset.SelectedIndex = switch ($providerKind) {
    'volcengine' { 1 }
    'deepseek' { 2 }
    'kimi' { 3 }
    'qwen' { 4 }
    'glm' { 5 }
    'gemini' { 6 }
    default { 0 }
}
$apiPage.Controls.Add($providerPreset)

Add-Label 'Base URL' 20 109
$baseUrl = New-Object System.Windows.Forms.TextBox
$baseUrl.Location = New-Object System.Drawing.Point(20, 130)
$baseUrl.Size = New-Object System.Drawing.Size(550, 24)
$baseUrl.Text = [string]$provider.base_url
$apiPage.Controls.Add($baseUrl)

Add-Label (L 'settings.api_key') 20 164 380
$apiKey = New-Object System.Windows.Forms.TextBox
$apiKey.Location = New-Object System.Drawing.Point(20, 185)
$apiKey.Size = New-Object System.Drawing.Size(435, 24)
$apiKey.UseSystemPasswordChar = $true
$apiPage.Controls.Add($apiKey)

$showKey = New-Object System.Windows.Forms.CheckBox
$showKey.Location = New-Object System.Drawing.Point(464, 186)
$showKey.Size = New-Object System.Drawing.Size(106, 24)
$showKey.Text = L 'settings.show_key'
$showKey.Add_CheckedChanged({ $apiKey.UseSystemPasswordChar = -not $showKey.Checked })
$apiPage.Controls.Add($showKey)

Add-Label (L 'settings.model') 20 219
$model = New-Object LilithComboBox
$model.Location = New-Object System.Drawing.Point(20, 240)
$model.Size = New-Object System.Drawing.Size(550, 24)
$model.DropDownStyle = 'DropDown'
$apiPage.Controls.Add($model)

function Set-ModelChoices {
    param([string[]] $Values, [string] $Selected)
    $model.BeginUpdate()
    $model.Items.Clear()
    foreach ($value in $Values) { [void]$model.Items.Add($value) }
    $model.EndUpdate()
    $model.Text = $Selected
}

switch ($providerKind) {
    'volcengine' {
        Set-ModelChoices @('deepseek-v4-1-flash-260910', 'deepseek-v4-flash-ga-260731', 'deepseek-v4-pro-ga-260813', 'doubao-seed-character-260628', 'doubao-seed-2-1-turbo-260628', 'doubao-seed-2-1-pro-260628', 'doubao-seed-2-0-lite-260428') ([string]$provider.model)
    }
    'deepseek' {
        Set-ModelChoices @('deepseek-flash', 'deepseek-v4-pro') ([string]$provider.model)
    }
    'kimi' {
        Set-ModelChoices @('kimi-k3', 'kimi-k2.6') ([string]$provider.model)
    }
    'qwen' {
        Set-ModelChoices @('qwen3.5-flash', 'qwen3.6-flash', 'qwen3.7-plus') ([string]$provider.model)
    }
    'glm' {
        Set-ModelChoices @('glm-4.7-flash', 'glm-4.7-flashx', 'glm-5.2') ([string]$provider.model)
    }
    'gemini' {
        Set-ModelChoices @('gemini-3.6-flash', 'gemini-3.5-flash', 'gemini-3.5-flash-lite', 'gemini-2.5-pro', 'gemini-2.5-flash', 'gemini-2.5-flash-lite') ([string]$provider.model)
    }
    default {
        Set-ModelChoices @() ([string]$provider.model)
    }
}

Add-Label (L 'settings.wire_api') 20 274 260
$wireApi = New-Object LilithComboBox
$wireApi.Location = New-Object System.Drawing.Point(20, 295)
$wireApi.Size = New-Object System.Drawing.Size(270, 24)
$wireApi.DropDownStyle = 'DropDownList'
[void]$wireApi.Items.Add('chat_completions')
[void]$wireApi.Items.Add('responses')
[void]$wireApi.Items.Add('gemini_interactions')
$selectedWire = [string]$provider.wire_api
if ($selectedWire -notin @('chat_completions', 'responses', 'gemini_interactions')) { $selectedWire = 'chat_completions' }
$wireApi.SelectedItem = $selectedWire
$apiPage.Controls.Add($wireApi)

$reasoningLabel = New-Object System.Windows.Forms.Label
$reasoningLabel.Location = New-Object System.Drawing.Point(310, 274)
$reasoningLabel.Size = New-Object System.Drawing.Size(260, 20)
$apiPage.Controls.Add($reasoningLabel)
$reasoning = New-Object LilithComboBox
$reasoning.Location = New-Object System.Drawing.Point(310, 295)
$reasoning.Size = New-Object System.Drawing.Size(260, 24)
$reasoning.DropDownStyle = 'DropDownList'
$apiPage.Controls.Add($reasoning)

Add-Label (L 'settings.web_search') 20 335 290
$searchBackend = New-Object LilithComboBox
$searchBackend.Location = New-Object System.Drawing.Point(20, 356)
$searchBackend.Size = New-Object System.Drawing.Size(270, 24)
$searchBackend.DropDownStyle = 'DropDownList'
[void]$searchBackend.Items.Add((L 'settings.search_off'))
[void]$searchBackend.Items.Add((L 'settings.search_provider'))
[void]$searchBackend.Items.Add('Brave Search')
[void]$searchBackend.Items.Add('SearXNG')
$selectedSearchIndex = if (-not [bool]$webSearch.enabled) {
    0
} else {
    switch ($existingBackend) {
        'provider' { 1 }
        'brave' { 2 }
        'searxng' { 3 }
        default { 0 }
    }
}
$searchBackend.SelectedIndex = $selectedSearchIndex
$apiPage.Controls.Add($searchBackend)

Add-Label (L 'settings.searxng_url') 310 335 260
$searchBase = New-Object System.Windows.Forms.TextBox
$searchBase.Location = New-Object System.Drawing.Point(310, 356)
$searchBase.Size = New-Object System.Drawing.Size(260, 24)
$searchBase.Text = $existingSearchBase
$apiPage.Controls.Add($searchBase)

Add-Label (L 'settings.search_key') 20 390 380
$searchKey = New-Object System.Windows.Forms.TextBox
$searchKey.Location = New-Object System.Drawing.Point(20, 411)
$searchKey.Size = New-Object System.Drawing.Size(435, 24)
$searchKey.UseSystemPasswordChar = $true
$apiPage.Controls.Add($searchKey)

$showSearchKey = New-Object System.Windows.Forms.CheckBox
$showSearchKey.Location = New-Object System.Drawing.Point(464, 412)
$showSearchKey.Size = New-Object System.Drawing.Size(106, 24)
$showSearchKey.Text = L 'settings.show_key'
$showSearchKey.Add_CheckedChanged({ $searchKey.UseSystemPasswordChar = -not $showSearchKey.Checked })
$apiPage.Controls.Add($showSearchKey)

$replyLimitLabel = New-Object System.Windows.Forms.Label
$replyLimitLabel.Location = New-Object System.Drawing.Point(20, 447)
$replyLimitLabel.Size = New-Object System.Drawing.Size(190, 24)
$replyLimitLabel.Text = L 'settings.reply_limit'
$apiPage.Controls.Add($replyLimitLabel)

$replyLimit = New-Object System.Windows.Forms.NumericUpDown
$replyLimit.Location = New-Object System.Drawing.Point(210, 445)
$replyLimit.Size = New-Object System.Drawing.Size(100, 24)
$replyLimit.Minimum = 20
$replyLimit.Maximum = 500
$configuredReplyLimit = [int]$conversation.reply_max_chars
if ($configuredReplyLimit -lt 20 -or $configuredReplyLimit -gt 500) { $configuredReplyLimit = 100 }
$replyLimit.Value = $configuredReplyLimit
$apiPage.Controls.Add($replyLimit)

$replyLimitHint = New-Object System.Windows.Forms.Label
$replyLimitHint.Location = New-Object System.Drawing.Point(320, 447)
$replyLimitHint.Size = New-Object System.Drawing.Size(250, 24)
$replyLimitHint.Text = L 'settings.reply_limit_hint'
$apiPage.Controls.Add($replyLimitHint)

$hint = New-Object System.Windows.Forms.Label
$hint.Location = New-Object System.Drawing.Point(20, 476)
$hint.Size = New-Object System.Drawing.Size(550, 28)
$apiPage.Controls.Add($hint)

$parentheticalActions = New-Object System.Windows.Forms.CheckBox
$parentheticalActions.Location = New-Object System.Drawing.Point(20, 510)
$parentheticalActions.Size = New-Object System.Drawing.Size(550, 38)
$parentheticalActions.Text = L 'settings.parenthetical'
$parentheticalActions.Checked = [bool]$conversation.allow_parenthetical_actions
$apiPage.Controls.Add($parentheticalActions)

$proxyLabel = New-Object System.Windows.Forms.Label
$proxyLabel.Location = New-Object System.Drawing.Point(20, 556)
$proxyLabel.Size = New-Object System.Drawing.Size(550, 20)
$proxyLabel.Text = L 'settings.proxy_url'
$apiPage.Controls.Add($proxyLabel)

$proxyUrl = New-Object System.Windows.Forms.TextBox
$proxyUrl.Location = New-Object System.Drawing.Point(20, 578)
$proxyUrl.Size = New-Object System.Drawing.Size(550, 24)
$proxyUrl.Text = [string]$network.proxy_url
$apiPage.Controls.Add($proxyUrl)

$proxyHint = New-Object System.Windows.Forms.Label
$proxyHint.Location = New-Object System.Drawing.Point(20, 607)
$proxyHint.Size = New-Object System.Drawing.Size(550, 40)
$proxyHint.Text = L 'settings.proxy_hint'
$apiPage.Controls.Add($proxyHint)

$dnsFallback = New-Object System.Windows.Forms.CheckBox
$dnsFallback.Location = New-Object System.Drawing.Point(20, 651)
$dnsFallback.Size = New-Object System.Drawing.Size(550, 40)
$dnsFallback.Text = L 'settings.dns_fallback'
$dnsFallback.Checked = [bool]$network.provider_dns_fallback_enabled
$apiPage.Controls.Add($dnsFallback)
$dnsFallbackHint = New-Object System.Windows.Forms.Label
$dnsFallbackHint.Location = New-Object System.Drawing.Point(20, 696)
$dnsFallbackHint.Size = New-Object System.Drawing.Size(550, 55)
$dnsFallbackHint.Text = L 'settings.dns_fallback_hint'
$apiPage.Controls.Add($dnsFallbackHint)

$geminiNotice = New-Object System.Windows.Forms.Label
$geminiNotice.Location = New-Object System.Drawing.Point(20, 759)
$geminiNotice.Size = New-Object System.Drawing.Size(550, 66)
$geminiNotice.Text = L 'settings.gemini_notice'
$geminiNotice.ForeColor = [Drawing.Color]::DarkRed
$apiPage.Controls.Add($geminiNotice)
$apiPage.AutoScrollMinSize = New-Object System.Drawing.Size(0, 838)

$musicIntro = New-Object System.Windows.Forms.Label
$languageLabel = New-Object System.Windows.Forms.Label
$languageLabel.Location = New-Object System.Drawing.Point(20, 20)
$languageLabel.Size = New-Object System.Drawing.Size(180, 20)
$languageLabel.Text = L 'settings.language_label'
$musicPage.Controls.Add($languageLabel)

$languageMode = New-Object LilithComboBox
$languageMode.Location = New-Object System.Drawing.Point(204, 17)
$languageMode.Size = New-Object System.Drawing.Size(366, 24)
$languageMode.DropDownStyle = 'DropDownList'
[void]$languageMode.Items.Add((L 'language.follow_game'))
[void]$languageMode.Items.Add((L 'language.zh_CN'))
[void]$languageMode.Items.Add((L 'language.zh_TW'))
[void]$languageMode.Items.Add((L 'language.en'))
[void]$languageMode.Items.Add((L 'language.ja'))
$languageMode.SelectedIndex = switch ([string]$uiConfig.language_mode) {
    'zh-CN' { 1 }
    'zh-TW' { 2 }
    'en' { 3 }
    'ja' { 4 }
    default { 0 }
}
$musicPage.Controls.Add($languageMode)

$languageHint = New-Object System.Windows.Forms.Label
$languageHint.Location = New-Object System.Drawing.Point(20, 46)
$languageHint.Size = New-Object System.Drawing.Size(550, 34)
$languageHint.Text = L 'settings.language_hint'
$musicPage.Controls.Add($languageHint)

$musicIntro.Location = New-Object System.Drawing.Point(20, 88)
$musicIntro.Size = New-Object System.Drawing.Size(550, 44)
$musicIntro.Text = L 'settings.other_intro'
$musicPage.Controls.Add($musicIntro)

$musicPreferenceLabel = New-Object System.Windows.Forms.Label
$musicPreferenceLabel.Location = New-Object System.Drawing.Point(20, 150)
$musicPreferenceLabel.Size = New-Object System.Drawing.Size(300, 20)
$musicPreferenceLabel.Text = L 'settings.preferred_player'
$musicPage.Controls.Add($musicPreferenceLabel)

$musicPreference = New-Object LilithComboBox
$musicPreference.Location = New-Object System.Drawing.Point(20, 174)
$musicPreference.Size = New-Object System.Drawing.Size(550, 24)
$musicPreference.DropDownStyle = 'DropDownList'
foreach ($key in @('menu.qq_music', 'menu.netease_music', 'menu.spotify', 'menu.apple_music', 'menu.amazon_music')) {
    [void]$musicPreference.Items.Add((L $key))
}
$savedMusicPlayer = [string]$music.preferred_player
$musicPreference.SelectedIndex = [Math]::Max(0, [Array]::IndexOf([string[]]$musicPlayerValues, $savedMusicPlayer))
$musicPage.Controls.Add($musicPreference)

$chatScaleLabel = New-Object System.Windows.Forms.Label
$chatScaleLabel.Location = New-Object System.Drawing.Point(20, 226)
$chatScaleLabel.Size = New-Object System.Drawing.Size(300, 20)
$chatScaleLabel.Text = L 'settings.chat_scale'
$musicPage.Controls.Add($chatScaleLabel)

$chatScale = New-Object System.Windows.Forms.NumericUpDown
$chatScale.Location = New-Object System.Drawing.Point(20, 250)
$chatScale.Size = New-Object System.Drawing.Size(120, 24)
$chatScale.Minimum = 50
$chatScale.Maximum = 200
$chatScale.Increment = 5
$savedUiScale = if ($null -ne $uiPreferences.PSObject.Properties['Scale']) {
    [int][Math]::Round(([double]$uiPreferences.Scale) * 100)
} else {
    100
}
$chatScale.Value = [Math]::Max(50, [Math]::Min(200, $savedUiScale))
$musicPage.Controls.Add($chatScale)

$chatScaleHint = New-Object System.Windows.Forms.Label
$chatScaleHint.Location = New-Object System.Drawing.Point(154, 252)
$chatScaleHint.Size = New-Object System.Drawing.Size(416, 36)
$chatScaleHint.Text = L 'settings.chat_scale_hint'
$musicPage.Controls.Add($chatScaleHint)

$enhancedCrossScreenDrag = New-Object System.Windows.Forms.CheckBox
$enhancedCrossScreenDrag.Location = New-Object System.Drawing.Point(20, 288)
$enhancedCrossScreenDrag.Size = New-Object System.Drawing.Size(550, 24)
$enhancedCrossScreenDrag.Text = L 'settings.cross_screen'
$enhancedCrossScreenDrag.Checked = [bool]$uiPreferences.EnhancedCrossScreenDrag
$musicPage.Controls.Add($enhancedCrossScreenDrag)

$enhancedCrossScreenDragHint = New-Object System.Windows.Forms.Label
$enhancedCrossScreenDragHint.Location = New-Object System.Drawing.Point(42, 312)
$enhancedCrossScreenDragHint.Size = New-Object System.Drawing.Size(528, 28)
$enhancedCrossScreenDragHint.Text = L 'settings.cross_screen_hint'
$musicPage.Controls.Add($enhancedCrossScreenDragHint)

function Show-PlayerMemoryEditor {
    $dialog = New-Object System.Windows.Forms.Form
    $dialog.Text = L 'settings.memory_edit'
    $dialog.StartPosition = 'CenterParent'
    $dialog.FormBorderStyle = 'FixedDialog'
    $dialog.MaximizeBox = $false
    $dialog.MinimizeBox = $false
    $dialog.ClientSize = New-Object System.Drawing.Size(600, 548)
    $dialog.AutoScaleMode = 'Dpi'

    $playerLabel = New-Object System.Windows.Forms.Label
    $playerLabel.Location = New-Object System.Drawing.Point(16, 16)
    $playerLabel.Size = New-Object System.Drawing.Size(250, 20)
    $playerLabel.Text = L 'settings.player_name'
    $dialog.Controls.Add($playerLabel)

    $gameState = Get-LilithGameState
    $originalPlayerName = ([string]$gameState.player_name).Trim()

    $playerName = New-Object System.Windows.Forms.TextBox
    $playerName.Location = New-Object System.Drawing.Point(16, 39)
    $playerName.Size = New-Object System.Drawing.Size(440, 24)
    $playerName.Text = $originalPlayerName
    $playerName.ReadOnly = -not [bool]$gameState.available
    $dialog.Controls.Add($playerName)

    $gameStateLabel = New-Object System.Windows.Forms.Label
    $gameStateLabel.Location = New-Object System.Drawing.Point(470, 41)
    $gameStateLabel.Size = New-Object System.Drawing.Size(112, 20)
    $gameStateLabel.Text = $(if ($gameState.available) { L 'settings.game_running' } else { L 'settings.game_stopped' })
    $gameStateLabel.ForeColor = $(if ($gameState.available) { [Drawing.Color]::DarkGreen } else { [Drawing.Color]::DarkRed })
    $dialog.Controls.Add($gameStateLabel)

    $profileHint = New-Object System.Windows.Forms.Label
    $profileHint.Location = New-Object System.Drawing.Point(16, 70)
    $profileHint.Size = New-Object System.Drawing.Size(566, 36)
    $profileHint.Text = [string]$gameState.status
    $dialog.Controls.Add($profileHint)

    $memoryGrid = New-Object System.Windows.Forms.DataGridView
    $memoryGrid.Location = New-Object System.Drawing.Point(16, 108)
    $memoryGrid.Size = New-Object System.Drawing.Size(566, 260)
    $memoryGrid.AllowUserToAddRows = $true
    $memoryGrid.AllowUserToDeleteRows = $true
    $memoryGrid.AllowUserToResizeRows = $false
    $memoryGrid.RowHeadersVisible = $false
    $memoryGrid.SelectionMode = [System.Windows.Forms.DataGridViewSelectionMode]::FullRowSelect
    $memoryGrid.MultiSelect = $true
    $memoryGrid.AutoSizeRowsMode = [System.Windows.Forms.DataGridViewAutoSizeRowsMode]::AllCells
    $memoryGrid.DefaultCellStyle.WrapMode = [System.Windows.Forms.DataGridViewTriState]::True
    [void]$memoryGrid.Columns.Add('MemoryKey', (L 'settings.memory_key'))
    [void]$memoryGrid.Columns.Add('MemoryValue', (L 'settings.memory_value'))
    $memoryGrid.Columns[0].Width = 150
    $memoryGrid.Columns[1].AutoSizeMode = [System.Windows.Forms.DataGridViewAutoSizeColumnMode]::Fill
    $dialog.Controls.Add($memoryGrid)

    $memoryStatus = New-Object System.Windows.Forms.Label
    $memoryStatus.Location = New-Object System.Drawing.Point(16, 374)
    $memoryStatus.Size = New-Object System.Drawing.Size(566, 22)
    $dialog.Controls.Add($memoryStatus)

    $editorRules = New-Object System.Windows.Forms.Label
    $editorRules.Location = New-Object System.Drawing.Point(16, 402)
    $editorRules.Size = New-Object System.Drawing.Size(566, 92)
    $editorRules.Text = L 'settings.memory_editor_rules' @($memoryMaxItems, $memoryMaxChars)
    $dialog.Controls.Add($editorRules)

    $saveMemoryButton = New-Object System.Windows.Forms.Button
    $saveMemoryButton.Location = New-Object System.Drawing.Point(352, 504)
    $saveMemoryButton.Size = New-Object System.Drawing.Size(130, 30)
    $saveMemoryButton.Text = L 'settings.memory_save'
    $dialog.Controls.Add($saveMemoryButton)

    $cancelMemoryButton = New-Object System.Windows.Forms.Button
    $cancelMemoryButton.Location = New-Object System.Drawing.Point(490, 504)
    $cancelMemoryButton.Size = New-Object System.Drawing.Size(92, 30)
    $cancelMemoryButton.Text = L 'common.cancel'
    $cancelMemoryButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $dialog.Controls.Add($cancelMemoryButton)
    $dialog.CancelButton = $cancelMemoryButton

    $refreshMemoryStatus = {
        $count = 0
        $chars = 0
        foreach ($row in $memoryGrid.Rows) {
            if ($row.IsNewRow) { continue }
            $key = [string]$row.Cells[0].Value
            $value = [string]$row.Cells[1].Value
            if ([string]::IsNullOrWhiteSpace($key) -and [string]::IsNullOrWhiteSpace($value)) { continue }
            $count++
            $chars += Get-TextElementCount ($key.Trim() + $value.Trim())
        }
        $memoryStatus.Text = L 'settings.memory_count' @($count, $memoryMaxItems, $chars, $memoryMaxChars)
        $memoryStatus.ForeColor = if ($count -gt $memoryMaxItems -or $chars -gt $memoryMaxChars) {
            [Drawing.Color]::DarkRed
        } else {
            [Drawing.SystemColors]::ControlText
        }
    }.GetNewClosure()

    $loadMemory = {
        $name = $originalPlayerName
        if ([string]::IsNullOrWhiteSpace($name)) {
            & $refreshMemoryStatus
            return
        }
        $memoryGrid.Rows.Clear()
        $path = Get-PlayerMemoryPath $name
        if (Test-Path -LiteralPath $path) {
            try {
                $profile = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
                foreach ($fact in @($profile.facts)) {
                    [void]$memoryGrid.Rows.Add([string]$fact.key, [string]$fact.value)
                }
            } catch {
                [void][System.Windows.Forms.MessageBox]::Show(
                    $dialog,
                    (L 'settings.memory_file_failed' @($_.Exception.Message)),
                    (L 'settings.memory_file_failed_title'),
                    [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Warning)
            }
        }
        & $refreshMemoryStatus
    }.GetNewClosure()

    $memoryGrid.Add_CellValueChanged($refreshMemoryStatus)
    $memoryGrid.Add_RowsAdded($refreshMemoryStatus)
    $memoryGrid.Add_RowsRemoved($refreshMemoryStatus)
    $memoryGrid.Add_KeyDown({
        param($sender, $eventArgs)

        if ($eventArgs.KeyCode -ne [System.Windows.Forms.Keys]::Delete -or
            $memoryGrid.IsCurrentCellInEditMode) {
            return
        }

        $selectedRows = @($memoryGrid.SelectedRows | Where-Object { -not $_.IsNewRow })
        if ($selectedRows.Count -eq 0) { return }

        foreach ($row in @($selectedRows | Sort-Object Index -Descending)) {
            $memoryGrid.Rows.Remove($row)
        }

        $eventArgs.Handled = $true
        $eventArgs.SuppressKeyPress = $true
    }.GetNewClosure())

    $saveMemoryButton.Add_Click({
        $stagePath = ''
        try {
            $requestedName = $playerName.Text.Trim()
            if ([string]::IsNullOrWhiteSpace($requestedName)) { throw (L 'settings.memory_name_required') }
            if ([string]::IsNullOrWhiteSpace($originalPlayerName)) {
                throw (L 'settings.memory_name_unknown')
            }

            $facts = [Collections.Generic.List[object]]::new()
            $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($row in $memoryGrid.Rows) {
                if ($row.IsNewRow) { continue }
                $key = ([string]$row.Cells[0].Value).Trim()
                $value = ([string]$row.Cells[1].Value).Trim()
                if ([string]::IsNullOrWhiteSpace($key) -and [string]::IsNullOrWhiteSpace($value)) { continue }
                if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($value)) {
                    throw (L 'settings.memory_fact_incomplete')
                }
                if ((Get-TextElementCount $key) -gt 24) { throw (L 'settings.memory_key_too_long' @($key)) }
                if ((Get-TextElementCount $value) -gt 100) { throw (L 'settings.memory_value_too_long' @($key)) }
                if (-not $keys.Add($key)) { throw (L 'settings.memory_duplicate' @($key)) }
                if ("$key $value" -match '(?i)密码|密碼|口令|验证码|驗證碼|身份证|身份證|身分證|护照|護照|银行卡|銀行卡|信用卡|支付密码|支付密碼|パスワード|暗証番号|認証コード|確認コード|身分証|本人確認|旅券|クレジットカード|銀行口座|api\s*key|apiキー|access\s*token|secret|password|passport|cvv') {
                    throw (L 'settings.memory_sensitive' @($key))
                }
                $facts.Add([pscustomobject]@{
                    key = $key
                    value = $value
                    updated_at = [DateTime]::UtcNow.ToString('o')
                })
            }
            Assert-PlayerMemoryLimits @($facts)

            $canonicalName = $originalPlayerName
            $nameChanged = -not [string]::Equals(
                $requestedName,
                $originalPlayerName,
                [StringComparison]::Ordinal)
            if ($nameChanged) {
                if (-not [bool]$gameState.available) {
                    throw (L 'settings.memory_game_required')
                }
                $validated = Invoke-PlayerNameBridge `
                    -Operation validate `
                    -GameState $gameState `
                    -ExpectedName $originalPlayerName `
                    -RequestedName $requestedName
                $canonicalName = ([string]$validated.applied_name).Trim()
                if ([string]::IsNullOrWhiteSpace($canonicalName)) {
                    throw (L 'settings.memory_invalid_game_name')
                }
                $nameChanged = -not [string]::Equals(
                    $canonicalName,
                    $originalPlayerName,
                    [StringComparison]::Ordinal)
            }

            New-Item -ItemType Directory -Path $playersRoot -Force | Out-Null
            $oldPath = Get-PlayerMemoryPath $originalPlayerName
            $targetPath = Get-PlayerMemoryPath $canonicalName
            $mergedFacts = @($facts)
            if ($nameChanged -and (Test-Path -LiteralPath $targetPath)) {
                $mergedFacts = @(Merge-PlayerMemoryFacts `
                    -TargetFacts @(Get-PlayerMemoryFacts $canonicalName) `
                    -CurrentFacts @($facts))
            }
            Assert-PlayerMemoryLimits $mergedFacts

            $profile = [pscustomobject]@{
                schema_version = 1
                player_name = $canonicalName
                facts = @($mergedFacts)
            }
            $stagePath = Join-Path $playersRoot ('.player-profile-' + [Guid]::NewGuid().ToString('N') + '.tmp')
            [IO.File]::WriteAllText(
                $stagePath,
                (($profile | ConvertTo-Json -Depth 6) + [Environment]::NewLine),
                (New-Object Text.UTF8Encoding($false)))

            if ($nameChanged) {
                $applied = Invoke-PlayerNameBridge `
                    -Operation apply `
                    -GameState $gameState `
                    -ExpectedName $originalPlayerName `
                    -RequestedName $requestedName
                if (-not [string]::Equals(
                        ([string]$applied.applied_name).Trim(),
                        $canonicalName,
                        [StringComparison]::Ordinal)) {
                    throw (L 'settings.memory_name_mismatch')
                }
            }

            $targetExisted = Test-Path -LiteralPath $targetPath
            $replacementBackup = Join-Path $playersRoot ('.player-profile-backup-' + [Guid]::NewGuid().ToString('N') + '.tmp')
            try {
                if ($targetExisted) {
                    [IO.File]::Replace($stagePath, $targetPath, $replacementBackup)
                } else {
                    [IO.File]::Move($stagePath, $targetPath)
                }
                $stagePath = ''
            } catch {
                if (Test-Path -LiteralPath $replacementBackup) {
                    if (Test-Path -LiteralPath $targetPath) {
                        [IO.File]::Replace($replacementBackup, $targetPath, $null)
                    } else {
                        [IO.File]::Move($replacementBackup, $targetPath)
                    }
                } elseif (-not $targetExisted -and (Test-Path -LiteralPath $targetPath)) {
                    Remove-Item -LiteralPath $targetPath -Force -ErrorAction SilentlyContinue
                }

                if ($nameChanged) {
                    try {
                        $rollbackState = Get-LilithGameState
                        [void](Invoke-PlayerNameBridge `
                            -Operation apply `
                            -GameState $rollbackState `
                            -ExpectedName $canonicalName `
                            -RequestedName $originalPlayerName)
                    } catch {
                        throw (L 'settings.memory_rollback_failed' @([Environment]::NewLine + $_.Exception.Message))
                    }
                }
                throw
            } finally {
                Remove-Item -LiteralPath $replacementBackup -Force -ErrorAction SilentlyContinue
            }

            if ($nameChanged -and -not [string]::Equals(
                    $oldPath,
                    $targetPath,
                    [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $oldPath -Force -ErrorAction SilentlyContinue
            }

            $resultMessage = if ($nameChanged) {
                L 'settings.memory_saved_changed' @($canonicalName, $mergedFacts.Count)
            } else {
                L 'settings.memory_saved' @($mergedFacts.Count)
            }
            [void][System.Windows.Forms.MessageBox]::Show(
                $dialog,
                $resultMessage,
                (L 'settings.memory_saved_title'),
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information)
            $dialog.DialogResult = [System.Windows.Forms.DialogResult]::OK
            $dialog.Close()
        } catch {
            [void][System.Windows.Forms.MessageBox]::Show(
                $dialog,
                $_.Exception.Message,
                (L 'settings.memory_save_failed_title'),
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Warning)
        } finally {
            if (-not [string]::IsNullOrWhiteSpace($stagePath) -and (Test-Path -LiteralPath $stagePath)) {
                Remove-Item -LiteralPath $stagePath -Force -ErrorAction SilentlyContinue
            }
        }
    }.GetNewClosure())

    if (-not [string]::IsNullOrWhiteSpace($originalPlayerName)) {
        & $loadMemory
    } else {
        & $refreshMemoryStatus
    }
    [void]$dialog.ShowDialog($form)
    $dialog.Dispose()
}

$memoryGroup = New-Object System.Windows.Forms.GroupBox
$memoryGroup.Location = New-Object System.Drawing.Point(20, 344)
$memoryGroup.Size = New-Object System.Drawing.Size(550, 258)
$memoryGroup.Text = L 'settings.memory_group'
$musicPage.Controls.Add($memoryGroup)

$memoryIntro = New-Object System.Windows.Forms.Label
$memoryIntro.Location = New-Object System.Drawing.Point(16, 26)
$memoryIntro.Size = New-Object System.Drawing.Size(516, 38)
$memoryIntro.Text = L 'settings.memory_intro'
$memoryGroup.Controls.Add($memoryIntro)

$editMemoryButton = New-Object System.Windows.Forms.Button
$editMemoryButton.Location = New-Object System.Drawing.Point(16, 66)
$editMemoryButton.Size = New-Object System.Drawing.Size(220, 32)
$editMemoryButton.Text = L 'settings.memory_edit'
$editMemoryButton.Add_Click({ Show-PlayerMemoryEditor })
$memoryGroup.Controls.Add($editMemoryButton)

$openMemoryFolderButton = New-Object System.Windows.Forms.Button
$openMemoryFolderButton.Location = New-Object System.Drawing.Point(248, 66)
$openMemoryFolderButton.Size = New-Object System.Drawing.Size(284, 32)
$openMemoryFolderButton.Text = L 'settings.memory_folder'
$openMemoryFolderButton.Add_Click({
    try {
        New-Item -ItemType Directory -Path $playersRoot -Force | Out-Null
        Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $playersRoot + '"')
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            (L 'settings.memory_folder_failed' @($_.Exception.Message)),
            (L 'settings.memory_folder_failed_title'),
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
    }
})
$memoryGroup.Controls.Add($openMemoryFolderButton)

$memoryRules = New-Object System.Windows.Forms.Label
$memoryRules.Location = New-Object System.Drawing.Point(16, 110)
$memoryRules.Size = New-Object System.Drawing.Size(516, 138)
$memoryRules.Text = L 'settings.memory_rules_overview' @($memoryMaxItems, $memoryMaxChars)
$memoryGroup.Controls.Add($memoryRules)

$localContextIntro = New-Object System.Windows.Forms.Label
$localContextIntro.Location = New-Object System.Drawing.Point(20, 20)
$localContextIntro.Size = New-Object System.Drawing.Size(550, 52)
$localContextIntro.Text = L 'settings.local_intro'
$localContextPage.Controls.Add($localContextIntro)

$localContextEnabled = New-Object System.Windows.Forms.CheckBox
$localContextEnabled.Location = New-Object System.Drawing.Point(20, 88)
$localContextEnabled.Size = New-Object System.Drawing.Size(550, 24)
$localContextEnabled.Text = L 'settings.local_enabled'
$localContextEnabled.Checked = [bool]$localContext.enabled
$localContextPage.Controls.Add($localContextEnabled)

$localTimeEnabled = New-Object System.Windows.Forms.CheckBox
$localTimeEnabled.Location = New-Object System.Drawing.Point(42, 126)
$localTimeEnabled.Size = New-Object System.Drawing.Size(528, 24)
$localTimeEnabled.Text = L 'settings.local_time'
$localTimeEnabled.Checked = [bool]$localContext.time_enabled
$localContextPage.Controls.Add($localTimeEnabled)

$localMediaEnabled = New-Object System.Windows.Forms.CheckBox
$localMediaEnabled.Location = New-Object System.Drawing.Point(42, 160)
$localMediaEnabled.Size = New-Object System.Drawing.Size(528, 24)
$localMediaEnabled.Text = L 'settings.local_media'
$localMediaEnabled.Checked = [bool]$localContext.media_enabled
$localContextPage.Controls.Add($localMediaEnabled)

$localContextPrivacy = New-Object System.Windows.Forms.Label
$localContextPrivacy.Location = New-Object System.Drawing.Point(20, 214)
$localContextPrivacy.Size = New-Object System.Drawing.Size(550, 72)
$localContextPrivacy.Text = L 'settings.local_privacy'
$localContextPage.Controls.Add($localContextPrivacy)

$refreshLocalContextControls = {
    $localTimeEnabled.Enabled = $localContextEnabled.Checked
    $localMediaEnabled.Enabled = $localContextEnabled.Checked
}
$localContextEnabled.Add_CheckedChanged($refreshLocalContextControls)
& $refreshLocalContextControls

$updateGroup = New-Object System.Windows.Forms.GroupBox
$updateGroup.Location = New-Object System.Drawing.Point(16, 14)
$updateGroup.Size = New-Object System.Drawing.Size(570, 126)
$updateGroup.Text = L 'settings.update_group'
$updatePage.Controls.Add($updateGroup)

$currentVersionLabel = New-Object System.Windows.Forms.Label
$currentVersionLabel.Location = New-Object System.Drawing.Point(18, 28)
$currentVersionLabel.Size = New-Object System.Drawing.Size(360, 24)
$currentVersionLabel.Text = L 'settings.current_version' @([string]$releaseInfo.version)
$updateGroup.Controls.Add($currentVersionLabel)

$checkUpdateButton = New-Object System.Windows.Forms.Button
$checkUpdateButton.Location = New-Object System.Drawing.Point(420, 24)
$checkUpdateButton.Size = New-Object System.Drawing.Size(128, 32)
$checkUpdateButton.Text = L 'settings.update_group'
$updateGroup.Controls.Add($checkUpdateButton)

$updateStatusLabel = New-Object System.Windows.Forms.Label
$updateStatusLabel.Location = New-Object System.Drawing.Point(18, 62)
$updateStatusLabel.Size = New-Object System.Drawing.Size(530, 46)
$updateStatusLabel.Text = if ([string]::IsNullOrWhiteSpace([string]$releaseInfo.github_repository)) {
    L 'settings.update_not_configured'
} else {
    L 'settings.update_ready'
}
$updateGroup.Controls.Add($updateStatusLabel)

$supportGroup = New-Object System.Windows.Forms.GroupBox
$supportGroup.Location = New-Object System.Drawing.Point(16, 152)
$supportGroup.Size = New-Object System.Drawing.Size(570, 382)
$supportGroup.Text = L 'settings.support_group'
$updatePage.Controls.Add($supportGroup)

$supportIntro = New-Object System.Windows.Forms.Label
$supportIntro.Location = New-Object System.Drawing.Point(18, 26)
$supportIntro.Size = New-Object System.Drawing.Size(530, 38)
$supportIntro.Text = L 'settings.support_intro'
$supportGroup.Controls.Add($supportIntro)

$rewardCode = New-Object System.Windows.Forms.PictureBox
$rewardCode.Location = New-Object System.Drawing.Point(155, 68)
$rewardCode.Size = New-Object System.Drawing.Size(260, 260)
$rewardCode.BackColor = [System.Drawing.Color]::White
$rewardCode.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$rewardCode.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
$rewardCodePath = Join-Path $PSScriptRoot 'assets\wechat-reward-code.png'
if (Test-Path -LiteralPath $rewardCodePath) {
    $sourceRewardImage = [System.Drawing.Image]::FromFile($rewardCodePath)
    try {
        $rewardCode.Image = New-Object System.Drawing.Bitmap($sourceRewardImage)
    } finally {
        $sourceRewardImage.Dispose()
    }
}
$supportGroup.Controls.Add($rewardCode)

$supportCaption = New-Object System.Windows.Forms.Label
$supportCaption.Location = New-Object System.Drawing.Point(18, 340)
$supportCaption.Size = New-Object System.Drawing.Size(530, 24)
$supportCaption.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$supportCaption.Text = L 'settings.support_caption'
$supportGroup.Controls.Add($supportCaption)

$saveButton = New-Object System.Windows.Forms.Button
$saveButton.Location = New-Object System.Drawing.Point(414, 594)
$saveButton.Size = New-Object System.Drawing.Size(88, 30)
$saveButton.Text = L 'common.save'
$form.Controls.Add($saveButton)

$testButton = New-Object System.Windows.Forms.Button
$testButton.Location = New-Object System.Drawing.Point(316, 594)
$testButton.Size = New-Object System.Drawing.Size(88, 30)
$testButton.Text = L 'settings.test'
$form.Controls.Add($testButton)

$cancelButton = New-Object System.Windows.Forms.Button
$cancelButton.Location = New-Object System.Drawing.Point(512, 594)
$cancelButton.Size = New-Object System.Drawing.Size(88, 30)
$cancelButton.Text = L 'common.cancel'
$cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($cancelButton)
$form.CancelButton = $cancelButton
$form.AcceptButton = $saveButton

function Localize-ControlTree([System.Windows.Forms.Control] $Control) {
    if (-not [string]::IsNullOrEmpty($Control.Text)) {
        foreach ($locale in @('zh-CN', 'zh-TW', 'en', 'ja')) {
            $catalog = Get-LocaleCatalog $locale
            foreach ($entry in $catalog.GetEnumerator()) {
                if ([string]::Equals([string]$entry.Value, $Control.Text, [StringComparison]::Ordinal)) {
                    $Control.Text = L ([string]$entry.Key)
                    break
                }
            }
        }
    }
    foreach ($child in $Control.Controls) { Localize-ControlTree $child }
}

$script:applyingLocalization = $false
$applyMainLocalization = {
    if ($script:applyingLocalization) { return }
    $script:applyingLocalization = $true
    $priorLoadingProfile = [bool]$script:loadingProfile
    $script:loadingProfile = $true
    try {
    $form.Text = L 'settings.title'
    $apiPage.Text = L 'settings.tab_api'
    $musicPage.Text = L 'settings.tab_other'
    $localContextPage.Text = L 'settings.tab_local'
    $updatePage.Text = L 'settings.tab_update'
    Localize-ControlTree $form

    $providerIndex = $providerPreset.SelectedIndex
    $providerPreset.Items.Clear()
    foreach ($key in @('settings.custom','settings.volcengine','settings.deepseek','settings.kimi','settings.qwen','settings.glm','settings.gemini')) {
        [void]$providerPreset.Items.Add((L $key))
    }
    $providerPreset.SelectedIndex = $providerIndex

    $searchIndex = $searchBackend.SelectedIndex
    $searchBackend.Items.Clear()
    foreach ($value in @((L 'settings.search_off'),(L 'settings.search_provider'),'Brave Search','SearXNG')) {
        [void]$searchBackend.Items.Add($value)
    }
    $searchBackend.SelectedIndex = $searchIndex

    $musicIndex = $musicPreference.SelectedIndex
    $musicPreference.Items.Clear()
    foreach ($key in @('menu.qq_music', 'menu.netease_music', 'menu.spotify', 'menu.apple_music', 'menu.amazon_music')) {
        [void]$musicPreference.Items.Add((L $key))
    }
    $musicPreference.SelectedIndex = [Math]::Max(0, [Math]::Min($musicIndex, $musicPlayerValues.Count - 1))

    $languageIndex = $languageMode.SelectedIndex
    $languageMode.Items.Clear()
    foreach ($key in @('language.follow_game','language.zh_CN','language.zh_TW','language.en','language.ja')) {
        [void]$languageMode.Items.Add((L $key))
    }
    $languageMode.SelectedIndex = $languageIndex
    $languageLabel.Text = L 'settings.language_label'
    $languageHint.Text = L 'settings.language_hint'
    $currentVersionLabel.Text = L 'settings.current_version' @([string]$releaseInfo.version)
    $updateStatusLabel.Text = if ([string]::IsNullOrWhiteSpace([string]$releaseInfo.github_repository)) {
        L 'settings.update_not_configured'
    } else {
        L 'settings.update_ready'
    }
    $saveButton.Text = L 'common.save'
    $cancelButton.Text = L 'common.cancel'
    $testButton.Text = L 'settings.test'
    $proxyLabel.Text = L 'settings.proxy_url'
    $proxyHint.Text = L 'settings.proxy_hint'
    $dnsFallback.Text = L 'settings.dns_fallback'
    $dnsFallbackHint.Text = L 'settings.dns_fallback_hint'
    $geminiNotice.Text = L 'settings.gemini_notice'
    Refresh-SearchControls
    } finally {
        $script:loadingProfile = $priorLoadingProfile
        $script:applyingLocalization = $false
    }
}.GetNewClosure()

$script:changingLanguage = $false
$languageMode.Add_SelectedIndexChanged({
    if ($script:changingLanguage -or $script:applyingLocalization) { return }
    $script:changingLanguage = $true
    try {
        $mode = switch ($languageMode.SelectedIndex) {
            1 { 'zh-CN' }
            2 { 'zh-TW' }
            3 { 'en' }
            4 { 'ja' }
            default { 'follow_game' }
        }
        Set-JsonProperty $uiConfig 'language_mode' $mode
        $script:currentLocale = Resolve-LilithLocale
        & $applyMainLocalization
    } finally {
        $script:changingLanguage = $false
    }
}.GetNewClosure())

$tabs.Add_SelectedIndexChanged({
    $testButton.Enabled = $tabs.SelectedIndex -eq 0
})

function ConvertTo-ReleaseVersion([string] $Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $normalized = $Value.Trim() -replace '^[vV]', ''
    $normalized = ($normalized -split '[-+]')[0]
    if ($normalized -notmatch '^\d+(\.\d+){0,3}$') { return $null }
    $parts = @($normalized -split '\.')
    while ($parts.Count -lt 3) { $parts += '0' }
    try {
        return [Version]($parts -join '.')
    } catch {
        return $null
    }
}

function Invoke-LilithProviderTest([hashtable] $InputData, [string] $RuntimeRoot = $PSScriptRoot) {
    $nodePath = Join-Path $RuntimeRoot 'node.exe'
    if (-not (Test-Path -LiteralPath $nodePath)) {
        $nodePath = $env:LILITH_NODE_PATH
        if ([string]::IsNullOrWhiteSpace($nodePath)) {
            $command = Get-Command node -ErrorAction SilentlyContinue
            if ($null -ne $command) { $nodePath = $command.Source }
        }
    }
    if ([string]::IsNullOrWhiteSpace($nodePath) -or -not (Test-Path -LiteralPath $nodePath)) {
        throw 'Bundled Node runtime not found.'
    }
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $nodePath
    $startInfo.Arguments = '"' + (Join-Path $RuntimeRoot 'src\provider-test.js') + '"'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
    $startInfo.StandardErrorEncoding = New-Object Text.UTF8Encoding($false)
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $inputBytes = [Text.Encoding]::UTF8.GetBytes(($InputData | ConvertTo-Json -Depth 12 -Compress))
        $process.StandardInput.BaseStream.Write($inputBytes, 0, $inputBytes.Length)
        $process.StandardInput.Close()
        $timeout = [Math]::Max(1000, [int]$InputData.timeout_ms) + 5000
        if (-not $process.WaitForExit($timeout)) {
            $process.Kill()
            throw 'PROVIDER_TIMEOUT'
        }
        $result = $outputTask.GetAwaiter().GetResult() | ConvertFrom-Json
        [void]$errorTask.GetAwaiter().GetResult()
        if (-not $result.ok) {
            throw "$($result.code) (HTTP $($result.http_status))"
        }
        return $result.response
    } finally {
        $process.Dispose()
    }
}

function Resolve-LilithProxy([Uri] $Target) {
    if ($Target.IsLoopback) { return '' }
    $candidate = $proxyUrl.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = [Environment]::GetEnvironmentVariable('HTTPS_PROXY', 'Process')
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            $candidate = [Environment]::GetEnvironmentVariable('HTTP_PROXY', 'Process')
        }
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) { return '' }
    $parsed = $null
    if (-not [Uri]::TryCreate($candidate, [UriKind]::Absolute, [ref]$parsed) `
        -or $parsed.Scheme -notin @('http', 'https') `
        -or -not [string]::IsNullOrWhiteSpace($parsed.UserInfo)) {
        throw (L 'settings.validation_proxy')
    }
    return $parsed.AbsoluteUri
}

function Add-LilithProxyArgument([hashtable] $Arguments, [Uri] $Target) {
    $proxy = Resolve-LilithProxy $Target
    if (-not [string]::IsNullOrWhiteSpace($proxy)) {
        $Arguments.Proxy = $proxy
    }
}

$checkUpdateButton.Add_Click({
    $repository = ([string]$releaseInfo.github_repository).Trim().Trim('/')
    if ($repository -notmatch '^[^/\s]+/[^/\s]+$') {
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            (L 'settings.update_not_configured_dialog'),
            (L 'settings.tab_update'),
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information)
        return
    }

    $form.UseWaitCursor = $true
    $checkUpdateButton.Enabled = $false
    $updateStatusLabel.Text = L 'settings.update_connecting'
    try {
        [Net.ServicePointManager]::SecurityProtocol = `
            [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $headers = @{
            Accept = 'application/vnd.github+json'
            'User-Agent' = "LilithMod/$([string]$releaseInfo.version)"
            'X-GitHub-Api-Version' = '2022-11-28'
        }
        $updateUri = [Uri]"https://api.github.com/repos/$repository/releases/latest"
        $updateArguments = @{
            Uri = $updateUri
            Method = 'Get'
            Headers = $headers
            TimeoutSec = 12
        }
        Add-LilithProxyArgument $updateArguments $updateUri
        $latest = Invoke-RestMethod @updateArguments
        $latestTag = [string]$latest.tag_name
        $releaseUrl = [string]$latest.html_url
        $currentVersion = ConvertTo-ReleaseVersion ([string]$releaseInfo.version)
        $latestVersion = ConvertTo-ReleaseVersion $latestTag
        if ($null -eq $latestVersion) {
            throw (L 'settings.update_invalid_tag' @($latestTag))
        }

        if ($null -ne $currentVersion -and $latestVersion -le $currentVersion) {
            $updateStatusLabel.Text = L 'settings.update_latest_status' @($latestTag)
            [void][System.Windows.Forms.MessageBox]::Show(
                $form,
                (L 'settings.update_latest' @($latestTag)),
                (L 'settings.tab_update'),
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information)
        } else {
            $updateStatusLabel.Text = L 'settings.update_new_status' @($latestTag)
            $choice = [System.Windows.Forms.MessageBox]::Show(
                $form,
                (L 'settings.update_new_prompt' @($latestTag)),
                (L 'settings.update_new_title'),
                [System.Windows.Forms.MessageBoxButtons]::YesNo,
                [System.Windows.Forms.MessageBoxIcon]::Information)
            if ($choice -eq [System.Windows.Forms.DialogResult]::Yes `
                -and -not [string]::IsNullOrWhiteSpace($releaseUrl)) {
                Start-Process $releaseUrl
            }
        }
    } catch {
        $updateStatusLabel.Text = L 'settings.update_unavailable'
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            (L 'settings.update_failed' @($_.Exception.Message)),
            (L 'settings.tab_update'),
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
    } finally {
        $form.UseWaitCursor = $false
        $checkUpdateButton.Enabled = $true
    }
})

function Set-ReasoningChoices {
    param([string] $Label, [string[]] $Values, [string] $Selected, [bool] $Enabled = $true)
    $reasoningLabel.Text = $Label
    $reasoning.BeginUpdate()
    $reasoning.Items.Clear()
    foreach ($value in $Values) { [void]$reasoning.Items.Add($value) }
    $reasoning.EndUpdate()
    if ($Selected -notin $Values) { $Selected = $Values[0] }
    $reasoning.SelectedItem = $Selected
    $reasoning.Enabled = $Enabled
}

function Refresh-ReasoningChoices([string] $Preferred = '') {
    switch ($providerPreset.SelectedIndex) {
        1 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.thinking_mode }
            Set-ReasoningChoices (L 'settings.reasoning_volcengine') @('disabled', 'enabled') $selected
        }
        2 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.thinking_mode }
            Set-ReasoningChoices (L 'settings.reasoning_deepseek') @('disabled', 'high', 'max') $selected
        }
        3 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.thinking_mode }
            Set-ReasoningChoices (L 'settings.reasoning_kimi') @('disabled', 'enabled') $selected
        }
        4 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.thinking_mode }
            Set-ReasoningChoices (L 'settings.reasoning_qwen') @('disabled', 'enabled') $selected
        }
        5 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.thinking_mode }
            Set-ReasoningChoices (L 'settings.reasoning_glm') @('disabled', 'enabled') $selected
        }
        6 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.reasoning_effort }
            Set-ReasoningChoices (L 'settings.reasoning_gemini') @('minimal', 'low', 'medium', 'high') $selected
        }
        default {
            if ([string]$wireApi.SelectedItem -eq 'responses') {
                $selected = if ($Preferred) { $Preferred } else { [string]$provider.reasoning_effort }
                Set-ReasoningChoices (L 'settings.reasoning_responses') @('', 'low', 'medium', 'high', 'xhigh') $selected
            } else {
                Set-ReasoningChoices (L 'settings.reasoning_provider') @('default') 'default' $false
            }
        }
    }
}

function Refresh-SearchControls {
    $mode = $searchBackend.SelectedIndex
    $searchBase.Enabled = $mode -eq 3
    $searchKey.Enabled = $mode -eq 2
    $showSearchKey.Enabled = $searchKey.Enabled
    if ($mode -eq 1) {
        if ([string]$wireApi.SelectedItem -eq 'gemini_interactions') {
            $hint.Text = L 'settings.search_hint_gemini'
        } elseif ([string]$wireApi.SelectedItem -eq 'responses') {
            $hint.Text = L 'settings.search_hint_responses'
        } else {
            $hint.Text = switch ($providerPreset.SelectedIndex) {
                4 { L 'settings.search_hint_qwen' }
                2 { L 'settings.search_hint_deepseek' }
                3 { L 'settings.search_hint_kimi' }
                default { L 'settings.search_hint_chat' }
            }
        }
    } elseif ($mode -eq 2) {
        $hint.Text = L 'settings.search_hint_brave'
    } elseif ($mode -eq 3) {
        $hint.Text = L 'settings.search_hint_searxng'
    } else {
        $hint.Text = L 'settings.search_hint_off'
    }
    $geminiNotice.Visible = $providerPreset.SelectedIndex -eq 6
}

$searchBackend.Add_SelectedIndexChanged({ Refresh-SearchControls })

$loadingProfile = $false
$script:geminiSearchDefaultApplied = [bool]$geminiProfileWasStored
$wireApi.Enabled = $providerKind -ne 'gemini'
$wireApi.Add_SelectedIndexChanged({
    if ($loadingProfile) { return }
    Refresh-ReasoningChoices
    Refresh-SearchControls
})
Refresh-ReasoningChoices
Refresh-SearchControls
& $applyMainLocalization

function Get-SelectedProviderKind {
    switch ($providerPreset.SelectedIndex) {
        1 { 'volcengine' }
        2 { 'deepseek' }
        3 { 'kimi' }
        4 { 'qwen' }
        5 { 'glm' }
        6 { 'gemini' }
        default { 'custom' }
    }
}

function Set-ProfileModelChoices([string] $Kind, [string] $Selected) {
    switch ($Kind) {
        'volcengine' { Set-ModelChoices @('deepseek-v4-1-flash-260910', 'deepseek-v4-flash-ga-260731', 'deepseek-v4-pro-ga-260813', 'doubao-seed-character-260628', 'doubao-seed-2-1-turbo-260628', 'doubao-seed-2-1-pro-260628', 'doubao-seed-2-0-lite-260428') $Selected }
        'deepseek' { Set-ModelChoices @('deepseek-flash', 'deepseek-v4-pro') $Selected }
        'kimi' { Set-ModelChoices @('kimi-k3', 'kimi-k2.6') $Selected }
        'qwen' { Set-ModelChoices @('qwen3.5-flash', 'qwen3.6-flash', 'qwen3.7-plus') $Selected }
        'glm' { Set-ModelChoices @('glm-4.7-flash', 'glm-4.7-flashx', 'glm-5.2') $Selected }
        'gemini' { Set-ModelChoices @('gemini-3.6-flash', 'gemini-3.5-flash', 'gemini-3.5-flash-lite', 'gemini-2.5-pro', 'gemini-2.5-flash', 'gemini-2.5-flash-lite') $Selected }
        default { Set-ModelChoices @() $Selected }
    }
}

function Capture-ProviderProfile {
    $profile = $profiles[$currentProviderKind]
    Set-JsonProperty $profile 'base_url' $baseUrl.Text.Trim().TrimEnd('/')
    Set-JsonProperty $profile 'model' $model.Text.Trim()
    Set-JsonProperty $profile 'wire_api' ([string]$wireApi.SelectedItem)
    if (-not [string]::IsNullOrWhiteSpace($apiKey.Text)) {
        Set-JsonProperty $profile 'api_key' $apiKey.Text.Trim()
    }
    if ($currentProviderKind -eq 'gemini') {
        Set-JsonProperty $profile 'wire_api' 'gemini_interactions'
        Set-JsonProperty $profile 'reasoning_effort' ([string]$reasoning.SelectedItem)
        Set-JsonProperty $profile 'thinking_mode' 'default'
    } elseif ($currentProviderKind -eq 'custom') {
        $effort = if ([string]$wireApi.SelectedItem -eq 'responses') { [string]$reasoning.SelectedItem } else { '' }
        Set-JsonProperty $profile 'reasoning_effort' $effort
        Set-JsonProperty $profile 'thinking_mode' 'default'
    } else {
        Set-JsonProperty $profile 'reasoning_effort' ''
        Set-JsonProperty $profile 'thinking_mode' ([string]$reasoning.SelectedItem)
    }
}

function Load-ProviderProfile([string] $Kind) {
    $script:loadingProfile = $true
    $script:currentProviderKind = $Kind
    $script:provider = $profiles[$Kind]
    $baseUrl.Text = [string]$provider.base_url
    $apiKey.Text = ''
    $showKey.Checked = $false
    Set-ProfileModelChoices $Kind ([string]$provider.model)
    $wire = [string]$provider.wire_api
    if ($wire -notin @('chat_completions', 'responses', 'gemini_interactions')) { $wire = 'chat_completions' }
    if ($Kind -eq 'gemini') { $wire = 'gemini_interactions' }
    $wireApi.SelectedItem = $wire
    $wireApi.Enabled = $Kind -ne 'gemini'
    $script:loadingProfile = $false
    Refresh-ReasoningChoices
    Refresh-SearchControls
}

$providerPreset.Add_SelectedIndexChanged({
    if ($loadingProfile) { return }
    Capture-ProviderProfile
    $selectedKind = Get-SelectedProviderKind
    Load-ProviderProfile $selectedKind
    if ($selectedKind -eq 'gemini' -and -not $script:geminiSearchDefaultApplied) {
        $searchBackend.SelectedIndex = 1
        $script:geminiSearchDefaultApplied = $true
    } elseif ($selectedKind -eq 'volcengine') {
        $searchBackend.SelectedIndex = 1
    } elseif ($selectedKind -in @('deepseek', 'kimi', 'glm') `
        -and $searchBackend.SelectedIndex -eq 1) {
        $searchBackend.SelectedIndex = 0
    }
})

function Reload-Companion {
    $runtimePath = Join-Path $dataRoot 'runtime.json'
    if (-not (Test-Path -LiteralPath $runtimePath)) { return $false }
    try {
        $runtime = Get-Content -LiteralPath $runtimePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace([string]$runtime.token)) { return $false }
        $headers = @{ Authorization = "Bearer $($runtime.token)" }
        $body = @{ action = 'reload' } | ConvertTo-Json
        $result = Invoke-RestMethod `
            -Uri "http://$($runtime.host):$($runtime.port)/v1/control" `
            -Method Post `
            -Headers $headers `
            -ContentType 'application/json; charset=utf-8' `
            -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
            -TimeoutSec 4
        return [bool]$result.ok
    } catch {
        return $false
    }
}

function Get-ApiErrorMessage([object] $ErrorRecord) {
    $detail = [string]$ErrorRecord.ErrorDetails.Message
    if (-not [string]::IsNullOrWhiteSpace($detail)) {
        try {
            $errorBody = $detail | ConvertFrom-Json
            $code = [string]$errorBody.error.code
            $message = [string]$errorBody.error.message
            if ($code -eq 'ToolNotOpen') {
                return (L 'settings.api_tool_not_open')
            }
            if ($code -eq 'InvalidEndpointOrModel.NotFound') {
                return (L 'settings.api_model_not_found' @($modelName))
            }
            if (-not [string]::IsNullOrWhiteSpace($message)) {
                return $(if ($code) { "$code：$message" } else { $message })
            }
        } catch {
            return $detail
        }
    }
    return [string]$ErrorRecord.Exception.Message
}

$testButton.Add_Click({
    $form.UseWaitCursor = $true
    $testButton.Enabled = $false
    try {
        $base = $baseUrl.Text.Trim().TrimEnd('/')
        $modelName = $model.Text.Trim()
        $testKey = $apiKey.Text.Trim()
        if ([string]::IsNullOrWhiteSpace($testKey)) { $testKey = [string]$provider.api_key }
        $parsedUri = $null
        if (-not [Uri]::TryCreate($base, [UriKind]::Absolute, [ref]$parsedUri) `
            -or $parsedUri.Scheme -notin @('http', 'https')) {
            throw (L 'settings.validation_base_url')
        }
        if ([string]::IsNullOrWhiteSpace($modelName)) { throw (L 'settings.validation_model') }
        if ([string]::IsNullOrWhiteSpace($testKey)) { throw (L 'settings.validation_api_key') }

        $isGemini = [string]$wireApi.SelectedItem -eq 'gemini_interactions'
        $isResponses = [string]$wireApi.SelectedItem -eq 'responses'
        # A connection test must not incur a Google Search grounding charge.
        $testBuiltInSearch = $searchBackend.SelectedIndex -eq 1 -and -not $isGemini
        $endpoint = $base + $(if ($isGemini) { '/interactions' } elseif ($isResponses) { '/responses' } else { '/chat/completions' })
        $payload = if ($isGemini) {
            @{
                model = $modelName
                store = $false
                system_instruction = 'Return only OK.'
                input = 'OK'
                generation_config = @{
                    thinking_level = [string]$reasoning.SelectedItem
                    max_output_tokens = 32
                }
            }
        } elseif ($isResponses) {
            @{
                model = $modelName
                input = $(if ($testBuiltInSearch) { '请使用联网搜索确认今天的日期，只回复 OK' } else { '只回复 OK' })
                max_output_tokens = 32
            }
        } else {
            @{
                model = $modelName
                messages = @(@{
                    role = 'user'
                    content = $(if ($testBuiltInSearch) { '请使用联网搜索确认今天的日期，只回复 OK' } else { '只回复 OK' })
                })
                max_tokens = 32
                stream = $false
            }
        }
        if ($testBuiltInSearch -and $isResponses) {
            $toolName = [string]$provider.search_tool
            if ([string]::IsNullOrWhiteSpace($toolName)) { $toolName = 'web_search' }
            $payload.tools = @(@{ type = $toolName })
        }
        if ($isResponses -and $providerPreset.SelectedIndex -eq 1) {
            $payload.thinking = @{ type = [string]$reasoning.SelectedItem }
        }
        if (-not $isResponses) {
            $thinkingMode = [string]$reasoning.SelectedItem
            switch ($providerPreset.SelectedIndex) {
                2 {
                    $payload.thinking = @{ type = $(if ($thinkingMode -eq 'disabled') { 'disabled' } else { 'enabled' }) }
                    if ($thinkingMode -in @('high', 'max')) { $payload.reasoning_effort = $thinkingMode }
                }
                3 { $payload.thinking = @{ type = $thinkingMode } }
                4 {
                    $payload.enable_thinking = $thinkingMode -eq 'enabled'
                    if ($testBuiltInSearch) { $payload.enable_search = $true }
                }
                5 { $payload.thinking = @{ type = $thinkingMode } }
            }
        }
        $headers = if ($isGemini) {
            @{ 'x-goog-api-key' = $testKey }
        } else {
            @{ Authorization = "Bearer $testKey" }
        }
        $testResult = Invoke-LilithProviderTest @{
            target = $endpoint
            payload = $payload
            headers = $headers
            timeout_ms = [int]$provider.timeout_ms
            network = @{
                proxy_url = $proxyUrl.Text.Trim()
                provider_dns_servers = @($network.provider_dns_servers)
                provider_dns_fallback_enabled = [bool]$dnsFallback.Checked
            }
        }
        if ($testBuiltInSearch -and $isResponses) {
            $searchUsed = @($testResult.output | Where-Object { $_.type -eq 'web_search_call' }).Count -gt 0
            if (-not $searchUsed) {
                throw (L 'settings.test_no_web_search')
            }
        }
        $successMessage = if ($testBuiltInSearch) {
            L 'settings.test_search_success'
        } else {
            L 'settings.test_success'
        }
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            $successMessage,
            (L 'settings.test'),
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information)
    } catch {
        $errorMessage = Get-ApiErrorMessage $_
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            (L 'settings.test_failed' @($errorMessage)),
            (L 'settings.test'),
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
    } finally {
        $form.UseWaitCursor = $false
        $testButton.Enabled = $true
    }
})

$saveButton.Add_Click({
    try {
        $base = $baseUrl.Text.Trim()
        $modelName = $model.Text.Trim()
        $newKey = $apiKey.Text.Trim()
        $hasApiConfiguration = -not [string]::IsNullOrWhiteSpace($modelName) `
            -and (-not [string]::IsNullOrWhiteSpace($newKey) `
                -or -not [string]::IsNullOrWhiteSpace([string]$provider.api_key))
        if ($tabs.SelectedIndex -eq 0 -or $hasApiConfiguration) {
        $parsedUri = $null
        if (-not [Uri]::TryCreate($base, [UriKind]::Absolute, [ref]$parsedUri) `
            -or $parsedUri.Scheme -notin @('http', 'https')) {
            throw (L 'settings.validation_base_url')
        }
        if ([string]::IsNullOrWhiteSpace($modelName)) {
            throw (L 'settings.validation_model')
        }
        if ([string]::IsNullOrWhiteSpace($newKey) -and [string]::IsNullOrWhiteSpace([string]$provider.api_key)) {
            throw (L 'settings.validation_api_key')
        }
        Capture-ProviderProfile
        Set-JsonProperty $config 'mock_mode' $false
        $selectedKind = Get-SelectedProviderKind
        $script:provider = $profiles[$selectedKind]
        Set-JsonProperty $config 'active_provider' $selectedKind
        Set-JsonProperty $config 'provider' $provider
        Set-JsonProperty $config 'provider_profiles' ([pscustomobject]$profiles)

        $searchBackendValue = switch ($searchBackend.SelectedIndex) {
            1 { 'provider' }
            2 { 'brave' }
            3 { 'searxng' }
            default { 'off' }
        }
        if ($searchBackendValue -eq 'provider') {
            $builtInAllowed = ($selectedKind -eq 'qwen') `
                -or ($selectedKind -eq 'gemini' -and [string]$wireApi.SelectedItem -eq 'gemini_interactions') `
                -or ($selectedKind -eq 'volcengine' -and [string]$wireApi.SelectedItem -eq 'responses') `
                -or ($selectedKind -eq 'custom' -and [string]$wireApi.SelectedItem -eq 'responses')
            if (-not $builtInAllowed) {
                throw (L 'settings.validation_search_unsupported')
            }
        }
        if ($searchBackendValue -eq 'brave') {
            $newSearchKey = $searchKey.Text.Trim()
            if ([string]::IsNullOrWhiteSpace($newSearchKey) -and [string]::IsNullOrWhiteSpace($existingSearchKey)) {
                throw (L 'settings.validation_brave_key')
            }
            if (-not [string]::IsNullOrWhiteSpace($newSearchKey)) {
                Set-JsonProperty $webSearch 'api_key' $newSearchKey
            }
        }
        if ($searchBackendValue -eq 'searxng') {
            $searchUrl = $searchBase.Text.Trim().TrimEnd('/')
            $parsedSearchUri = $null
            if (-not [Uri]::TryCreate($searchUrl, [UriKind]::Absolute, [ref]$parsedSearchUri) `
                -or $parsedSearchUri.Scheme -notin @('http', 'https')) {
                throw (L 'settings.validation_searxng_url')
            }
            Set-JsonProperty $webSearch 'base_url' $searchUrl
        }
        Set-JsonProperty $webSearch 'enabled' ($searchBackendValue -ne 'off')
        Set-JsonProperty $webSearch 'backend' $searchBackendValue
        Set-JsonProperty $conversation 'reply_max_chars' ([int]$replyLimit.Value)
        Set-JsonProperty $conversation 'allow_parenthetical_actions' ([bool]$parentheticalActions.Checked)
        }
        $newProxyUrl = $proxyUrl.Text.Trim()
        if (-not [string]::IsNullOrWhiteSpace($newProxyUrl)) {
            $parsedProxyUri = $null
            if (-not [Uri]::TryCreate($newProxyUrl, [UriKind]::Absolute, [ref]$parsedProxyUri) `
                -or $parsedProxyUri.Scheme -notin @('http', 'https') `
                -or -not [string]::IsNullOrWhiteSpace($parsedProxyUri.UserInfo)) {
                throw (L 'settings.validation_proxy')
            }
            $newProxyUrl = $parsedProxyUri.AbsoluteUri.TrimEnd('/')
        }
        Set-JsonProperty $network 'proxy_url' $newProxyUrl
        Set-JsonProperty $network 'provider_dns_fallback_enabled' ([bool]$dnsFallback.Checked)
        $selectedLanguageMode = switch ($languageMode.SelectedIndex) {
            1 { 'zh-CN' }
            2 { 'zh-TW' }
            3 { 'en' }
            4 { 'ja' }
            default { 'follow_game' }
        }
        Set-JsonProperty $uiConfig 'language_mode' $selectedLanguageMode
        $selectedMusicPlayer = if ($musicPreference.SelectedIndex -ge 0 -and
            $musicPreference.SelectedIndex -lt $musicPlayerValues.Count) {
            $musicPlayerValues[$musicPreference.SelectedIndex]
        } else {
            'qqmusic'
        }
        Set-JsonProperty $music 'preferred_player' $selectedMusicPlayer
        Set-JsonProperty $localContext 'enabled' ([bool]$localContextEnabled.Checked)
        Set-JsonProperty $localContext 'time_enabled' ([bool]$localTimeEnabled.Checked)
        Set-JsonProperty $localContext 'media_enabled' ([bool]$localMediaEnabled.Checked)
        Set-JsonProperty $uiPreferences 'Scale' ([double]$chatScale.Value / 100.0)
        Set-JsonProperty $uiPreferences 'EnhancedCrossScreenDrag' ([bool]$enhancedCrossScreenDrag.Checked)

        $json = $config | ConvertTo-Json -Depth 10
        [IO.File]::WriteAllText(
            $configPath,
            $json + [Environment]::NewLine,
            (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText(
            $uiPath,
            (($uiPreferences | ConvertTo-Json -Depth 5) + [Environment]::NewLine),
            (New-Object Text.UTF8Encoding($false)))
        $reloaded = Reload-Companion
        $message = if ($reloaded) {
            L 'settings.saved_live'
        } else {
            L 'settings.saved_restart'
        }
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            $message,
            (L 'settings.title'),
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information)
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            $_.Exception.Message,
            (L 'settings.save_failed'),
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
    }
})

if ($env:LILITH_CONFIGURE_STARTUP_SMOKE -eq '1') {
    $form.ShowInTaskbar = $false
    $form.Opacity = 0
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    if ($apiPage.HorizontalScroll.Visible) {
        throw 'AI API settings page unexpectedly requires horizontal scrolling.'
    }
    if (-not $apiPage.VerticalScroll.Visible) {
        throw 'AI API settings page should retain vertical scrolling.'
    }
    if ($dnsFallback.Checked -ne [bool]$network.provider_dns_fallback_enabled -or
        $dnsFallback.Text -ne (L 'settings.dns_fallback')) {
        throw 'DNS recovery control did not retain configuration/localization.'
    }
    $originalPreset = $providerPreset.SelectedIndex
    $originalModel = $model.Text
    $providerPreset.SelectedIndex = 2
    $model.Text = 'smoke-handwritten-model'
    $providerPreset.SelectedIndex = 1
    if ($model.Items -notcontains 'deepseek-v4-1-flash-260910' -or
        $model.Items -notcontains 'deepseek-v4-flash-ga-260731' -or
        $model.Items -notcontains 'deepseek-v4-pro-ga-260813') {
        throw 'Ark model choices are incomplete.'
    }
    $providerPreset.SelectedIndex = 2
    if ($model.Text -ne 'smoke-handwritten-model' -or $model.Items -notcontains 'deepseek-flash') {
        throw 'Provider switching overwrote a manually entered model.'
    }
    $providerPreset.SelectedIndex = $originalPreset
    $model.Text = $originalModel
    $form.Hide()
} else {
    [void]$form.ShowDialog()
}
if ($null -ne $rewardCode.Image) {
    $rewardCode.Image.Dispose()
}
$form.Dispose()
