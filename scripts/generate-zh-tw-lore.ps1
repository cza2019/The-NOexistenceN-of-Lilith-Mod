param([string] $RepoRoot = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName Microsoft.VisualBasic

$repo = if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($RepoRoot)
}
$source = Join-Path $repo 'character\lore\index.json'
$target = Join-Path $repo 'character\lore\index.zh-TW.json'
$document = Get-Content -LiteralPath $source -Raw -Encoding UTF8 | ConvertFrom-Json

$replacements = [ordered]@{
    '制作' = '製作'
    '以后' = '以後'
    '之后' = '之後'
    '背后' = '背後'
    '后来' = '後來'
    '最后' = '最後'
    '后续' = '後續'
    '身后' = '身後'
    '里面' = '裡面'
    '这里' = '這裡'
    '那里' = '那裡'
    '游戏' = '遊戲'
    '程序' = '程式'
    '默认' = '預設'
    '设置' = '設定'
    '台词' = '臺詞'
    '附录' = '附錄'
    '文件' = '檔案'
    '数据' = '資料'
    '信息' = '資訊'
    '软件' = '軟體'
    '网络' = '網路'
    '视频' = '影片'
    '分支' = '路線分支'
    '《不/存在的你，和我》' = '《不／存在的你，和我》'
    '《不/存在的逐梦者：银葱之森》' = '《不／存在的逐夢者：銀蔥之森》'
    'Huis-clos演唱会' = 'Huis-clos 演唱會'
}

function Convert-Text([string] $Value) {
    if ($null -eq $Value) { return $Value }
    $converted = [Microsoft.VisualBasic.Strings]::StrConv(
        $Value,
        [Microsoft.VisualBasic.VbStrConv]::TraditionalChinese,
        0)
    foreach ($entry in $replacements.GetEnumerator()) {
        $converted = $converted.Replace([string]$entry.Key, [string]$entry.Value)
    }
    return $converted
}

$document.canon_context = Convert-Text ([string]$document.canon_context)

foreach ($entry in $document.entries) {
    $entry.title = Convert-Text ([string]$entry.title)
    $entry.summary = Convert-Text ([string]$entry.summary)
    $entry.aliases = @($entry.aliases | ForEach-Object { Convert-Text ([string]$_) })
    $entry.keywords = @($entry.keywords | ForEach-Object { Convert-Text ([string]$_) })
    $entry.facts = @($entry.facts | ForEach-Object { Convert-Text ([string]$_) })
    $entry.short_quotes = @($entry.short_quotes | ForEach-Object { Convert-Text ([string]$_) })
}
$document | Add-Member -MemberType NoteProperty -Name locale -Value 'zh-TW' -Force
[IO.File]::WriteAllText(
    $target,
    (($document | ConvertTo-Json -Depth 12) + [Environment]::NewLine),
    (New-Object Text.UTF8Encoding($false)))
