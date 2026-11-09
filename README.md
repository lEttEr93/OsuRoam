# osu! Roam v1.5.6.1

[English](#english) · [中文](#中文)

---

# English

A beatmap roaming recommender for osu! — Windows / WPF / .NET 8. It reads your score samples and builds a
recommendation pool by star rating, year, length and tags. Together with [tosu](https://github.com/KotRikD/tosu)
it pre-downloads the next map when you start playing and imports it when the results screen appears, so you can
keep roaming without touching it.

**Two packages, same code:**

| Package | UI language | Default download source |
|---|---|---|
| `osu-roam-1.5.6.1-portable.zip` | follows the system language | sayobot mirrors (China) |
| `osu-roam-1.5.6.1-en-portable.zip` | English | catboy / beatconnect (international) |

The .NET runtime and `tosu.exe` are already inside the archive — extract and run. Each build keeps its own `data`
folder, so **do not install one over the other**.  A fresh folder inherits the analysed-pattern library from a
neighbouring `osu-roam*` install, so pattern data does not start from zero.

## What's new in 1.5.6.1

The 1.5.6fix baseline plus four interface changes from the maintenance list:

- **The mini player's button says what it does**: with nothing imported it reads "Start roaming" instead of
  "Next", because pressing it is exactly that.
- **The recommendation range is split into three tabs** — *Basic* (stars / years / lengths), *Tag* (status /
  genre / language / the tag lists) and *Pattern* (pattern preference, LN-only). One is shown at a time.
  Exploration sits on its own row at the bottom: it applies to the whole pool, not to one tab.
- **The game mode moved above the pool** as four icon buttons (osu! / taiko / catch / mania). mania is last and,
  while selected, the free space on the right is used for the key count instead of a separate row.
- **Preview speed is entered as the in-game scroll speed**: speed × milliseconds = 11485, so the field reads
  something like `9.19` and the milliseconds per screen are printed underneath.

Also in this version:

- **The new controls animate**: switching the mode gives the icon a short pop and slides the key-count picker in
  and out; switching a range tab fades the tab colour over and eases the panel in (the panel is laid out first, so
  the fade is not eaten by the layout cost of the Tag page).
- **The range block no longer changes width**: the scrollbar is always reserved, so switching from *Basic* to
  *Tag* (the tall one) does not shift everything by 9 px, and the thumbnail is hidden while nothing scrolls.
- **"Suggest" sits on the star-rating row**, right-aligned, next to the range label.
- **Tooltips are no longer covered**: the topmost keeper skips its 750 ms kick while a tooltip or a dropdown is
  open and re-asserts right after it closes.

## What's new in 1.5.6fix

This is the **maintenance baseline**: everything from 1.5.5, plus the fixes below.

- **The preview no longer dies after gameplay.** Returning to song select from a pause could leave the panel with
  its background but no notes at all. The render thread was blocked on a frame-latency wait that sits *before* it
  reads the chart, so it stayed alive but never drew again. It no longer waits there, has a timeout fallback and
  can restart itself if the thread ever dies.
- **Notes are drawn from textures now** instead of solid rectangles: a built-in set ships in `Assets/Preview`, and
  Settings → *preview texture folder* can point at an osu! skin (a skin folder is never auto-detected). Missing
  textures fall back to the plain rectangles.
- Fixed two crashes/hangs found on the way: textures showing only their left half, and a Direct2D deadlock when
  bitmaps were created inside `BeginDraw`.
- **tosu no longer leaves zombies behind** (the child process is bound to a Job object), and a tosu that is
  connected but reports no game data is restarted automatically.
- **Import failures are no longer silent**: if `osu!.exe` exits non-zero within 30 seconds, the status bar says so
  and the exit code is logged.

**Known limitation, not fixed:** the preview still has slight periodic judder. With per-pixel transparency it must
go through DWM composition, which occasionally holds a frame for an extra refresh (measured display interval
p95 8.3 ms, versus 4.21 ms for osu!lazer's independent flip). Queue depth, submission rate, both GPUs and a
compositor-animated tile renderer were all tried without removing it.

## Quick start

1. Extract and run `OsuRoam.exe`.
2. Under "Account & connection", fill in the OAuth Client ID, Client Secret and your player name.
3. Pick a mode with the four icons above the pool (mania is last; the key count appears on the right).
4. Drag the star rating slider, or click "Suggest" to compute it from your scores.
5. Click "Start roaming".

## OAuth credentials

- Create an application at [osu! account settings → OAuth](https://osu.ppy.sh/home/account/edit#oauth) and copy the
  **Client ID** and **Client Secret**.
- The `client_credentials` flow with `scope=public` — public data only, no password needed.
- The secret is encrypted with Windows user credentials (DPAPI), so **it must be re-entered after switching users
  or machines**.

## Settings you will actually touch

- **Star rating range**: 0–11, with "∞" meaning no upper bound; stored separately for each of the 13 modes
  (`osu` / `taiko` / `catch` / `mania-1k` … `mania-10k`).
- **Suggest**: computes `center ± 0.45` from your recent scores. Starting a roam never overwrites a range you set
  by hand.
- **Exploration**: higher favours maps you have not heard, lower sticks to the mappers / BPM / length you play.
- **Quick filters** (status / genre / language): checked = preferred (a hit adds points), indeterminate = strict
  (a mismatch is excluded), unchecked = no restriction.
- **Tags**: a candidate must hit at least one recommended tag; any excluded tag knocks it out. Double-click a tag
  under a card to add it to the recommended list.
- **Language**: Interface → Language — system default / 简体中文 / English, applied immediately.
- **Skip installed beatmaps** (on by default): scans the osu! beatmap folder — `Songs` for stable, `files`
  for lazer — and never recommends a difficulty you already have. Other difficulties of the same song still
  show up. Costs ~2 s on the first run, then it is cached.
- **Info panel shows**: the beatmap Roam just imported, or whatever is selected in osu! (read live from
  tosu, no download needed).
- **Beatmap preview in song select** (on by default): scrolls the selected mania chart with the game's preview
  audio; the window docks to the right of the info panel at exactly the same height.
- **History** (title bar) shows the last 50 recommendations.

## Download and tosu

- `.osz` files come from mirrors; the template list is editable under "Downloads & advanced". ↓ downloads and
  imports, ↷ skips.
- Client can be `osu!lazer` or `osu!stable`; leave the exe path empty to auto-detect.
- tosu is started automatically (and stopped when Roam exits) — you can turn both off. Without tosu everything
  works except pre-download and auto-advance.

## Where things live

| What | Where |
|---|---|
| Settings (encrypted secret), history, pattern library | `data\settings.json`, `history.json`, `patterns.json` |
| Beatmap cache | `data\Beatmaps` |
| Logs | `data\Logs\` |

Portable by default: everything sits in `data\` next to the exe; it only falls back to `%LOCALAPPDATA%\OsuRoam`
when the program folder is not writable. The beatmap cache is never cleaned automatically.

## Credits

- [tosu](https://github.com/KotRikD/tosu) (MIT) — game state over WebSocket; bundled with the portable package.
- [osumania_map_analyser](https://github.com/LeoBlackMT/osumania_map_analyser) (MIT) — the mania key-pattern
  classification this project follows.
- [Interlude](https://github.com/YAVSRG/YAVSRG) — the RC pattern algorithm behind that classification.
- osu!(lazer) and [MinaCalc](https://github.com/etternagame/etterna) — references for the UI feel and for mania
  skill evaluation.
- **Outfit** (SIL OFL 1.1) — the Latin UI font; licence texts in `Fonts/`.
- Beatmap metadata and covers come from the osu! Web API.

---

# 中文

osu! 谱面漫游推荐工具，Windows / WPF / .NET 8。读你的成绩样本，按星级、年份、时长和 tag 生成一个推荐池；
配合 [tosu](https://github.com/KotRikD/tosu)，开局时预下载下一首、出结算画面时自动导入，可以一路漫游下去。

**两个包，同一份代码：**

| 包 | 界面语言 | 默认下载源 |
|---|---|---|
| `osu-roam-1.5.6.1-portable.zip` | 跟随系统 | sayobot 镜像（国内） |
| `osu-roam-1.5.6.1-en-portable.zip` | 英文 | catboy / beatconnect（国际） |

## 1.5.6.1 新增

1.5.6fix 这个维护基线照旧，下面是按维护清单改的五处界面：

- **迷你播放器的按钮改成「说人话」**：还没有谱面（标题显示「尚未导入谱面」）时，按钮显示「开始漫游」而不是
  「下一首」—— 因为点下去做的事就是开始漫游。
- **推荐范围拆成「基本 / Tag / 键型」三个标签页**：基本 = 星级、年份、时长；Tag = 分类、流派、语言、tag 列表；
  键型 = 键型偏好、只要面谱。一次只显示一个，点标签切换。**探索度单独放在最下面一行**，因为它影响整个推荐池，
  不属于任何一个标签页。
- **游戏模式挪到推荐池上方**，换成四个玩法图标按钮（osu! / taiko / catch / mania）。mania 排在最后，
  选中时用这一行右边的空位选键数，不再单独占一行。
- **「预览流速」改成填游戏里的滚动速度**：速度 × 毫秒数 = 11485，所以框里是 `9.19` 这样的数，
  下面一行显示换算出来的「≈ 1250 毫秒/屏」。
- **四个大块改成底部标签条**：推荐范围 / 账号与连接 / 界面 / 下载与高级设置不再各自折叠，改成日志区上方的
  四个横标签 —— **默认收起**（推荐池拿回全部高度），点哪个就从底部抽出哪一块，再点同一个标签收起。切换时
  标签底色渐变、整页淡入上浮，抽屉高度也是动画；设置区高度跟着当前块走（界面这种一两行的块不留空白，
  下载与高级设置封顶 360 后区内滚动）。

这个版本里还修了这些：

- **新控件补上了动画**：切模式时图标会轻微弹一下、mania 的键数选择滑进滑出；切推荐范围标签页时标签底色
  渐变过去、整页淡入上浮（新页先跑完布局再淡入，否则 Tag 页的布局开销会把动画时间吃掉，看着还是硬切）。
- **推荐范围的宽度不再变**：滚动条改成常驻占位，所以从「基本」切到最长的「Tag」不会整块左右抖 9px；
  内容放得下时滑块本身隐藏，只留下透明的空位。
- **「建议星级」挪到「难度星级」那一行的最右边**，左边标签、右边区间提示 + 按钮。
- **提示气泡不再被置顶行为盖住**：气泡或下拉展开期间，置顶保持会跳过这一拍，关掉后立刻补回来。

## 1.5.6fix 新增（维护基线）

1.5.5 的全部功能照旧，下面是这个版本修的：

- **从游戏退回选歌后预览不再“只剩黑底”**：根因是渲染线程卡在“帧延迟等待”上，而该等待在读谱面之前，
  线程活着却再也不绘制。现在不再等它，并且加了连续超时兜底与线程自愈。
- **音符改成贴图驱动**（不再是纯色矩形）：内置一套贴图在 `Assets/Preview`；设置里的「预览贴图目录」可以指向
  一个 osu! 皮肤目录（**不会自动扫描**）。读不到就退回纯色块。
- 顺手修了两个坑：贴图只显示左半截、以及在 `BeginDraw` 里创建位图导致的 Direct2D 死锁。
- **tosu 不再留僵尸进程**（放进 Job 对象，Roam 被强杀时一起收掉），并且“连着但读不到游戏”会自动重启。
- **导入失败不再静默**：`osu!.exe` 30 秒内非 0 退出会在状态栏说明并记录退出码。

**已知限制（不修）**：预览仍有轻微周期性抖动。逐像素透明必须走 DWM 合成，它会偶尔把一帧多留一个刷新周期
（实测显示间隔 p95 8.3 ms，而 osu!lazer 的独立翻转是 4.21 ms）。排队深度、提交频率、两块 GPU、
图块 + 合成器动画都试过，没能消除。

.NET 运行时和 `tosu.exe` 都在包里，解压即用。两版各有自己的 `data` 目录，**不要覆盖安装**。

## 快速使用

1. 解压，双击 `OsuRoam.exe`。
2. 「账号与连接」填 OAuth Client ID、Client Secret 和玩家名。
3. 用推荐池上方的四个图标选游戏模式（mania 在最后，选中后右边出现键数）。
4. 拖星级双滑块，或点「建议星级」按成绩算一次。
5. 点「开始漫游」。

## OAuth 凭据

- 在 [osu! 账号设置 → OAuth](https://osu.ppy.sh/home/account/edit#oauth) 建一个应用，拿到 **Client ID** 和
  **Client Secret**。
- 走 `client_credentials`，`scope=public`，只读公开数据，不需要密码。
- Secret 用 Windows 用户凭据（DPAPI）加密后存在本地，**换 Windows 用户或换机器要重填**。

## 真正会调的设置

- **难度区间**：0–11 双滑块，右端「∞」= 不限；`osu` / `taiko` / `catch` / `mania-1k` … `mania-10k`
  十三组分别保存。
- **建议星级**：按最近成绩算 `中心 ± 0.45`；开始漫游不会覆盖你手动拖出来的值。
- **探索度**：越高越偏没听过的谱面，越低越贴近常打的谱师 / BPM / 时长。
- **简易筛选**（状态 / 流派 / 语言）：勾选 = 偏好（命中加分），中间态 = 严格（不匹配就排除），不勾 = 不限。
- **Tag**：推荐 tag 必须命中至少一个才入选，排除 tag 命中即出局；卡片下方的 Tags 里双击任意 tag 可直接加入。
- **界面语言**：「界面」→「界面语言」，跟随系统 / 简体中文 / English，切换即时生效。
- **排除已安装的谱面**（默认开）：扫一遍 osu! 的谱面目录（stable 的 `Songs`、lazer 的 `files`），
  已经在你机器上的难度不再推荐；同一首歌的其他难度仍会出现。首次约 2 秒，之后走缓存。
- **信息栏显示**：可以选「导入的谱面」或「游戏内选中的谱面」（后者直接读 tosu，不用下载）。
- **选歌时显示谱面预览**（默认开）：跟着游戏的预览音频滚当前选中的 mania 谱面；窗口贴在信息窗右侧、和它等高。
- **历史**（标题栏）看最近推荐的 50 首。

## 下载与 tosu

- `.osz` 从镜像取，模板可在「下载与高级设置」里改；↓ 下载并导入，↷ 跳过。
- 客户端可选 `osu!lazer` 或 `osu!stable`，也可以手填 exe 路径，留空自动探测。
- tosu 默认自动启动、关 Roam 时结束，两者都能关。不装 tosu 也能用，只是没有预下载和自动推进。

## 文件在哪

| 内容 | 位置 |
|---|---|
| 配置（含加密 Secret）、历史、键型库 | `data\settings.json`、`history.json`、`patterns.json` |
| 谱面缓存 | `data\Beatmaps` |
| 日志 | `data\Logs\` |

默认便携模式：都在 exe 旁边的 `data\`；只有程序目录不可写时才退回 `%LOCALAPPDATA%\OsuRoam`。
下载缓存不会自动清理。

## 致谢

- [tosu](https://github.com/KotRikD/tosu)（MIT）—— 通过 WebSocket 读取对局状态，便携包里已附带。
- [osumania_map_analyser](https://github.com/LeoBlackMT/osumania_map_analyser)（MIT）—— mania 键型分类体系，
  本项目的分析与分类照此实现。
- [Interlude](https://github.com/YAVSRG/YAVSRG) —— 上述分类里 RC 键型算法的来源。
- osu!(lazer) 与 [MinaCalc](https://github.com/etternagame/etterna) —— 界面观感与 mania 技能评估的参考。
- **Outfit**（SIL OFL 1.1）—— 界面西文字体，授权文本见 `Fonts/`。
- 谱面元数据与曲绘来自 osu! Web API。
