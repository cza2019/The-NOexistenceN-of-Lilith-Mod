# v0.2.0 干净电脑验收表

测试包：`The-NOexistenceN-of-Lilith-Mod-v0.2.0-win-x64.zip`

候选 SHA-256：

```text
46a8dc821e9341d4a4322a1cdf3753fde6d1b5dcddd5b04a984ff77ff511497a
```

## 环境

- [ ] Windows 10或11 x64。
- [ ] Steam已合法安装《不存在的莉莉丝》。
- [ ] 游戏目录此前没有BepInEx和本Mod。
- [ ] 系统没有单独安装Node，且没有设置 `LILITH_NODE_PATH`。

## 安装与启动

- [ ] ZIP的SHA-256与上方一致。
- [ ] 解压后 `winhttp.dll` 与 `Lilith.exe` 同级。
- [ ] 从Steam直接启动成功，首次缓存生成结束。
- [ ] 断开网络或屏蔽 `unity.bepinex.dev` 时，首次启动仍可使用包内 `BepInEx/unity-libs/2021.3.45.zip` 完成缓存生成。
- [ ] `LogOutput.log` 中没有 Unity基础库下载失败、`no plugins will be loaded` 或 `UnityEngine.CoreModule` 缺失错误。
- [ ] 没有常驻BepInEx控制台窗口。
- [ ] 任务管理器中的Companion使用包内 `BepInEx/plugins/companion/node.exe`。

## 核心功能

- [ ] 右键角色可以打开“和莉莉丝聊天”和“一起听歌吧”。
- [ ] 中文输入、Enter发送和Shift+Enter换行正常。
- [ ] AI回复同时进入历史并显示在角色上方。
- [ ] 展开历史后滚轮平滑，关闭再打开保留本次游戏内窗口位置。
- [ ] 点击可交互的原生台词会打开对话并追加为莉莉丝首句。
- [ ] 当前会话和长期记忆在重启后恢复。
- [ ] 修改游戏内玩家称呼后，原生称呼与AI称呼同步，旧记忆自动迁移。

## API与联网

- [ ] Mod设置窗口能打开且没有外部PowerShell控制台常驻。
- [ ] 自定义及至少一个官方/方舟预设可以保存和测试连接。
- [ ] API密钥只出现在 `%APPDATA%\LilithAI\config.json`。
- [ ] 未开启搜索时不会声称已取得实时资料。
- [ ] 开启可用搜索后，天气、电影或歌曲问题能够取得实时上下文。

## 音乐

- [ ] QQ音乐完全退出时可以由“一起听歌吧”启动。
- [ ] QQ音乐播放进入循环动作，暂停正常退出。
- [ ] 网易云音乐同样可以启动、播放和暂停。
- [ ] 浏览器视频不会触发音乐动作或媒体对话上下文。
- [ ] 退出游戏不会关闭玩家原本已打开的QQ音乐或网易云音乐。

## 退出、升级与卸载

- [ ] 托盘“再见”和Steam“停止”都能结束游戏、Companion和媒体桥接。
- [ ] Steam不会长期停留在“停止”。
- [ ] 覆盖安装同版本包不会清除API配置、历史或玩家记忆。
- [ ] 按安装文档卸载后，原游戏仍可正常启动。

## 公开前最后检查

- [ ] GitHub仓库已从私有改为公开。
- [ ] 正式Release不是Draft或Prerelease。
- [ ] Mod内“检查更新”显示当前已是最新版本。
- [ ] Release附件包含ZIP和对应 `.sha256.txt`。
