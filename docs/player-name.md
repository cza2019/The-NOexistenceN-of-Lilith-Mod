# 玩家称呼集成

桌宠游戏把玩家称呼保存在运行时 `Archive.Instance.playerName` 中。Mod每次发送
模型请求时读取该值，不从原作视觉小说或机器绝对路径推断玩家身份。

## 读取与保存

- 读取：`Archive.Instance.playerName`。
- 规范化：游戏原生 `TraySettingModel.NormalizePlayerName` 与 `PlayerNameRule`。
- 保存：Unity主线程调用 `TraySettingModel.SavePlayerName(string)`。
- 原设置页正在打开时，通过 `TraySettingView.SetNameWithoutNotify` 刷新显示。

游戏存档使用加密与完整性校验，Mod不得直接解析或修改
`lilith_save_data`。所有改名必须通过游戏原生模型接口完成。

## 外部设置窗口桥接

PowerShell设置窗口不能直接调用 Unity对象，因此双方通过
`%APPDATA%\LilithAI` 下的会话级JSON请求通信：

- 插件写出当前进程、会话标识和真实玩家称呼。
- 设置窗口先请求原生规则预校验，再提交应用请求。
- 插件只接受当前游戏会话、当前旧称呼和30秒内创建的请求。
- 游戏未运行、会话变化或名称被拒绝时不修改存档。

称呼改变后，设置窗口将旧称呼对应的长期记忆迁移到游戏最终采用的新称呼；
目标档案已有内容时按项目合并，当前编辑值优先。合并超出记忆限制时整单拒绝，
不会截断记忆，也不会调用AI进行校验。

原作《不/存在的你，和我》拥有独立存档；桌宠 Mod不读取或修改原作流程进度。
