# 安装、升级与卸载

[简体中文](installation.md) · [繁體中文](installation.zh-TW.md) · [English](installation.en.md) · [日本語](installation.ja.md)

## 系统要求

- Steam版《不存在的莉莉丝》。
- Windows 10/11 x64。
- Windows系统自带的 PowerShell 5.1，用于 Mod设置窗口。
- 玩家自己的 OpenAI兼容模型服务信息；联网搜索为可选功能。

不需要单独安装 BepInEx、Node.js、.NET、AstrBot或 PowerShell 7。

## 首次安装

1. 完全退出游戏，并确认 Steam按钮已经从“停止”恢复为“开始游戏”。
2. 下载与 Release 标签一致的 Windows x64 ZIP，并核对 SHA-256。
3. 在 Steam库中右键游戏，选择“管理 → 浏览本地文件”。
4. 将 ZIP 内全部文件解压到该目录，而不是新建一层同名文件夹。
5. 确认 `Lilith.exe`、`winhttp.dll` 和 `doorstop_config.ini` 位于同一目录。
6. 从 Steam启动游戏。首次运行会使用包内附带的 Unity基础库在本地生成 BepInEx IL2CPP缓存，耗时可能较长，但不需要连接 `unity.bepinex.dev`。
7. 打开“Mod设置 → 其他”，选择跟随游戏或手动指定 Mod 语言；再按需配置模型服务并点击“测试连接”。

游戏启动后会在后台检查一次 GitHub 最新正式版。已经是最新版或网络不可用时不会弹窗；发现新版时只询问是否打开对应 Release，不会自动下载或替换文件。

## 配置 Gemini

1. 在 Google AI Studio 创建玩家自己的 Gemini API Key，并确认账号所在地区可使用 Gemini API 与 Google Search grounding。
2. 打开“Mod设置 → AI API”，选择“Google Gemini 官方”。默认 Base URL、`gemini-3.6-flash`、Interactions API 与 `low` 思考强度会自动填写。
3. 需要实时资料时选择“模型内置搜索”；否则选择关闭、Brave Search 或 SearXNG。仅当 Gemini 实际调用 Google Search 时才会打开独立结果窗口并产生相应搜索费用。
4. 若 Google API 无法直连，可填写无认证的 HTTP/HTTPS 代理。代理同时用于设置测试和游戏内 Companion 请求。

结果窗口依赖 Microsoft Edge WebView2 Evergreen Runtime。Windows 10/11 通常已安装；缺失时 Mod 会使用默认浏览器打开同一个一次性页面，grounded 输出、来源和 Search Suggestions 不会缺失。`store=false` 不等于所有数据均不保留：免费配额数据使用和 Search grounding 的 30 天相关请求保留仍以 Google 条款为准。

## 跨屏拖动优化（实验性）

该选项默认关闭。它在拖动期间临时处理游戏新版的分段地面碰撞体，优化副屏无任务栏时角色踩空，并适配上下或错位排列的多屏拖动。

1. 在游戏“设置 → 控制”中开启“跨屏拖动”。
2. 打开“Mod设置 → 其他”，开启“优化跨屏拖动（实验性）”。
3. 分别拖到主屏和副屏，特别是上下排列时，确认松手后角色停留在目标屏并按该屏任务栏或屏幕底边触地。

未开启游戏原生“跨屏拖动”时，本 Mod 不会强制角色跨越显示器。

## 升级

1. 退出游戏。
2. 下载新 Release并核对 SHA-256。
3. 将新包覆盖到游戏目录。
4. 从 Steam启动并在“检查更新”页面确认版本。

玩家配置、会话和长期记忆位于 `%APPDATA%\LilithAI`，正常覆盖升级不会删除这些数据。
升级包不会替换游戏存档。
切换 Mod 语言不会翻译或复制已经存在的会话和玩家长期记忆。


更新只替换游戏目录中的程序文件。API 密钥与全部预设配置保存在 `%APPDATA%\LilithAI\config.json`，聊天历史在 `sessions`，长期记忆在 `players`，游戏原生存档也不会被安装包覆盖。不要删除该用户数据目录；需要额外备份时可复制整个目录。

“连接失败时自动尝试备用 DNS”默认开启；只在直连官方接口的 TLS 建连失败时重试。已保存的模型名和手填型号保持原样，新增 DeepSeek 预设默认使用 `deepseek-flash`，火山方舟使用 `deepseek-v4-1-flash-260910`。

## 卸载

如果该游戏目录没有其他 BepInEx Mod，可在退出游戏后删除：

- `winhttp.dll`
- `doorstop_config.ini`
- `.doorstop_version`
- `BepInEx` 目录
- Mod发布包附带的根目录许可证和说明文件

如果还有其他 BepInEx Mod，只删除 `BepInEx/plugins` 中本 Mod对应的 DLL、
`companion`、`character`、`assets`、`media-bridge` 和 `grounding-viewer`，不要删除共用的加载器。

卸载默认保留 `%APPDATA%\LilithAI`。希望彻底清除 API配置、对话和玩家记忆时，
确认备份后再手动删除该目录。

## 常见问题

- **Steam一直显示“停止”**：确认游戏、Node和媒体桥接是否仍在任务管理器中；附上脱敏后的 BepInEx日志报告问题。
- **Mod设置打不开**：检查系统是否存在 `powershell.exe`，以及企业策略或安全软件是否阻止脚本和 WinForms。
- **首次启动很慢**：BepInEx首次生成 IL2CPP缓存属于正常情况。
- **日志提示正在下载 Unity base libraries**：正常发布包已经包含 `BepInEx/unity-libs/2021.3.45.zip`。如果仍尝试下载，请重新下载完整ZIP并核对SHA-256，确认该文件没有被安全软件删除。
- **没有AI回复**：先在 Mod设置中测试 API连接，并确认模型名称、Base URL和接口类型。
- **Gemini 无法连接**：确认地区、API Key、Base URL 和可选代理；代理不能包含用户名或密码，也不能使用 SOCKS 地址。
- **搜索后没有独立窗口**：只有模型实际调用 Google Search 时才会打开；若已调用，检查 WebView2 Runtime、默认浏览器和安全软件是否拦截 `LilithGroundingViewer.exe`。
- **音乐软件没有启动**：检查设置中的优先播放器以及该播放器是否已安装。
- **副屏拖不过去、松手后跳回上屏或角色没有触底**：确认游戏“设置 → 控制”中的“跨屏拖动”和 Mod设置中的实验性优化均已开启；关闭实验性选项会恢复原游戏行为，垂直跨屏可能无法使用。
- **游戏“默认音乐播放器”无法启动应用**：这是游戏更新后新增的原生功能，Mod 不拦截该调用；可先使用 Mod 的“一起听歌吧”入口，并向游戏官方反馈。
- **安全软件告警**：只从项目正式 Release下载并核对 SHA-256，不要从第三方重新打包站获取。
