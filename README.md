# osu! Roam v1.4.0

osu! 谱面漫游推荐工具。Windows / WPF / .NET 8。

通过 osu! Web API 读取玩家资料和成绩样本，建立「星级 / BPM / 时长 / 谱师」偏好画像，再按星级区间、年份、时长、简易筛选和 tag 生成一个有上限的推荐池。配合 [tosu](https://github.com/KotRikD/tosu) 可以做到 **开局预下载下一首、结算后自动导入下一首** 的连续漫游。

不需要密码，只需要玩家名 + 自己申请的 OAuth Client ID / Secret。

---

## 推荐是怎么算出来的

1. **画像**：拉当前模式的 `recent` + `best` 成绩（mania 会按所选键数过滤），算出星级 62 分位（±0.45 作为建议区间）、BPM 中位数、时长中位数、谱师出现频率、近期玩过的谱面集合。
2. **硬过滤**（任一不满足直接出局）：模式 / mania 键数、星级区间、时长区间、年份区间、简易筛选的「严格」档、排除 tag 命中、推荐 tag 一个都不命中、已推荐或近期玩过的谱面。
3. **打分**：星级越贴近区间中心越好（相对权重 62）、BPM 接近偏好（15）、时长接近偏好（10）、谱师匹配（13）；再叠加探索度随机项、tag 命中比例、简易筛选偏好命中率、`ranked` / `loved` 状态优先级，以及 mania 模式的**键型贴合度**。
4. **排序**：严格筛选命中项数 → 状态优先级 → 综合分数，取前 N 个进推荐池。

候选来源每次漫游会在 6 种搜索排序之间轮换（最新上架 / 评分高 / 最早上架 / 热门 / 收藏多 / 难度高），每轮两种排序各取 4 页，合计最多 8 页约 400 个 beatmapset。这样下一轮看到的候选和这一轮不一样，不容易反复推荐同一批谱面。

---

## 历史推荐

标题栏的「历史」按钮打开历史推荐窗口，列出最近推荐过的 **50 首**谱面，卡片形态与推荐池一致：封面、曲名、难度、星级 / BPM / 时长、推荐理由和推荐时间。每张卡片下方的 **Tags** 可以展开看这首谱面的全部 tag，**双击任意 tag 直接加入推荐 tag**，已命中的会高亮。

历史记录存在 `%LOCALAPPDATA%\OsuRoam\history.json`，同一张谱面再次被推荐时会移到最前。

## 键型（mania）

mania 模式下会用键型提高推荐针对性。分类体系移植自 [osumania_map_analyser](https://github.com/LeoBlackMT/osumania_map_analyser)（MIT，其 RC 键型算法源自 [Interlude](https://github.com/YAVSRG/YAVSRG)）：

- 6 个核心型：`Stream 流` / `Chordstream 和弦流` / `Jacks 叠键` / `Coordination 协调` / `Density 密度` / `Wildcard 变速杂项`
- 4K 子型：`Rolls` `Trills` `Minitrills` `Handstream` `Split Trill` `Jumptrill` `Jumpstream` `Longjacks` `Quadstream` `Gluts` `Chordjacks` `Minijacks` `Column Lock` `Release` `Shield` `Inverse` `JS Density` `HS Density` `Jacky WC` `Speedy WC`
- 模式标签：`RC`（长条占比 ≤15%）、`LN`（≥90%）、`Mix`、`HB`（长条与普通键混排）

工作方式：

1. 推荐池里的谱面被下载后，直接从 `.osz` 里读 `.osu` 解析键型，结果存进键型库 `%LOCALAPPDATA%\OsuRoam\patterns.json`；推荐池卡片上的推荐理由会附带识别出的分类（如 `Jumpstream Tech`）。
2. 用你 recent + best 里同键数的成绩，取这些谱面的键型平均，得到**键型偏好向量**。
3. 排序时按候选键型与偏好向量的余弦贴合度加分，权重由 `PatternWeight` 控制（默认 0.12）。
4. 还没下载过的候选谱面拿不到 `.osu`，先用 tag 粗略推断（`ln` / `rice` / `jack` / `chordjack` / `handstream` / `tech` / `sv` 等），真实键型入库后自动接管。

---

## 快速使用

1. 解压 `osu-roam-v1.2.5-portable.zip`（从 [Releases](https://github.com/lEttEr93/OsuRoam/releases/latest) 下载；自包含 .NET 运行时，不用另装 .NET，包内已带 `tosu.exe`）。
2. 双击 `OsuRoam.exe`。
3. 在「账号与连接」填 OAuth Client ID、Client Secret 和玩家名。
4. 选游戏模式（osu! / taiko / catch / mania 1K–10K）。
5. 拖星级双滑块，或点「建议星级」让它按成绩算一次。
6. 点「开始漫游」。

## OAuth 凭据

- 在 [osu! 账号设置 → OAuth](https://osu.ppy.sh/home/account/edit#oauth) 新建一个应用，拿到 **Client ID** 和 **Client Secret**。
- 走的是 `client_credentials` 流程，`scope=public`，只能读公开数据。
- Client Secret 用 Windows 用户凭据（DPAPI）加密后存在本地配置里，**换 Windows 用户或换机器要重填**。
- 需要能访问 `osu.ppy.sh`。

## 推荐范围

- **难度星级**：双滑块，0–11；拉到「∞」表示不设上限。
- **建议星级**：按最近成绩样本算一次，把 `center ± 0.45` 写进当前模式 / 键数的区间。**开始漫游不会覆盖手动设置**；手动拖动后会关掉该区间的自动标记。
- 星级区间按 `osu` / `taiko` / `catch` / `mania-1k` … `mania-10k` **共 13 组分别保存**。
- **最早 / 最晚年份**（默认 2007–当前年）、**最短 / 最长秒数**（默认 45–360）。
- **探索度**（默认 0.45，0–1）：越高越偏向没听过的谱面（随机发现项权重上升、熟悉度权重下降）；越低越贴近你常打的谱师 / BPM / 时长。
- **推荐池大小**（默认 5）、**排除近期玩过**（默认开）。

## 简易筛选（三态）

「分类（状态）/ 流派 / 语言」每项都是三态复选框：

| 状态 | 含义 |
|---|---|
| 勾选 | **偏好** —— 命中加分，不命中也不排除 |
| 中间态 | **严格筛选** —— 不匹配直接排除 |
| 未勾 | 不限 |

默认只偏好 `ranked` / `approved` / `qualified` / `loved`，流派和语言不限。

## Tag 筛选

Tag 池内置预设曲风、mania 键型特征（含 `米` `面` `叠` `技` `乱` `切`）和常见多词组合，并会从 API 搜索结果里持续收集新 tag，**永久保存到本地配置**。

- **推荐 tag**：设置后**必须至少命中其中一个**才会入选（不是纯加分），命中比例越高排序越靠前。留空 = 不限制。
- **排除 tag**：绝对排除，命中任意一个就不推荐。
- 搜索框可多选，点「加入推荐标签」/「排除这些标签」；已选区域的标签点一下就能取消。
- 推荐池里每张卡片下方都有可展开的 **Tags** 按钮，点开能看到该谱面的全部 tag；**双击任意 tag 直接加入推荐 tag**，已命中的会高亮。
- 脏 tag 会被自动过滤：长度 < 2 或 > 40、含替换符 / 问号 / 控制字符、字母数字占比过低的一律丢弃；`drum and bass`、`video game music` 这类已知组合按最长优先合并，避免被空格拆散。

## 试听与迷你播放器

- 卡片上的「播放预览」会拉取 osu! 官方预览音（`b.ppy.sh/preview/{setId}.mp3`）边下边放，可暂停 / 继续，切歌时自动停止并清理临时文件。
- 「迷你播放器」（标题栏的 `−`）把窗口收成 64px 高的一条，只留封面、曲名和「下一首」，适合挂在游戏旁边；`+` 还原。
- 窗口会周期性重新置顶，避免被 osu!lazer 抢到后面；窗口位置下次启动自动还原。

## 下载与导入

- 默认从 sayobot 多镜像取 `.osz`（`dl.sayobot.cn` + `txy1.sayobot.cn`，模板里的 `{setId}` 会被替换），也可自定义镜像列表。
- **自动下载** / **自动导入** 可以分开关掉；卡片上的 ↓ 是「下载并导入」，↷ 是「跳过」。
- 客户端可选 `osu!lazer` 或 `osu!stable`，也可以手动指定 exe 路径；留空时自动探测安装位置。
- 导入是把 `.osz` 交给 osu! 自己处理（进程启动导入），下载缓存只在本地。

## tosu 联动

- WebSocket 默认 `ws://127.0.0.1:24050/websocket/v2`，可改。
- **自动启动 tosu**（可关，找不到就留空自动查找 `tosu.exe`），**关闭 Roam 时结束它**（可关）。
- 能识别 `选歌 / 游玩中 / 已暂停 / 本局失败 / 结算`：
  - 开局 → 预下载推荐池里的**下一首**；
  - 成功结算 → 把刚打完的移出池子，自动导入下一首，并补齐推荐池。
- 不装 tosu 也能正常用，只是没有预下载和自动推进，需要手动点「下一首」。

## 配置、缓存和日志

| 内容 | 位置 |
|---|---|
| 配置（含加密后的 Secret、tag 池、推荐历史） | `%LOCALAPPDATA%\OsuRoam\settings.json` |
| 历史推荐记录（最近 50 首） | `%LOCALAPPDATA%\OsuRoam\history.json` |
| 键型库（解析过的谱面键型） | `%LOCALAPPDATA%\OsuRoam\patterns.json` |
| 谱面下载缓存 | `%LOCALAPPDATA%\OsuRoam\Beatmaps` |
| 日志 | `%LOCALAPPDATA%\OsuRoam\Logs\osu-roam-YYYYMMDD-HHmmss.log` |

- 推荐历史最多保留 500 条，用于减少重复推荐。
- 下载缓存**不会自动清理**，长期用记得手动清 `Beatmaps` 目录。
- 删掉 `patterns.json` 只会丢掉键型记录，重新下载谱面会重新解析。
- `settings.json` 里有 OAuth Secret（已用当前 Windows 用户加密），不要分享给别人，也别拷到别的用户环境里。

## 曲绘展示

osu! 的曲绘是 **900×250 的横幅**（3.6:1）。塞进正方形缩略图 + `UniformToFill` 时，WPF 会把图撑到
比容器大、并贴在左上角，结果**只看得见最左边 28%**（实测：230×64 的图配 64×64 的框）。
所以卡片不再放缩略图：

- 曲绘**整幅**（`Stretch=Uniform`，不裁切）铺在卡片最底层，顶部对齐，`Opacity 0.5`；
- 叠加一层由透明到卡片底色的竖直渐变，把下边缘化掉，看起来像背景而不是贴了一张图；
- 模糊不挂 `BlurEffect`：曲绘会先缩到 **96px 宽**再放大到卡片宽度，放大本身就是模糊。
  这样卡片四角仍然是精确的圆角（`BlurEffect` 会把圆角裁切糊成直角），而且没有逐帧的离屏渲染开销；
- 卡片左侧留一个 40px 的 ▶ 预览按钮（原来它叠在缩略图上，缩略图没了就得自己露出来）。

迷你播放器（34px）和信息窗（56px）的小图放不下整幅，改成**居中裁切**——原来贴左上角，等于只看左边 28%。

## 谱面信息窗

Roam 发起导入后，会在主窗口（通常是迷你播放器）**下方**弹出一个信息窗，不用再手动打开/还原窗口：

- 头部：封面、`曲名 - 谱师`、难度名、谱师 / 年份 / 键数
- 数据胶囊：星级（按 osu! 难度色标上色）、BPM、时长，以及从 `.osz` 解析出来的 mode、OD / AR / CS / HP、物件数
- **7 轴技能雷达** + **核心键型占比直方图**（进场时横向生长；占比四舍五入为 0% 的键型不占行）
  - 为什么不给核心键型也画雷达：那 6 个占比加起来恒等于 1，是"此消彼长"的数据，
    塞进雷达图会被读成互相独立的维度；横向条能直接比长短，也更省空间。
- 卡片详情层和历史详情层同样改成「7 轴技能雷达 + 直方图」，原来雷达旁边
  用文字罗列的键型占比已由直方图取代（谱师 / 星级 / 模式·LN·NPS 这些非占比信息保留）
- 明细：物件构成（单点 / 长条 / 滑条 / 转盘）、模式标签（RC / LN / HB / Mix）、LN 占比、NPS、主导键型、占比前四的具体子型
- 底部：这次推荐的推荐理由

行为：

- **下次开局自动关闭**（tosu 报 PlayStarted 时），点右上角 `×` 也能随时关；关掉后下次导入会再出现。 进出场都有动画（180ms 淡入上浮 / 170ms 淡出下沉 + 轻微缩小）。
- 「下载与高级设置」→「导入后显示谱面信息窗」可以整体关掉（默认开）。
- 窗口不抢焦点（`ShowActivated=false`），不影响 osu! 的输入；贴在主窗口下方跟随它移动/缩放，窗口放不下时自动落到主窗口右侧。

数据来源：`CS / OD / AR / HP / 物件数 / 实际 BPM` 是导入时从 `.osz` 里的 `.osu` 头解析的（osu! API 的 beatmapset 接口不直接给这些），解析在后台线程完成，和键型分析共用一次解压。

## 界面字体

界面上的数字和英文字母用 **Outfit**（`Fonts/`，Regular / SemiBold / Bold 三个字面，
以 WPF Resource 编进程序集）。它和 osu!(lazer) 用的 Torus 同属几何无衬线、观感接近，
但授权是 **SIL OFL 1.1**，可以随程序自由分发（Torus 是商业字体，需要授权才能嵌进发行包）。

Outfit 只有拉丁字母和数字，**没有汉字字形**：中文照旧由系统字体 `Microsoft YaHei UI` 渲染
（WPF 逐字符回退），和 osu!lazer 用 Noto 补 CJK 是一个思路。

- 「下载与高级设置」→「使用内置字体（Outfit）」可以随时关掉，**立刻生效、不用重启**。
- 启动日志会有一行 `字体：内置 Outfit（N 字形）；汉字回退 Microsoft YaHei UI`；
  字体没加载出来会打 `不可用` 并自动回退到系统字体（界面不会变成方框）。
- `google/fonts` 只发可变字体，而 WPF 不认可变轴，所以仓库里放的是用 fonttools
  `varLib.instancer` 切出来的静态字面，切法见 `Fonts/ATTRIBUTION.md`。

## 构建

需要 .NET 8 SDK：

```powershell
dotnet build OsuRoam.csproj -c Release

# 自包含便携包（非单文件；Fonts\*.otf 作为 WPF Resource 编进程序集）
dotnet publish OsuRoam.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -o bin\v1.3.2
```

发布前确认没有正在运行的 `OsuRoam.exe`，发布会因文件占用失败。

## 项目结构

```
MainWindow.xaml(.cs)          主窗口与交互、漫游循环
HistoryWindow.xaml(.cs)       历史推荐窗口
Models/AppSettings.cs         配置模型、tag 解析、简易筛选语义
Models/OsuData.cs             osu! API 数据结构
Services/OsuApiClient.cs      OAuth、用户 / 成绩 / 搜索（排序轮换）
Services/RecommendationEngine.cs  候选过滤与排序
Services/ManiaPatternAnalyzer.cs  mania 键型分类（移植 MMA / Interlude 算法）
Services/PatternLibrary.cs    键型库、tag 推断、键型偏好
Services/ArchiveStore.cs      历史推荐记录
Services/BeatmapDownloadService.cs  .osz 下载与导入
Services/TosuClient.cs        tosu WebSocket 状态
Services/TosuProcessManager.cs     tosu 进程托管
Services/SettingsStore.cs     配置读写
Services/SecretProtector.cs   DPAPI 加密
Services/AppLog.cs            日志
Tests/                        本地验证用控制台工程
```

## 已知限制

- 搜索上限 8 页 / 约 400 个候选；池子太小可以把星级区间放宽或降探索度。
- API 的 tag 是谱师自由填写的文本，不是严格的音乐分类字段，脏数据和拼写不一致都常见。
- 仅 Windows（WPF / `net8.0-windows`）。
- 下载源是第三方镜像，站点挂掉时需要在「下载与高级设置」里换镜像模板。
