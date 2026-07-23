# 维护者发布清单

## 发布边界

- Release只发布 Mod补丁，不包含游戏本体、游戏程序集、存档或完整原作文案。
- `vendor`、游戏生成的 `BepInEx/interop`、开发工具和本机配置不进入 Git仓库。
- Windows ZIP捆绑干净的 BepInEx 6 IL2CPP、经过校验的 Unity 2021.3.45基础类库、Node.js 24 LTS和自包含媒体桥接。
- 玩家不需要安装Node、BepInEx、.NET、AstrBot或PowerShell 7。
- 公开前确认头像素材授权，并保留 `ASSET-NOTICE.md`。

## 版本准备

1. 同步 `LilithModPlugin.PluginVersion`、`companion/package.json` 和
   `companion/release-info.json`。
2. `release-info.json` 的 `github_repository` 必须是最终 `owner/repository`。
3. 更新 `CHANGELOG.md`，并确认 Release标签为 `v<version>`。
4. 首版更新检查只读取最新正式Release，因此不要把唯一可用版本标记为Prerelease。

## 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-bundle.ps1 `
  -GameDir '完整游戏目录' `
  -NodePath 'Node 24 LTS 的 node.exe'
```

脚本会编译插件和媒体桥接，生成：

- `dist/The-NOexistenceN-of-Lilith-Mod-v<version>-win-x64/`
- 对应 ZIP。
- ZIP的 `.sha256.txt`。
- 包内 `RELEASE-MANIFEST.json` 和逐文件 `MANIFEST.sha256`。

脚本发现测试文件、PDB、日志、本机配置、API Key或机器绝对路径时必须失败。

## 验收

- 在干净的 Steam游戏目录安装 ZIP，不预装BepInEx或Node。
- 从Steam直接启动，确认首次缓存生成完成且不弹常驻控制台；断开外网后重复一次全新目录验收，确认不会尝试下载 Unity基础类库。
- 验证聊天、历史、气泡点击、长期记忆、玩家改名、API设置和检查更新。
- 验证QQ音乐/网易云音乐启动、播放、暂停、切歌及浏览器排除。
- 退出后确认游戏、Node和媒体桥接进程全部结束，玩家音乐软件不被关闭。
- 覆盖升级后 `%APPDATA%\LilithAI` 中的配置、历史和记忆保持不变。

## GitHub发布

1. 私有仓库完成候选验证后改为公开。
2. 推送最终提交和 `v<version>` 标签。
3. 创建正式Release，上传ZIP和 `.sha256.txt`。
4. Release说明以中文为主，包含系统要求、安装方式、已知限制和更新日志。
5. 发布后从Mod设置点击“检查更新”，确认显示当前已是最新版本。

如果 `gh release upload` 在代理环境下上传大文件时停滞，可使用不保存令牌的流式上传脚本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/upload-release-asset.ps1 `
  -Repository 'owner/repository' -Tag 'v0.2.0' -FilePath '完整ZIP路径' `
  -Proxy 'http://127.0.0.1:7897'
```

脚本从 GitHub CLI系统凭据库临时读取认证信息，通过标准输入交给 `curl`，不会把令牌写入项目、临时文件或命令行参数。
