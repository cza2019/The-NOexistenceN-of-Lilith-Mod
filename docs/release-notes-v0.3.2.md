# 《不存在的莉莉丝》Mod v0.3.2

v0.3.2 在现有手动“检查更新”基础上加入游戏启动自动检查。它只负责提示玩家前往 GitHub，不会下载、执行或覆盖任何文件。

## 下载

- `The-NOexistenceN-of-Lilith-Mod-v0.3.2-win-x64.zip`
- `The-NOexistenceN-of-Lilith-Mod-v0.3.2-win-x64.zip.sha256.txt`

## 新增

- 每次游戏启动约 5 秒后，通过 Companion 在后台查询一次 GitHub 最新正式 Release。
- 只有发现更高版本时才显示简体中文、繁體中文、English 或日本語确认框；选择“是”打开对应 Release，选择“否”关闭提示。
- 查询使用现有 HTTP/HTTPS 代理优先级，并在本次游戏进程内合并重复请求。

## 安全与兼容性

- 浏览器只允许打开本项目位于 `https://github.com/` 的正式 Release 地址。
- 当前已是最新版、GitHub 无法连接、超时或限流时不弹窗，也不影响游戏和其他 Mod 功能。
- 不保存已提示版本或检查时间；尚未升级时，下次启动会重新检查。
- 不修改 Gemini、Google Search、模型接口、聊天历史、玩家记忆或音乐播放器配置。

## 升级

完全退出游戏后，将新 ZIP 的全部内容覆盖到游戏目录。`%APPDATA%\LilithAI` 中的 API 配置、聊天历史和玩家长期记忆不会被安装包覆盖。
