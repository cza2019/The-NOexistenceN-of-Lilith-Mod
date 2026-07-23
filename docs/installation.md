# 安装、升级与卸载

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
7. 打开“Mod设置”，配置模型服务并点击“测试连接”。

## 升级

1. 退出游戏。
2. 下载新 Release并核对 SHA-256。
3. 将新包覆盖到游戏目录。
4. 从 Steam启动并在“检查更新”页面确认版本。

玩家配置、会话和长期记忆位于 `%APPDATA%\LilithAI`，正常覆盖升级不会删除这些数据。
升级包不会替换游戏存档。

## 卸载

如果该游戏目录没有其他 BepInEx Mod，可在退出游戏后删除：

- `winhttp.dll`
- `doorstop_config.ini`
- `.doorstop_version`
- `BepInEx` 目录
- Mod发布包附带的根目录许可证和说明文件

如果还有其他 BepInEx Mod，只删除 `BepInEx/plugins` 中本 Mod对应的 DLL、
`companion`、`character`、`assets` 和 `media-bridge`，不要删除共用的加载器。

卸载默认保留 `%APPDATA%\LilithAI`。希望彻底清除 API配置、对话和玩家记忆时，
确认备份后再手动删除该目录。

## 常见问题

- **Steam一直显示“停止”**：确认游戏、Node和媒体桥接是否仍在任务管理器中；附上脱敏后的 BepInEx日志报告问题。
- **Mod设置打不开**：检查系统是否存在 `powershell.exe`，以及企业策略或安全软件是否阻止脚本和 WinForms。
- **首次启动很慢**：BepInEx首次生成 IL2CPP缓存属于正常情况。
- **日志提示正在下载 Unity base libraries**：正常发布包已经包含 `BepInEx/unity-libs/2021.3.45.zip`。如果仍尝试下载，请重新下载完整ZIP并核对SHA-256，确认该文件没有被安全软件删除。
- **没有AI回复**：先在 Mod设置中测试 API连接，并确认模型名称、Base URL和接口类型。
- **音乐软件没有启动**：检查设置中的优先播放器以及该播放器是否已安装。
- **安全软件告警**：只从项目正式 Release下载并核对 SHA-256，不要从第三方重新打包站获取。
