# 《不存在的莉莉丝》Mod v0.2.0

首个公开 Windows版本，为桌宠增加莉莉丝角色AI聊天、长期记忆、原作剧情检索、
外部音乐联动和本机状态感知。

## 下载

下载以下两个附件：

- `The-NOexistenceN-of-Lilith-Mod-v0.2.0-win-x64.zip`
- `The-NOexistenceN-of-Lilith-Mod-v0.2.0-win-x64.zip.sha256.txt`

ZIP SHA-256：

```text
2565f40e7f443475a45eef4c40508fc2829e241cef621cbc3f7a6726015e93dc
```

## 安装

1. 完全退出游戏。
2. 将ZIP内全部内容解压到游戏根目录，使 `winhttp.dll` 与 `Lilith.exe` 同级。
3. 从Steam正常启动游戏。
4. 在“Mod设置”中填写自己的模型服务信息。

包内已包含BepInEx 6 IL2CPP、Node.js 24 LTS和自包含媒体桥接，不需要单独安装。

## 主要功能

- 符合原作正史与语气的莉莉丝AI聊天。
- 当前会话、玩家长期记忆和原作剧情本地检索。
- 游戏内聊天窗口、头顶回复与原生气泡点击入口。
- DeepSeek、Kimi、Qwen、GLM、火山方舟和自定义OpenAI兼容接口。
- 可选联网搜索、本机时间与当前音乐上下文。
- QQ音乐、网易云音乐启动及游戏原生音乐动作联动。
- 游戏内玩家称呼修改与长期记忆迁移。
- GitHub正式版检查和作者赞赏页面。

## 已知限制

- 仅支持Windows 10/11 x64。
- Mod设置依赖系统自带的Windows PowerShell 5.1。
- 检查更新只会打开下载页，不会自动覆盖文件。
- 联网搜索能力取决于玩家配置的模型或搜索服务。

完整变化参见仓库中的 `CHANGELOG.md`，安装、升级和卸载说明参见 `docs/installation.md`。
