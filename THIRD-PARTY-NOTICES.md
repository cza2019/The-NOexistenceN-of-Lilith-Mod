# 第三方组件声明

Windows 发布包捆绑以下运行组件，玩家无需另行安装。各组件仍受其原许可证约束，
不属于本项目根目录 MIT 许可证的重新授权范围。

## BepInEx 6 IL2CPP

- 用途：随 Unity IL2CPP 游戏启动并加载 Mod 插件。
- 版本：6.0.0-be.769。
- 许可证：GNU Lesser General Public License v2.1。
- 发布包附带：`BepInEx-LICENSE.txt`。

## Unity 2021.3.45 基础类库

- 用途：供 BepInEx在玩家电脑上首次生成 IL2CPP互操作程序集，避免首次启动依赖外部下载。
- 来源：BepInEx Unity基础类库服务所提供的 `2021.3.45.zip`。
- 权利归属：Unity相关名称、程序集和组件的权利归其各自权利人所有。
- 发布包位置：`BepInEx/unity-libs/2021.3.45.zip`；该文件不属于本项目 MIT许可证的重新授权范围。

## Node.js

- 用途：运行本地 Lilith Companion 服务。
- 版本：Windows 发布时固定为 Node.js 24 LTS。
- 许可证：Node.js 许可证及其第三方组件条款。
- 发布包附带：`BepInEx/plugins/companion/node-LICENSE.txt`。

## Microsoft .NET Runtime 与 Windows SDK 组件

- 用途：BepInEx CoreCLR，以及自包含的 Windows 媒体会话桥接与 grounding viewer。
- 许可证：对应 Microsoft .NET、Windows SDK 及第三方组件许可。
- 发布包附带：`DotNet-LICENSE.txt` 与 `DotNet-ThirdPartyNotices.txt`。

## Microsoft Edge WebView2 SDK

- 用途：在独立窗口显示 Gemini Google Search 的 grounded 输出、来源与 Search Suggestions。
- SDK 版本：`Microsoft.Web.WebView2 1.0.4078.44`。
- 运行时：使用玩家系统中的 WebView2 Evergreen Runtime；发布包不携带完整浏览器内核。
- 发布包附带：`BepInEx/plugins/grounding-viewer/WebView2-LICENSE.txt` 与 `WebView2-NOTICE.txt`。

本项目不修改上述组件的许可证。若发布包内随附的完整许可证文本与本摘要存在
差异，以随附文本及组件上游项目的许可证为准。
