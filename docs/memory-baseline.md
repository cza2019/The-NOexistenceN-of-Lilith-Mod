# 内存基准与长时间回归

使用 `scripts/measure-memory.ps1` 在相同分辨率、角色状态和前台程序条件下分别采样：

1. `vanilla`：暂时禁用 BepInEx 加载入口后启动游戏。
2. `bepinex`：启用 BepInEx，但暂时移走 `LilithMod.dll` 后启动游戏。
3. `full`：恢复完整 Mod 后启动游戏。

脚本只读取进程数据，不会自动改动游戏目录或启停游戏。每种模式预热 5 分钟，再采样 30 分钟：

```powershell
./scripts/measure-memory.ps1 -Mode vanilla
./scripts/measure-memory.ps1 -Mode bepinex
./scripts/measure-memory.ps1 -Mode full
```

也可使用 `-GamePid` 指定游戏进程。原始 CSV 和摘要 JSON 默认写入 `artifacts/memory/`，该目录不进入版本控制。

摘要分别列出游戏、Companion、媒体桥接及其他子进程的私有字节、私有工作集、峰值、句柄和线程漂移。任务管理器“内存”列接近脚本的 `PrivateWorkingSetMB`；`PrivateMB` 是包含未驻留提交页的私有字节，二者不能直接比较。验收时比较完整 Mod 与仅 BepInEx，而不是只比较任务管理器中的绝对数值。

目标：

- 完整 Mod 相对仅 BepInEx 的私有内存增量不超过 100 MB。
- 30 分钟内游戏私有内存漂移不超过 25 MB。
- 句柄漂移不超过 50，线程漂移不超过 5。
- 压力操作结束并静置 5 分钟后，私有内存回到测试前 40 MB 范围内。

## 开发诊断

在 `BepInEx/plugins/` 创建空文件 `LilithMod.memory-diagnostics` 后重启游戏，可启用每分钟一次的开发诊断日志。日志包含：

- 游戏私有内存和工作集；
- CoreCLR 托管堆；
- Unity 已分配/保留内存；
- 句柄、线程；
- 媒体轮询当前并发、历史最大并发和完成计数。

该标记默认不存在，普通用户不会产生周期日志。测试完成后删除标记并重启游戏。
