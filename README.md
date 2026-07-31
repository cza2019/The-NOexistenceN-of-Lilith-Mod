# 《不存在的莉莉丝》Mod

[简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md)

`The NOexistenceN of Lilith Mod` 为 Steam 桌宠游戏《不存在的莉莉丝》增加
生成式 AI 聊天、角色长期记忆、原作剧情检索、本机状态感知和外部音乐联动。
普通玩家只需配置自己的服务模型，或仅使用音乐同步功能。

> 当前版本：0.3.2 · Windows 10/11 x64

## 主要功能

- 以《不/存在的你，和我》结局 1（即 Happy Ending）之后的莉莉丝为固定角色背景。
- 一个安装包完整支持简体中文、繁體中文、English 和日本語；默认跟随游戏，也可在 Mod 设置中覆盖。
- 支持 Gemini 原生 Interactions API、OpenAI兼容接口及 DeepSeek、Kimi、Qwen、GLM、火山方舟等预设。
- 本地保存当前会话和莉莉丝对玩家的长期记忆，支持手动查看、修改和迁移。
- 仅在相关问题中读取本机时间和白名单音乐软件的曲名、歌手及播放状态。
- 可选 Gemini Google Search、其他模型内置搜索、Brave Search 或 SearXNG；搜索失败不影响普通聊天。
- 游戏内 Unity聊天窗口、头顶文字回复、对话历史和角色气泡点击入口。
- QQ音乐、网易云音乐等播放器冷启动与游戏原生音乐动作联动；排除浏览器视频。
- 默认关闭的实验性跨屏拖动优化：改善副屏无任务栏时角色踩空，适配上下或错位排列的多屏拖动；需先开启游戏原生“跨屏拖动”。
- 在 Mod设置中修改游戏内玩家称呼，并无损迁移对应长期记忆。
- 每次游戏启动在后台检查一次 GitHub 最新正式版；仅在发现更新时询问是否打开 Release，不自动替换文件。

## 普通玩家安装

1. 在 GitHub Releases 下载 `The-NOexistenceN-of-Lilith-Mod-v0.3.2-win-x64.zip`。
2. 完全退出《不存在的莉莉丝》。
3. 将 ZIP 内全部内容解压到游戏根目录，使 `winhttp.dll` 与 `Lilith.exe` 同级。
4. 从 Steam正常启动游戏。首次加载 BepInEx可能比平时慢。
5. 右键莉莉丝或托盘打开“Mod设置”，填写自己的模型 API 信息。

发布包已经包含 BepInEx 6 IL2CPP、Unity 2021.3.45基础库、Node.js 24 LTS和自包含媒体桥接，首次生成 IL2CPP缓存不需要外部下载。玩家不需要
安装或固定 Node、BepInEx、.NET或 PowerShell 7；设置窗口使用 Windows 10/11
系统自带的 Windows PowerShell 5.1。

详细步骤参见 [安装、升级与卸载](docs/installation.md)。

## Gemini 与 Google Search

在“Mod设置 → AI API”选择“Google Gemini 官方”，填写玩家自己的 Gemini API Key。默认使用稳定版 `gemini-3.6-flash`、`low` 思考强度和原生 Interactions API；也可从列表选择其他受支持模型或手工输入模型名。请求始终使用 `store=false`，不会创建 Google 托管的多轮会话链；跨轮上下文只来自本机保存的可见 user/assistant 对话。

选择“模型内置搜索”后，Gemini 会自行判断是否调用 Google Search。普通回复和来源仍显示在游戏聊天中；只有实际产生搜索调用时，Mod 才自动打开独立结果窗口，同时展示本轮 grounded 原始输出、来源和 Google Search Suggestions。该一次性页面只在内存保留很短时间，首次读取即销毁，不写入聊天历史。缺少 WebView2 Evergreen Runtime 时会改用默认浏览器打开同一页面。

Google 服务不可直连的网络可填写无认证的 `http://` 或 `https://` 代理。优先级为设置中的代理、`HTTPS_PROXY/HTTP_PROXY`、直连；`localhost/127.0.0.1` 始终绕过代理。

## API 与隐私

- API密钥只保存在玩家本机 `%APPDATA%\LilithAI\config.json`。
- 玩家发送的对话及启用的临时上下文会发送给玩家自行配置的模型服务商。
- 本机状态默认只包含时间和白名单音乐软件媒体信息，且仅在话题相关时附加。
- 浏览器视频、剪贴板、活动窗口、文件、电池和网络状态不会被读取。
- 使用付费模型产生的费用由模型服务商向玩家收取，请自行设置额度。
- `store=false` 只表示不保存 Interactions 会话链。Gemini 免费配额的输入输出可能用于改进服务；Google Search grounding 相关请求数据仍按 Google 条款保留 30 天，Gemini 3 按模型实际执行的搜索查询计费。
- 发布包和源码仓库不包含作者或测试者的 API密钥、聊天历史、玩家记忆或存档。

## 更新与故障排查

- 游戏启动约 5 秒后会在后台检查一次 GitHub 最新正式 Release；只有发现新版本才询问是否打开下载页。Mod 设置中仍可手动检查，网络不可用时不弹窗且不影响其他功能。
- 更新前退出游戏，然后用新版本覆盖旧文件；`%APPDATA%\LilithAI` 中的配置和记忆不会被覆盖。
- BepInEx注入、`winhttp.dll` 或捆绑的 `node.exe` 可能被少数安全软件误报；请先核对 Release提供的 SHA-256。
- 日志位于游戏目录 `BepInEx/LogOutput.log`，报告问题时请先删除其中可能包含的私人对话或路径。

## 开发与构建

源码不包含游戏程序集、BepInEx发行二进制或 Node运行时。插件开发构建需要已安装
游戏生成的 BepInEx IL2CPP interop程序集；发布包由维护者在授权的本地环境生成。

```powershell
npm test --prefix companion
powershell -NoProfile -ExecutionPolicy Bypass -File tests/player-name-settings.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-bundle.ps1
```

可独立运行的纯逻辑测试位于 `tests/`；GitHub Actions不会上传或依赖游戏私有程序集。

## 授权与素材

- 原创代码使用 [MIT License](LICENSE)。
- 游戏角色素材和赞赏码不适用 MIT代码许可，详见 [素材声明](ASSET-NOTICE.md)。
- BepInEx、Unity基础类库、Node.js、.NET等组件使用各自条款，详见 [第三方组件声明](THIRD-PARTY-NOTICES.md)。
- 本项目不包含游戏本体，使用者必须通过 Steam合法安装《不存在的莉莉丝》。

macOS适配、设置窗口脱离 PowerShell和自动覆盖更新不属于 0.3.2 范围。
