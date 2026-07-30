# 《不存在的莉莉丝》Mod v0.3.0

v0.3.0 在 v0.2.0 中文功能基线上加入 Gemini 原生接口、Google Search grounding、完整四语本地化，并将外部音乐播放器扩展到五个平台。

## 下载

- `The-NOexistenceN-of-Lilith-Mod-v0.3.0-win-x64.zip`
- `The-NOexistenceN-of-Lilith-Mod-v0.3.0-win-x64.zip.sha256.txt`

## 新增

- 新增 Google Gemini 官方预设：原生 Interactions API、稳定版 `gemini-3.6-flash`、默认 `low` 思考强度与 `store=false`。
- Gemini 可自行判断并调用 Google Search。普通回复和来源显示在聊天中；实际搜索时另开一次性结果窗口，同时展示 grounded 原始输出、引用和 Google Search Suggestions。
- 结果窗口使用 WebView2 `1.0.4078.44`，同一 Companion 只保留一个窗口；缺少 Evergreen Runtime 时回退系统默认浏览器。
- 新增可选无认证 HTTP/HTTPS 代理；设置测试与游戏内请求共用相同优先级和 loopback 绕过规则。
- 支持 QQ音乐、网易云音乐、Spotify、Apple Music 和 Amazon Music 作为优先播放器；首选不可用时自动尝试其他已安装播放器。
- Spotify、Apple Music 和 Amazon Music 同时支持桌面版与 Windows Store 版发现和启动。
- 游戏“放点音乐”菜单保留“使用优先播放器”和“本地音乐”，五个平台统一通过设置选择。
- 五个平台的播放、暂停、曲名和歌手继续通过 SMTC 驱动莉莉丝的音乐动作与本机媒体上下文。
- 简体中文、繁體中文、English、日本語共用一个安装包。
- 默认跟随桌宠游戏语言，亦可在“Mod 设置 → 其他”中手动覆盖。
- 聊天窗口、角色菜单、设置、记忆编辑、更新页、提示和错误使用共享静态语言资源。
- 四套莉莉丝角色卡与结构化剧情索引共享相同条目 ID、关联关系和来源编号。
- 本机时间、当前音乐、剧情资料和联网资料上下文均按当前 Mod 语言构造。
- 英文头顶回复优先按单词边界折行，中日韩按约 15 个全角字符的视觉宽度折行。

## 行为边界

- 不修改游戏本体的菜单、随机台词或互动文本；这些继续使用官方本地化。
- 从切换语言后的下一条 AI 回复开始生效；已有历史、玩家记忆和头顶台词不自动翻译。
- 所有翻译随包静态分发，运行时不调用 AI 翻译，也不产生额外 API 消耗。
- 当前仍仅发布 Windows 10/11 x64 安装包。
- Gemini 的跨轮上下文仅使用本机可见聊天记录，thought、签名、搜索步骤和 Search Suggestions 不落盘。`store=false` 不改变免费配额数据用途或 Google Search grounding 相关请求保留 30 天的条款；实际搜索可能计费。
