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
    version = '0.2.0'
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

if ($null -eq $config.provider) {
    Set-JsonProperty $config 'provider' ([pscustomobject]@{})
}
if ($null -eq $config.web_search) {
    Set-JsonProperty $config 'web_search' ([pscustomobject]@{})
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
$conversation = $config.conversation
$music = $config.music
$localContext = $config.local_context
$memory = $config.memory
$playersRoot = Join-Path $dataRoot 'players'
$memoryMaxItems = [Math]::Max(1, [Math]::Min(50, [int]$memory.long_term_max_items))
$memoryMaxChars = [Math]::Max(200, [Math]::Min(5000, [int]$memory.long_term_max_chars))

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
        status = '未检测到正在运行的游戏；仍可编辑已读取的记忆，但不能修改游戏称呼。'
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
            $result.status = '游戏运行中；保存后会同步修改游戏存档和莉莉丝使用的称呼。'
        }
    } catch {
        $result.status = '无法读取游戏状态；仍可编辑已读取的记忆，但不能修改游戏称呼。'
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
        throw '游戏或 Mod 当前不可用，请保持游戏运行并重新打开记忆编辑器。'
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
                    throw '游戏会话已变化，请重新打开记忆编辑器。'
                }
                if (-not [bool]$result.success) {
                    $message = [string]$result.error
                    if ([string]::IsNullOrWhiteSpace($message)) { $message = '游戏拒绝修改玩家称呼。' }
                    throw $message
                }
                return $result
            } catch [System.Management.Automation.RuntimeException] {
                throw
            } catch {
                # The plugin may be replacing the result file; retry until timeout.
            }
        }
        throw '等待游戏确认称呼修改超时，请保持游戏运行后重试。'
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
    if (@($Facts).Count -gt $memoryMaxItems) { throw "长期记忆最多保存$memoryMaxItems 条。" }
    $totalChars = 0
    foreach ($fact in @($Facts)) {
        $totalChars += Get-TextElementCount (([string]$fact.key) + ([string]$fact.value))
    }
    if ($totalChars -gt $memoryMaxChars) {
        throw "长期记忆合计不能超过$memoryMaxChars 个字符；请精简后再修改称呼。"
    }
}
$existingBackend = [string]$webSearch.backend
$existingSearchKey = [string]$webSearch.api_key
$existingSearchBase = [string]$webSearch.base_url
$providerKind = [string]$config.active_provider
if ($providerKind -notin @('custom', 'volcengine', 'deepseek', 'kimi', 'qwen', 'glm')) {
    $providerKind = [string]$provider.kind
}
if ($providerKind -notin @('custom', 'volcengine', 'deepseek', 'kimi', 'qwen', 'glm')) {
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
    } else {
        'custom'
    }
}

function New-ProviderProfile([string] $Kind) {
    $values = switch ($Kind) {
        'volcengine' { @{ base_url = 'https://ark.cn-beijing.volces.com/api/v3'; model = 'deepseek-v4-flash-260425'; wire_api = 'responses'; thinking_mode = 'disabled' } }
        'deepseek' { @{ base_url = 'https://api.deepseek.com'; model = 'deepseek-v4-flash'; wire_api = 'chat_completions'; thinking_mode = 'disabled' } }
        'kimi' { @{ base_url = 'https://api.moonshot.cn/v1'; model = 'kimi-k3'; wire_api = 'chat_completions'; thinking_mode = 'disabled' } }
        'qwen' { @{ base_url = 'https://dashscope.aliyuncs.com/compatible-mode/v1'; model = 'qwen3.5-flash'; wire_api = 'chat_completions'; thinking_mode = 'disabled' } }
        'glm' { @{ base_url = 'https://open.bigmodel.cn/api/paas/v4'; model = 'glm-4.7-flash'; wire_api = 'chat_completions'; thinking_mode = 'disabled' } }
        default { @{ base_url = ''; model = ''; wire_api = 'chat_completions'; thinking_mode = 'default' } }
    }
    return [pscustomobject]@{
        kind = $Kind
        base_url = $values.base_url
        api_key = ''
        model = $values.model
        wire_api = $values.wire_api
        thinking_mode = $values.thinking_mode
        reasoning_effort = ''
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
foreach ($kind in @('custom', 'volcengine', 'deepseek', 'kimi', 'qwen', 'glm')) {
    $profile = New-ProviderProfile $kind
    $stored = if ($null -ne $storedProfiles) { $storedProfiles.PSObject.Properties[$kind].Value } else { $null }
    $profile = Merge-ProviderProfile $profile $stored
    if ($kind -eq $providerKind) {
        $profile = Merge-ProviderProfile $profile $config.provider
        Set-JsonProperty $profile 'kind' $kind
    }
    if ($kind -eq 'volcengine') {
        if ([string]$profile.model -eq 'deepseek-v4-flash') {
            Set-JsonProperty $profile 'model' 'deepseek-v4-flash-260425'
        } elseif ([string]$profile.model -eq 'deepseek-v4-pro') {
            Set-JsonProperty $profile 'model' 'deepseek-v4-pro-260425'
        }
    }
    $profiles[$kind] = $profile
}
$provider = $profiles[$providerKind]
$currentProviderKind = $providerKind

$form = New-Object System.Windows.Forms.Form
$form.Text = '莉莉丝 Mod 设置'
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
$apiPage.Text = 'AI API 设置'
$musicPage = New-Object System.Windows.Forms.TabPage
$musicPage.Text = '其他'
$localContextPage = New-Object System.Windows.Forms.TabPage
$localContextPage.Text = '本机状态'
$updatePage = New-Object System.Windows.Forms.TabPage
$updatePage.Text = '检查更新'
[void]$tabs.TabPages.Add($apiPage)
[void]$tabs.TabPages.Add($musicPage)
[void]$tabs.TabPages.Add($localContextPage)
[void]$tabs.TabPages.Add($updatePage)
$form.Controls.Add($tabs)

$intro = New-Object System.Windows.Forms.Label
$intro.Location = New-Object System.Drawing.Point(20, 16)
$intro.Size = New-Object System.Drawing.Size(580, 32)
$intro.Text = '填写中转站或模型服务商提供的信息。API 密钥只保存在本机。'
$apiPage.Controls.Add($intro)

function Add-Label([string] $text, [int] $x, [int] $y, [int] $width = 260) {
    $label = New-Object System.Windows.Forms.Label
    $label.Location = New-Object System.Drawing.Point($x, $y)
    $label.Size = New-Object System.Drawing.Size($width, 20)
    $label.Text = $text
    $apiPage.Controls.Add($label)
}

Add-Label '服务商预设' 20 54
$providerPreset = New-Object LilithComboBox
$providerPreset.Location = New-Object System.Drawing.Point(20, 75)
$providerPreset.Size = New-Object System.Drawing.Size(580, 24)
$providerPreset.DropDownStyle = 'DropDownList'
[void]$providerPreset.Items.Add('自定义')
[void]$providerPreset.Items.Add('火山方舟')
[void]$providerPreset.Items.Add('DeepSeek 官方')
[void]$providerPreset.Items.Add('Kimi 官方')
[void]$providerPreset.Items.Add('Qwen 官方（阿里云百炼）')
[void]$providerPreset.Items.Add('GLM 官方（智谱 AI）')
$providerPreset.SelectedIndex = switch ($providerKind) {
    'volcengine' { 1 }
    'deepseek' { 2 }
    'kimi' { 3 }
    'qwen' { 4 }
    'glm' { 5 }
    default { 0 }
}
$apiPage.Controls.Add($providerPreset)

Add-Label 'Base URL' 20 109
$baseUrl = New-Object System.Windows.Forms.TextBox
$baseUrl.Location = New-Object System.Drawing.Point(20, 130)
$baseUrl.Size = New-Object System.Drawing.Size(580, 24)
$baseUrl.Text = [string]$provider.base_url
$apiPage.Controls.Add($baseUrl)

Add-Label 'API 密钥（留空则保留已保存的密钥）' 20 164 340
$apiKey = New-Object System.Windows.Forms.TextBox
$apiKey.Location = New-Object System.Drawing.Point(20, 185)
$apiKey.Size = New-Object System.Drawing.Size(465, 24)
$apiKey.UseSystemPasswordChar = $true
$apiPage.Controls.Add($apiKey)

$showKey = New-Object System.Windows.Forms.CheckBox
$showKey.Location = New-Object System.Drawing.Point(494, 186)
$showKey.Size = New-Object System.Drawing.Size(106, 24)
$showKey.Text = '显示密钥'
$showKey.Add_CheckedChanged({ $apiKey.UseSystemPasswordChar = -not $showKey.Checked })
$apiPage.Controls.Add($showKey)

Add-Label '模型名称' 20 219
$model = New-Object LilithComboBox
$model.Location = New-Object System.Drawing.Point(20, 240)
$model.Size = New-Object System.Drawing.Size(580, 24)
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
        Set-ModelChoices @('deepseek-v4-flash-260425', 'deepseek-v4-pro-260425', 'doubao-seed-character-260628', 'doubao-seed-2-1-turbo-260628', 'doubao-seed-2-1-pro-260628', 'doubao-seed-2-0-lite-260428') ([string]$provider.model)
    }
    'deepseek' {
        Set-ModelChoices @('deepseek-v4-flash', 'deepseek-v4-pro') ([string]$provider.model)
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
    default {
        Set-ModelChoices @() ([string]$provider.model)
    }
}

Add-Label '接口类型' 20 274 260
$wireApi = New-Object LilithComboBox
$wireApi.Location = New-Object System.Drawing.Point(20, 295)
$wireApi.Size = New-Object System.Drawing.Size(270, 24)
$wireApi.DropDownStyle = 'DropDownList'
[void]$wireApi.Items.Add('chat_completions')
[void]$wireApi.Items.Add('responses')
$selectedWire = [string]$provider.wire_api
if ($selectedWire -notin @('chat_completions', 'responses')) { $selectedWire = 'chat_completions' }
$wireApi.SelectedItem = $selectedWire
$apiPage.Controls.Add($wireApi)

$reasoningLabel = New-Object System.Windows.Forms.Label
$reasoningLabel.Location = New-Object System.Drawing.Point(310, 274)
$reasoningLabel.Size = New-Object System.Drawing.Size(290, 20)
$apiPage.Controls.Add($reasoningLabel)
$reasoning = New-Object LilithComboBox
$reasoning.Location = New-Object System.Drawing.Point(310, 295)
$reasoning.Size = New-Object System.Drawing.Size(290, 24)
$reasoning.DropDownStyle = 'DropDownList'
$apiPage.Controls.Add($reasoning)

Add-Label '联网搜索方式（不一定能正常使用）' 20 335 270
$searchBackend = New-Object LilithComboBox
$searchBackend.Location = New-Object System.Drawing.Point(20, 356)
$searchBackend.Size = New-Object System.Drawing.Size(270, 24)
$searchBackend.DropDownStyle = 'DropDownList'
[void]$searchBackend.Items.Add('关闭')
[void]$searchBackend.Items.Add('模型内置')
[void]$searchBackend.Items.Add('Brave Search')
[void]$searchBackend.Items.Add('SearXNG')
$selectedSearch = if (-not [bool]$webSearch.enabled) {
    '关闭'
} else {
    switch ($existingBackend) {
        'provider' { '模型内置' }
        'brave' { 'Brave Search' }
        'searxng' { 'SearXNG' }
        default { '关闭' }
    }
}
$searchBackend.SelectedItem = $selectedSearch
$apiPage.Controls.Add($searchBackend)

Add-Label 'SearXNG 地址' 310 335 290
$searchBase = New-Object System.Windows.Forms.TextBox
$searchBase.Location = New-Object System.Drawing.Point(310, 356)
$searchBase.Size = New-Object System.Drawing.Size(290, 24)
$searchBase.Text = $existingSearchBase
$apiPage.Controls.Add($searchBase)

Add-Label 'Brave Search API Key（留空则保留）' 20 390 340
$searchKey = New-Object System.Windows.Forms.TextBox
$searchKey.Location = New-Object System.Drawing.Point(20, 411)
$searchKey.Size = New-Object System.Drawing.Size(465, 24)
$searchKey.UseSystemPasswordChar = $true
$apiPage.Controls.Add($searchKey)

$showSearchKey = New-Object System.Windows.Forms.CheckBox
$showSearchKey.Location = New-Object System.Drawing.Point(494, 412)
$showSearchKey.Size = New-Object System.Drawing.Size(106, 24)
$showSearchKey.Text = '显示密钥'
$showSearchKey.Add_CheckedChanged({ $searchKey.UseSystemPasswordChar = -not $showSearchKey.Checked })
$apiPage.Controls.Add($showSearchKey)

$replyLimitLabel = New-Object System.Windows.Forms.Label
$replyLimitLabel.Location = New-Object System.Drawing.Point(20, 447)
$replyLimitLabel.Size = New-Object System.Drawing.Size(190, 24)
$replyLimitLabel.Text = '单次回复字数上限'
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
$replyLimitHint.Size = New-Object System.Drawing.Size(280, 24)
$replyLimitHint.Text = '模型会在限制内组织完整回复。'
$apiPage.Controls.Add($replyLimitHint)

$hint = New-Object System.Windows.Forms.Label
$hint.Location = New-Object System.Drawing.Point(20, 476)
$hint.Size = New-Object System.Drawing.Size(580, 28)
$apiPage.Controls.Add($hint)

$parentheticalActions = New-Object System.Windows.Forms.CheckBox
$parentheticalActions.Location = New-Object System.Drawing.Point(20, 510)
$parentheticalActions.Size = New-Object System.Drawing.Size(580, 38)
$parentheticalActions.Text = '用括号内容模拟角色表情/动作（Deepseek模型喜欢用括号返回角色表情或动作）'
$parentheticalActions.Checked = [bool]$conversation.allow_parenthetical_actions
$apiPage.Controls.Add($parentheticalActions)

$musicIntro = New-Object System.Windows.Forms.Label
$musicIntro.Location = New-Object System.Drawing.Point(20, 20)
$musicIntro.Size = New-Object System.Drawing.Size(550, 44)
$musicIntro.Text = '设置“一起听歌吧”优先打开的音乐软件，以及聊天窗口的显示大小。首选音乐软件不可用时会自动尝试另一个。'
$musicPage.Controls.Add($musicIntro)

$musicPreferenceLabel = New-Object System.Windows.Forms.Label
$musicPreferenceLabel.Location = New-Object System.Drawing.Point(20, 82)
$musicPreferenceLabel.Size = New-Object System.Drawing.Size(300, 20)
$musicPreferenceLabel.Text = '优先打开'
$musicPage.Controls.Add($musicPreferenceLabel)

$musicPreference = New-Object LilithComboBox
$musicPreference.Location = New-Object System.Drawing.Point(20, 106)
$musicPreference.Size = New-Object System.Drawing.Size(550, 24)
$musicPreference.DropDownStyle = 'DropDownList'
[void]$musicPreference.Items.Add('QQ 音乐')
[void]$musicPreference.Items.Add('网易云音乐')
$musicPreference.SelectedIndex = if ([string]$music.preferred_player -eq 'netease') { 1 } else { 0 }
$musicPage.Controls.Add($musicPreference)

$chatScaleLabel = New-Object System.Windows.Forms.Label
$chatScaleLabel.Location = New-Object System.Drawing.Point(20, 158)
$chatScaleLabel.Size = New-Object System.Drawing.Size(300, 20)
$chatScaleLabel.Text = '聊天窗口缩放（50%–200%）'
$musicPage.Controls.Add($chatScaleLabel)

$chatScale = New-Object System.Windows.Forms.NumericUpDown
$chatScale.Location = New-Object System.Drawing.Point(20, 182)
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
$chatScaleHint.Location = New-Object System.Drawing.Point(154, 184)
$chatScaleHint.Size = New-Object System.Drawing.Size(416, 36)
$chatScaleHint.Text = '聊天窗口会先按屏幕分辨率自动适配；此倍率在自动结果上额外叠加。'
$musicPage.Controls.Add($chatScaleHint)

function Show-PlayerMemoryEditor {
    $dialog = New-Object System.Windows.Forms.Form
    $dialog.Text = '手动调整莉莉丝对玩家的记忆'
    $dialog.StartPosition = 'CenterParent'
    $dialog.FormBorderStyle = 'FixedDialog'
    $dialog.MaximizeBox = $false
    $dialog.MinimizeBox = $false
    $dialog.ClientSize = New-Object System.Drawing.Size(600, 548)
    $dialog.AutoScaleMode = 'Dpi'

    $playerLabel = New-Object System.Windows.Forms.Label
    $playerLabel.Location = New-Object System.Drawing.Point(16, 16)
    $playerLabel.Size = New-Object System.Drawing.Size(250, 20)
    $playerLabel.Text = '游戏内玩家称呼'
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
    $gameStateLabel.Text = $(if ($gameState.available) { '游戏运行中' } else { '游戏未运行' })
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
    $memoryGrid.AutoSizeRowsMode = [System.Windows.Forms.DataGridViewAutoSizeRowsMode]::AllCells
    $memoryGrid.DefaultCellStyle.WrapMode = [System.Windows.Forms.DataGridViewTriState]::True
    [void]$memoryGrid.Columns.Add('MemoryKey', '项目')
    [void]$memoryGrid.Columns.Add('MemoryValue', '内容')
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
    $editorRules.Text = "修改称呼会同时更新游戏存档，并自动迁移莉莉丝对你的长期记忆；整个过程不会调用 AI。`r`n每行是一条记忆：项目≤24字符，内容≤100字符；最多$memoryMaxItems 条，项目与内容合计≤$memoryMaxChars 字符。`r`n选中整行后按 Delete 可删除；保存后从下一轮对话开始生效。请勿填写密码、验证码、证件、银行卡或 API 密钥。"
    $dialog.Controls.Add($editorRules)

    $saveMemoryButton = New-Object System.Windows.Forms.Button
    $saveMemoryButton.Location = New-Object System.Drawing.Point(352, 504)
    $saveMemoryButton.Size = New-Object System.Drawing.Size(130, 30)
    $saveMemoryButton.Text = '保存称呼与记忆'
    $dialog.Controls.Add($saveMemoryButton)

    $cancelMemoryButton = New-Object System.Windows.Forms.Button
    $cancelMemoryButton.Location = New-Object System.Drawing.Point(490, 504)
    $cancelMemoryButton.Size = New-Object System.Drawing.Size(92, 30)
    $cancelMemoryButton.Text = '取消'
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
        $memoryStatus.Text = "当前：$count / $memoryMaxItems 条，$chars / $memoryMaxChars 字符"
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
                    "记忆文件无法读取：$($_.Exception.Message)",
                    '读取记忆失败',
                    [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Warning)
            }
        }
        & $refreshMemoryStatus
    }.GetNewClosure()

    $memoryGrid.Add_CellValueChanged($refreshMemoryStatus)
    $memoryGrid.Add_RowsAdded($refreshMemoryStatus)
    $memoryGrid.Add_RowsRemoved($refreshMemoryStatus)

    $saveMemoryButton.Add_Click({
        $stagePath = ''
        try {
            $requestedName = $playerName.Text.Trim()
            if ([string]::IsNullOrWhiteSpace($requestedName)) { throw '请填写玩家称呼。' }
            if ([string]::IsNullOrWhiteSpace($originalPlayerName)) {
                throw '无法确认当前游戏内称呼，请启动游戏并重新打开记忆编辑器。'
            }

            $facts = [Collections.Generic.List[object]]::new()
            $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($row in $memoryGrid.Rows) {
                if ($row.IsNewRow) { continue }
                $key = ([string]$row.Cells[0].Value).Trim()
                $value = ([string]$row.Cells[1].Value).Trim()
                if ([string]::IsNullOrWhiteSpace($key) -and [string]::IsNullOrWhiteSpace($value)) { continue }
                if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($value)) {
                    throw '每条记忆都必须同时填写“项目”和“内容”。'
                }
                if ((Get-TextElementCount $key) -gt 24) { throw ('项目「{0}」超过24个字符。' -f $key) }
                if ((Get-TextElementCount $value) -gt 100) { throw ('项目「{0}」的内容超过100个字符。' -f $key) }
                if (-not $keys.Add($key)) { throw ('项目「{0}」重复；同类事实请保留一条并直接更新内容。' -f $key) }
                if ("$key $value" -match '(?i)密码|口令|验证码|身份证|护照|银行卡|信用卡|支付密码|api\s*key|access\s*token|secret|password|passport|cvv') {
                    throw ('项目「{0}」疑似包含敏感信息，不能写入长期记忆。' -f $key)
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
                    throw '修改游戏内称呼时必须保持游戏和 Mod 正在运行。'
                }
                $validated = Invoke-PlayerNameBridge `
                    -Operation validate `
                    -GameState $gameState `
                    -ExpectedName $originalPlayerName `
                    -RequestedName $requestedName
                $canonicalName = ([string]$validated.applied_name).Trim()
                if ([string]::IsNullOrWhiteSpace($canonicalName)) {
                    throw '游戏没有返回有效的玩家称呼。'
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
                    throw '游戏保存后的玩家称呼与预期不一致。'
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
                        throw "记忆文件提交失败，且游戏称呼自动回滚失败。旧记忆仍保留在原文件中；请重新打开设置检查。$([Environment]::NewLine)$($_.Exception.Message)"
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
                "游戏内称呼已修改为【${canonicalName}】，并迁移了$($mergedFacts.Count)条长期记忆。"
            } else {
                "已保存$($mergedFacts.Count)条长期记忆，将从下一轮对话开始生效。"
            }
            [void][System.Windows.Forms.MessageBox]::Show(
                $dialog,
                $resultMessage,
                '称呼与记忆已保存',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information)
            $dialog.DialogResult = [System.Windows.Forms.DialogResult]::OK
            $dialog.Close()
        } catch {
            [void][System.Windows.Forms.MessageBox]::Show(
                $dialog,
                $_.Exception.Message,
                '无法保存称呼与记忆',
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
$memoryGroup.Location = New-Object System.Drawing.Point(20, 238)
$memoryGroup.Size = New-Object System.Drawing.Size(550, 296)
$memoryGroup.Text = '莉莉丝对玩家的记忆'
$musicPage.Controls.Add($memoryGroup)

$memoryIntro = New-Object System.Windows.Forms.Label
$memoryIntro.Location = New-Object System.Drawing.Point(16, 26)
$memoryIntro.Size = New-Object System.Drawing.Size(516, 38)
$memoryIntro.Text = '手动查看或调整本机保存的玩家长期记忆；不会修改角色设定和当前会话。'
$memoryGroup.Controls.Add($memoryIntro)

$editMemoryButton = New-Object System.Windows.Forms.Button
$editMemoryButton.Location = New-Object System.Drawing.Point(16, 66)
$editMemoryButton.Size = New-Object System.Drawing.Size(220, 32)
$editMemoryButton.Text = '手动调整莉莉丝对玩家的记忆'
$editMemoryButton.Add_Click({ Show-PlayerMemoryEditor })
$memoryGroup.Controls.Add($editMemoryButton)

$openMemoryFolderButton = New-Object System.Windows.Forms.Button
$openMemoryFolderButton.Location = New-Object System.Drawing.Point(248, 66)
$openMemoryFolderButton.Size = New-Object System.Drawing.Size(284, 32)
$openMemoryFolderButton.Text = '打开玩家记忆文件所在目录'
$openMemoryFolderButton.Add_Click({
    try {
        New-Item -ItemType Directory -Path $playersRoot -Force | Out-Null
        Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $playersRoot + '"')
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            "无法打开玩家记忆目录：$($_.Exception.Message)",
            '打开目录失败',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
    }
})
$memoryGroup.Controls.Add($openMemoryFolderButton)

$memoryRules = New-Object System.Windows.Forms.Label
$memoryRules.Location = New-Object System.Drawing.Point(16, 110)
$memoryRules.Size = New-Object System.Drawing.Size(516, 166)
$memoryRules.Text = "手动调整方法与规范：`r`n· 游戏运行时可在编辑窗口顶部修改游戏内玩家称呼，原有长期记忆会自动迁移。`r`n· 每行填写一条记忆：「项目」是分类名称，「内容」是希望莉莉丝记住的信息。`r`n· 同类内容合并在同一项目；项目≤24字符，内容≤100字符。`r`n· 最多$memoryMaxItems 条，项目和内容合计≤$memoryMaxChars 字符。`r`n· 选中整行后按 Delete 可删除；保存后从下一轮对话生效。`r`n· 迁移设备时复制整个 players 目录；修改称呼和校验均在本地完成，不调用 AI。`r`n· 请勿填写密码、验证码、证件、银行卡或 API 密钥。"
$memoryGroup.Controls.Add($memoryRules)

$localContextIntro = New-Object System.Windows.Forms.Label
$localContextIntro.Location = New-Object System.Drawing.Point(20, 20)
$localContextIntro.Size = New-Object System.Drawing.Size(550, 52)
$localContextIntro.Text = '仅当问题涉及时间、日期、歌曲或播放状态时，才向模型提供对应的只读本机状态。普通对话不会附加这些信息。'
$localContextPage.Controls.Add($localContextIntro)

$localContextEnabled = New-Object System.Windows.Forms.CheckBox
$localContextEnabled.Location = New-Object System.Drawing.Point(20, 88)
$localContextEnabled.Size = New-Object System.Drawing.Size(550, 24)
$localContextEnabled.Text = '允许莉莉丝在相关问题中读取本机状态'
$localContextEnabled.Checked = [bool]$localContext.enabled
$localContextPage.Controls.Add($localContextEnabled)

$localTimeEnabled = New-Object System.Windows.Forms.CheckBox
$localTimeEnabled.Location = New-Object System.Drawing.Point(42, 126)
$localTimeEnabled.Size = New-Object System.Drawing.Size(528, 24)
$localTimeEnabled.Text = '本机时间、日期、星期和时区'
$localTimeEnabled.Checked = [bool]$localContext.time_enabled
$localContextPage.Controls.Add($localTimeEnabled)

$localMediaEnabled = New-Object System.Windows.Forms.CheckBox
$localMediaEnabled.Location = New-Object System.Drawing.Point(42, 160)
$localMediaEnabled.Size = New-Object System.Drawing.Size(528, 24)
$localMediaEnabled.Text = '当前音乐的播放状态、曲名和歌手（仅音乐软件）'
$localMediaEnabled.Checked = [bool]$localContext.media_enabled
$localContextPage.Controls.Add($localMediaEnabled)

$localContextPrivacy = New-Object System.Windows.Forms.Label
$localContextPrivacy.Location = New-Object System.Drawing.Point(20, 214)
$localContextPrivacy.Size = New-Object System.Drawing.Size(550, 72)
$localContextPrivacy.Text = '隐私提示：启用后，相关问题中的时间、曲名和歌手可能发送给你配置的模型或联网搜索服务商。原始状态不会作为独立记录写入历史、长期记忆或日志；模型回复中自然提到的内容会像普通对话一样保留。浏览器视频不会被读取。'
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
$updateGroup.Text = '检查更新'
$updatePage.Controls.Add($updateGroup)

$currentVersionLabel = New-Object System.Windows.Forms.Label
$currentVersionLabel.Location = New-Object System.Drawing.Point(18, 28)
$currentVersionLabel.Size = New-Object System.Drawing.Size(360, 24)
$currentVersionLabel.Text = "当前版本：$([string]$releaseInfo.version)"
$updateGroup.Controls.Add($currentVersionLabel)

$checkUpdateButton = New-Object System.Windows.Forms.Button
$checkUpdateButton.Location = New-Object System.Drawing.Point(420, 24)
$checkUpdateButton.Size = New-Object System.Drawing.Size(128, 32)
$checkUpdateButton.Text = '检查更新'
$updateGroup.Controls.Add($checkUpdateButton)

$updateStatusLabel = New-Object System.Windows.Forms.Label
$updateStatusLabel.Location = New-Object System.Drawing.Point(18, 62)
$updateStatusLabel.Size = New-Object System.Drawing.Size(530, 46)
$updateStatusLabel.Text = if ([string]::IsNullOrWhiteSpace([string]$releaseInfo.github_repository)) {
    'GitHub 仓库尚未配置。项目发布后即可按 Release 版本检查；当前不会下载或替换任何文件。'
} else {
    '从 GitHub Releases 查询最新正式版。当前只检查并打开下载页，不会自动替换文件。'
}
$updateGroup.Controls.Add($updateStatusLabel)

$supportGroup = New-Object System.Windows.Forms.GroupBox
$supportGroup.Location = New-Object System.Drawing.Point(16, 152)
$supportGroup.Size = New-Object System.Drawing.Size(570, 382)
$supportGroup.Text = '支持作者'
$updatePage.Controls.Add($supportGroup)

$supportIntro = New-Object System.Windows.Forms.Label
$supportIntro.Location = New-Object System.Drawing.Point(18, 26)
$supportIntro.Size = New-Object System.Drawing.Size(530, 38)
$supportIntro.Text = 'Mod 免费提供。喜欢这个项目的话，可以自愿请作者喝杯咖啡；不赞赏不会影响任何功能。'
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
$supportCaption.Text = '微信赞赏码 · Dreamer'
$supportGroup.Controls.Add($supportCaption)

$saveButton = New-Object System.Windows.Forms.Button
$saveButton.Location = New-Object System.Drawing.Point(414, 594)
$saveButton.Size = New-Object System.Drawing.Size(88, 30)
$saveButton.Text = '保存'
$form.Controls.Add($saveButton)

$testButton = New-Object System.Windows.Forms.Button
$testButton.Location = New-Object System.Drawing.Point(316, 594)
$testButton.Size = New-Object System.Drawing.Size(88, 30)
$testButton.Text = '测试连接'
$form.Controls.Add($testButton)

$cancelButton = New-Object System.Windows.Forms.Button
$cancelButton.Location = New-Object System.Drawing.Point(512, 594)
$cancelButton.Size = New-Object System.Drawing.Size(88, 30)
$cancelButton.Text = '取消'
$cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($cancelButton)
$form.CancelButton = $cancelButton
$form.AcceptButton = $saveButton
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

$checkUpdateButton.Add_Click({
    $repository = ([string]$releaseInfo.github_repository).Trim().Trim('/')
    if ($repository -notmatch '^[^/\s]+/[^/\s]+$') {
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            'GitHub 仓库尚未配置。等 Mod 发布后填写 owner/repository，即可启用版本检查。',
            '检查更新',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information)
        return
    }

    $form.UseWaitCursor = $true
    $checkUpdateButton.Enabled = $false
    $updateStatusLabel.Text = '正在连接 GitHub Releases……'
    try {
        [Net.ServicePointManager]::SecurityProtocol = `
            [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $headers = @{
            Accept = 'application/vnd.github+json'
            'User-Agent' = "LilithMod/$([string]$releaseInfo.version)"
            'X-GitHub-Api-Version' = '2022-11-28'
        }
        $latest = Invoke-RestMethod `
            -Uri "https://api.github.com/repos/$repository/releases/latest" `
            -Method Get `
            -Headers $headers `
            -TimeoutSec 12
        $latestTag = [string]$latest.tag_name
        $releaseUrl = [string]$latest.html_url
        $currentVersion = ConvertTo-ReleaseVersion ([string]$releaseInfo.version)
        $latestVersion = ConvertTo-ReleaseVersion $latestTag
        if ($null -eq $latestVersion) {
            throw "无法识别 GitHub Release 版本号：$latestTag"
        }

        if ($null -ne $currentVersion -and $latestVersion -le $currentVersion) {
            $updateStatusLabel.Text = "已经是最新版本（GitHub：$latestTag）。"
            [void][System.Windows.Forms.MessageBox]::Show(
                $form,
                "当前已是最新版本：$latestTag",
                '检查更新',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information)
        } else {
            $updateStatusLabel.Text = "发现新版本：$latestTag"
            $choice = [System.Windows.Forms.MessageBox]::Show(
                $form,
                "发现新版本 $latestTag。是否打开 GitHub 下载页面？`r`n`r`n当前版本不会自动替换本地文件。",
                '发现新版本',
                [System.Windows.Forms.MessageBoxButtons]::YesNo,
                [System.Windows.Forms.MessageBoxIcon]::Information)
            if ($choice -eq [System.Windows.Forms.DialogResult]::Yes `
                -and -not [string]::IsNullOrWhiteSpace($releaseUrl)) {
                Start-Process $releaseUrl
            }
        }
    } catch {
        $updateStatusLabel.Text = '暂时无法连接 GitHub；可以稍后重试。'
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            "检查失败：$($_.Exception.Message)`r`n`r`n这不会影响 Mod 的其他功能。",
            '检查更新',
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
            Set-ReasoningChoices '思考模式（火山方舟）' @('disabled', 'enabled') $selected
        }
        2 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.thinking_mode }
            Set-ReasoningChoices '思考模式（DeepSeek）' @('disabled', 'high', 'max') $selected
        }
        3 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.thinking_mode }
            Set-ReasoningChoices '思考模式（Kimi）' @('disabled', 'enabled') $selected
        }
        4 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.thinking_mode }
            Set-ReasoningChoices '思考模式（Qwen）' @('disabled', 'enabled') $selected
        }
        5 {
            $selected = if ($Preferred) { $Preferred } else { [string]$provider.thinking_mode }
            Set-ReasoningChoices '思考模式（GLM）' @('disabled', 'enabled') $selected
        }
        default {
            if ([string]$wireApi.SelectedItem -eq 'responses') {
                $selected = if ($Preferred) { $Preferred } else { [string]$provider.reasoning_effort }
                Set-ReasoningChoices '推理强度（Responses，可选）' @('', 'low', 'medium', 'high', 'xhigh') $selected
            } else {
                Set-ReasoningChoices '推理设置（由服务商决定）' @('default') 'default' $false
            }
        }
    }
}

function Refresh-SearchControls {
    $mode = [string]$searchBackend.SelectedItem
    $searchBase.Enabled = $mode -eq 'SearXNG'
    $searchKey.Enabled = $mode -eq 'Brave Search'
    $showSearchKey.Enabled = $searchKey.Enabled
    if ($mode -eq '模型内置') {
        $hint.Text = switch ($providerPreset.SelectedIndex) {
            1 { '火山方舟将通过 Responses API 调用 Web Search 联网内容插件。' }
            4 { 'Qwen 将使用 Chat Completions 的内置联网搜索。' }
            0 { '自定义服务只有在 Responses 接口支持 web_search 时才能使用模型内置搜索。' }
            2 { 'DeepSeek 直连不使用 Responses 搜索，请改选 Brave Search 或 SearXNG。' }
            3 { 'Kimi 的内置搜索接口仍在调整，当前建议改选 Brave Search 或 SearXNG。' }
            default { '当前预设尚未启用模型内置搜索，请改选 Brave Search 或 SearXNG。' }
        }
    } elseif ($mode -eq 'Brave Search') {
        $hint.Text = 'Brave Search 需要单独的搜索 API Key；模型 API Key 不会用于搜索。'
    } elseif ($mode -eq 'SearXNG') {
        $hint.Text = '填写可访问的 SearXNG 实例地址；搜索请求只发送当前问题，不上传本地聊天记录。'
    } else {
        $hint.Text = '联网搜索已关闭；普通聊天、角色设定和本地记忆不受影响。'
    }
}

$searchBackend.Add_SelectedIndexChanged({ Refresh-SearchControls })

$loadingProfile = $false
$wireApi.Add_SelectedIndexChanged({
    if ($loadingProfile) { return }
    Refresh-ReasoningChoices
    Refresh-SearchControls
})
Refresh-ReasoningChoices
Refresh-SearchControls

function Get-SelectedProviderKind {
    switch ($providerPreset.SelectedIndex) {
        1 { 'volcengine' }
        2 { 'deepseek' }
        3 { 'kimi' }
        4 { 'qwen' }
        5 { 'glm' }
        default { 'custom' }
    }
}

function Set-ProfileModelChoices([string] $Kind, [string] $Selected) {
    switch ($Kind) {
        'volcengine' { Set-ModelChoices @('deepseek-v4-flash-260425', 'deepseek-v4-pro-260425', 'doubao-seed-character-260628', 'doubao-seed-2-1-turbo-260628', 'doubao-seed-2-1-pro-260628', 'doubao-seed-2-0-lite-260428') $Selected }
        'deepseek' { Set-ModelChoices @('deepseek-v4-flash', 'deepseek-v4-pro') $Selected }
        'kimi' { Set-ModelChoices @('kimi-k3', 'kimi-k2.6') $Selected }
        'qwen' { Set-ModelChoices @('qwen3.5-flash', 'qwen3.6-flash', 'qwen3.7-plus') $Selected }
        'glm' { Set-ModelChoices @('glm-4.7-flash', 'glm-4.7-flashx', 'glm-5.2') $Selected }
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
    if ($currentProviderKind -eq 'custom') {
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
    if ($wire -notin @('chat_completions', 'responses')) { $wire = 'chat_completions' }
    $wireApi.SelectedItem = $wire
    $script:loadingProfile = $false
    Refresh-ReasoningChoices
    Refresh-SearchControls
}

$providerPreset.Add_SelectedIndexChanged({
    if ($loadingProfile) { return }
    Capture-ProviderProfile
    $selectedKind = Get-SelectedProviderKind
    Load-ProviderProfile $selectedKind
    if ($selectedKind -eq 'volcengine') {
        $searchBackend.SelectedItem = '模型内置'
    } elseif ($selectedKind -in @('deepseek', 'kimi', 'glm') `
        -and [string]$searchBackend.SelectedItem -eq '模型内置') {
        $searchBackend.SelectedItem = '关闭'
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
                return '模型连接正常，但火山方舟账号尚未开通“联网内容插件”。请先在方舟控制台开通该插件后再测试。'
            }
            if ($code -eq 'InvalidEndpointOrModel.NotFound') {
                return "当前模型名称不存在或账号无权访问：$modelName"
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
            throw 'Base URL 必须是完整的 http:// 或 https:// 地址。'
        }
        if ([string]::IsNullOrWhiteSpace($modelName)) { throw '请填写模型名称。' }
        if ([string]::IsNullOrWhiteSpace($testKey)) { throw '请填写 API 密钥。' }

        $isResponses = [string]$wireApi.SelectedItem -eq 'responses'
        $testBuiltInSearch = [string]$searchBackend.SelectedItem -eq '模型内置'
        $endpoint = $base + $(if ($isResponses) { '/responses' } else { '/chat/completions' })
        $payload = if ($isResponses) {
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
        $headers = @{ Authorization = "Bearer $testKey" }
        $body = $payload | ConvertTo-Json -Depth 6 -Compress
        $testTimeout = if ($testBuiltInSearch) { 45 } else { 20 }
        $testResult = Invoke-RestMethod `
            -Uri $endpoint `
            -Method Post `
            -Headers $headers `
            -ContentType 'application/json; charset=utf-8' `
            -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
            -TimeoutSec $testTimeout
        if ($testBuiltInSearch -and $isResponses) {
            $searchUsed = @($testResult.output | Where-Object { $_.type -eq 'web_search_call' }).Count -gt 0
            if (-not $searchUsed) {
                throw '接口返回了普通回复，但没有执行 web_search；当前模型或服务可能不支持模型内置搜索。'
            }
        }
        $successMessage = if ($testBuiltInSearch) {
            '模型连接和联网搜索均成功。'
        } else {
            '模型连接成功。'
        }
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            $successMessage,
            '测试连接',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information)
    } catch {
        $errorMessage = Get-ApiErrorMessage $_
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            "连接失败：$errorMessage",
            '测试连接',
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
            throw 'Base URL 必须是完整的 http:// 或 https:// 地址。'
        }
        if ([string]::IsNullOrWhiteSpace($modelName)) {
            throw '请填写模型名称。'
        }
        if ([string]::IsNullOrWhiteSpace($newKey) -and [string]::IsNullOrWhiteSpace([string]$provider.api_key)) {
            throw '请填写 API 密钥。'
        }
        Capture-ProviderProfile
        Set-JsonProperty $config 'mock_mode' $false
        $selectedKind = Get-SelectedProviderKind
        $script:provider = $profiles[$selectedKind]
        Set-JsonProperty $config 'active_provider' $selectedKind
        Set-JsonProperty $config 'provider' $provider
        Set-JsonProperty $config 'provider_profiles' ([pscustomobject]$profiles)

        $searchMode = [string]$searchBackend.SelectedItem
        $searchBackendValue = switch ($searchMode) {
            '模型内置' { 'provider' }
            'Brave Search' { 'brave' }
            'SearXNG' { 'searxng' }
            default { 'off' }
        }
        if ($searchBackendValue -eq 'provider') {
            $builtInAllowed = ($selectedKind -eq 'qwen') `
                -or ($selectedKind -eq 'volcengine' -and [string]$wireApi.SelectedItem -eq 'responses') `
                -or ($selectedKind -eq 'custom' -and [string]$wireApi.SelectedItem -eq 'responses')
            if (-not $builtInAllowed) {
                throw '当前服务商预设不支持此处的模型内置搜索，请选择 Brave Search 或 SearXNG。'
            }
        }
        if ($searchBackendValue -eq 'brave') {
            $newSearchKey = $searchKey.Text.Trim()
            if ([string]::IsNullOrWhiteSpace($newSearchKey) -and [string]::IsNullOrWhiteSpace($existingSearchKey)) {
                throw '使用 Brave Search 时请填写搜索 API Key。'
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
                throw 'SearXNG 地址必须是完整的 http:// 或 https:// 地址。'
            }
            Set-JsonProperty $webSearch 'base_url' $searchUrl
        }
        Set-JsonProperty $webSearch 'enabled' ($searchBackendValue -ne 'off')
        Set-JsonProperty $webSearch 'backend' $searchBackendValue
        Set-JsonProperty $conversation 'reply_max_chars' ([int]$replyLimit.Value)
        Set-JsonProperty $conversation 'allow_parenthetical_actions' ([bool]$parentheticalActions.Checked)
        }
        Set-JsonProperty $music 'preferred_player' $(if ($musicPreference.SelectedIndex -eq 1) { 'netease' } else { 'qqmusic' })
        Set-JsonProperty $localContext 'enabled' ([bool]$localContextEnabled.Checked)
        Set-JsonProperty $localContext 'time_enabled' ([bool]$localTimeEnabled.Checked)
        Set-JsonProperty $localContext 'media_enabled' ([bool]$localMediaEnabled.Checked)
        Set-JsonProperty $uiPreferences 'Scale' ([double]$chatScale.Value / 100.0)

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
            '配置已保存并立即生效。'
        } else {
            '配置已保存。请重新启动桌宠后使用。'
        }
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            $message,
            '莉莉丝 Mod 设置',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information)
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $form,
            $_.Exception.Message,
            '无法保存配置',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
    }
})

[void]$form.ShowDialog()
if ($null -ne $rewardCode.Image) {
    $rewardCode.Image.Dispose()
}
$form.Dispose()
