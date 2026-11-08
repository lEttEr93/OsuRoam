# tosu 启动失败：EPERM: mkdir %USERPROFILE%\.cache\pkg\…

## 症状

Roam 里「tosu 启动失败：tosu 启动后提前退出，退出码 1」，日志里是这样一串：

```
正在启动 tosu：D:\…\bin\Debug\net8.0-windows\tosu.exe
tosu stderr: pkg/prelude/bootstrap.js:1756
tosu stderr: Error: EPERM: operation not permitted, mkdir
             'C:\Users\梨头\.cache\pkg\05768fcd047bc9d53602c9d29b9fef6652e40472d8f14ded9044ee45f55552ab'
tosu stderr:     at Object.mkdirSync (pkg/prelude/bootstrap.js:1519:33)
由 Roam 启动的 tosu 已退出，退出码 1
```

这跟 Roam 无关，也跟 tosu 版本无关 —— 换哪份 `tosu.exe` 都一样。

## 根因

tosu 是 [pkg](https://github.com/vercel/pkg) 打的 Node 单文件。启动时 pkg 会把原生模块
（`.node`）解到 `%USERPROFILE%\.cache\pkg\<hash>\`，**这一步需要能在用户目录里建文件夹**。

这台机器的 `C:\Users\梨头` 根目录只剩一条 ACE：

```
WIN-HOHHTPSNOD4\CodexSandboxUsers:(RA)
```

也就是除了「读」，其他权限都没了，于是 `mkdir` 直接 EPERM，pkg 抛异常，tosu 以 1 退出。
旧版 tosu（4.26.1）之所以"能跑"，只是因为它那个 hash 目录**早就存在**，不需要再建。

## 修法

Roam 启动 tosu 时把它的用户目录改到 Roam 自己的 `data\tosu-home`：

```csharp
string tosuHome = Path.Combine(StoragePaths.Root, "tosu-home");
Directory.CreateDirectory(tosuHome);
process.StartInfo.Environment["USERPROFILE"] = tosuHome;
process.StartInfo.Environment["HOME"] = tosuHome;
```

只影响这一个子进程：

- pkg 缓存落到 `data\tosu-home\.cache\pkg\…`（首次约 31 MB，按 tosu 版本各一份）；
- `tosu.env` 仍然在 `tosu.exe` 旁边，配置判定逻辑不变；
- tosu 找 osu! 靠进程名和内存，不看用户目录，不受影响；
- 目录建不出来时自动退回默认用户目录（不会因此起不来）。

日志会多一行 `tosu 的用户目录：…\data\tosu-home`，之后正常是
`tosu WebSocket 已就绪，PID=…`。

## 顺带确认的另一件事

同一台机器上 `%TEMP%`、`%APPDATA%\NuGet\v3-cache` 之类也会偶发"拒绝访问"，
根因是同一条被破坏的 ACE。要用命令行工具（nuget / MSBuild / git credential）的话，
可以自己修一下（**需要管理员 cmd**，我没动它）：

```bat
icacls "C:\Users\梨头" /grant "梨头:(OI)(CI)F" /T
```

不修也不影响 Roam：它自己的数据都在 exe 旁边的 `data\`，脚本也把 TMP 指到了 `bin\tmp`。

## 第二种：tosu 和 osu! 版本不兼容（游戏在导入时崩）

症状：Roam 一导入，`osu!.exe` 在 7 秒左右退出，退出码 `0xE0434352` 或 `0xC0000409`，
崩溃模块是 `%APPDATA%\osu\AuthNative.dll`（osu! 自己的签名组件）；同一台机器上
**手动双击启动游戏却完全正常**。

排查结论（2026-11-08 现场）：

| tosu | 会不会把游戏搞崩 |
|---|---|
| `osu-roam-v1.2.4-portable\tosu.exe`（4.26.1） | 不崩，导入正常 |
| 发布包里随带的 `tosu.exe`（4.26.3） | 崩，7 秒 `0xC0000409` |

也就是说**不是启动参数、不是环境变量、不是工作目录**，而是 tosu 挂上去之后
游戏才死的。同一个 osu!、同一份 `.osz`、同样的命令行，从 cmd 里跑正常，
那是因为当时那个 tosu 已经坏在端口上没在读内存。

为什么发布包"看起来没问题"：它的 `settings.json` 里
`TosuExecutablePath` 指向老的 1.2.4 那份 tosu，根本没用随包的 4.26.3。

应对：

- 设置里把「tosu 路径」指到另一份 tosu（现场指回 4.26.1 那份就正常）；
- 不要清空这条路径 —— 清空后 Roam 会用 `OsuRoam.exe` 同目录随包的 tosu；
- 导入崩溃时日志里会多一行提示，指向这个可能。
