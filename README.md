# osu! Roam v1.5.6.4

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
| `osu-roam-1.5.6.4-portable.zip` | follows the system language | sayobot mirrors (China) |
| `osu-roam-1.5.6.4-en-portable.zip` | English | catboy / beatconnect (international) |

**Companion packages:** [`OsuSv`](https://www.nuget.org/packages/OsuSv) /
[`OsuSv.Cli`](https://www.nuget.org/packages/OsuSv.Cli) — the scroll-velocity (SV) analysis used by Roam, shipped as a
standalone library and command line tool (`dotnet tool install --global OsuSv.Cli`, or grab the single-file exe from the
`sv-*` release). Feed it `.osu` text, get irregularity / extremeness / memorization / discomfort / exaggeration back;
see [`docs/1.5.3-SV评估算法.md`](docs/1.5.3-SV评估算法.md) for the model. Sources live in `src/OsuSv` and `src/OsuSv.Cli`, MIT.

The .NET runtime and `tosu.exe` are already inside the archive — extract and run. Each build keeps its own `data`
folder, so **do not install one over the other**.  A fresh folder inherits the analysed-pattern library from a
neighbouring `osu-roam*` install, so pattern data does not start from zero.

## What's new in 1.5.6.4

- **The window no longer jumps at startup**: the saved position is applied before the window is shown
  (it used to be applied in `Loaded`, so the window appeared at the default spot and then moved).
- **The pattern tab follows the mode**: on anything other than mania the pattern checkboxes are replaced by a
  one-line note, since pattern analysis only exists for mania.
- **New "Avoid LN-heavy charts"**: charts with more than 30% long notes sort to the end of the candidate pool and
  can be swapped out while the pool is full (their reason line says so).
- **A four-second splash on startup**: icon, `osu!Roam` and "Your hands are for your life, not for osu.", fading
  in and out; the main window only appears afterwards, so clicking early can no longer stall it.

## What's new in 1.5.6.3

Fixes the "Next keeps cycling the same few maps" problem, and adds a one-shot notice after an in-place update.

- **The candidate pool keeps topping itself up**: admission used to stop after at most 3 rounds (a round that
  found nothing ended it), so a 700-set total pool was often judged to ~30 charts and the recommendation pool
  could only ever draw from those. It is now water-level driven — while the number of *unseen* qualified
  candidates is below the level (pool size × 6, clamped to 20–60) it keeps judging batch after batch, and when
  the total pool has no unjudged sets left it runs **another search** (different rotation) to bring more in.
- **The recommendation pool is append-only again**: cards already on screen are no longer replaced by background
  re-ranking; they only leave by being played, skipped or replaced through "Next".
- **Candidates are split into two tiers**: unseen qualified charts fill the candidate slots; charts you already
  got (imported or played) sit at the tail as a backup and get pushed out as soon as new ones arrive.
- **First launch after an in-place update shows a small notice**: "Updated to 1.5.6.3 — improved the
  recommendation algorithm". It is shown once: the updater's result file is consumed when it is read.

## What's new in 1.5.6.2

- **The app updates itself.** Settings → *Downloads & advanced* → “Version & updates” shows the current version,
  looks up the newest release and, when there is one, downloads the portable package and swaps the program files
  in place. `data\` (settings / beatmap cache / logs) is never touched, and the app restarts into the new version
  by itself. A check also runs 20 seconds after startup (can be switched off) — check only, nothing is downloaded
  until you press the button. If the proxy's exit IP has used up GitHub's anonymous 60-per-hour quota (the normal
  case behind a game accelerator), the check retries over a direct connection.
- **Edits under “Downloads & advanced” are stored right away** — text boxes on focus loss, checkboxes and the
  pool-size picker on change, plus one more save just before quitting for an update. Previously that page was only
  written when you pressed “Start roaming”, so changing something and then updating threw it away.
- **The LN triangle draws itself** in the beatmap info panel: the outline traces around in 320 ms, then the fill
  fades in. The finished picture is pixel-identical to before.
- **Startup and exit animations**, and the startup work is split into four phases with a dispatcher yield between
  them, so clicking a button right after launch no longer queues behind config/pattern parsing.

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

**Games launched by an accelerator (UU etc.) are unreadable:** these launchers start the game with administrator
rights (the game's parent process really is `uu.exe`), so from a normal-privilege process
`OpenProcess(PROCESS_VM_READ)` fails with `ERROR_ACCESS_DENIED` — and tosu reads the game from memory, which shows up
as Roam never seeing the game while `tosu stderr: failed to query the process` repeats every second.
Roam now detects this: the status bar explains it and a **Run as admin** button appears (the tosu it spawns inherits
the elevation). You can also right-click → Run as administrator.

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
| `osu-roam-1.5.6.4-portable.zip` | 跟随系统 | sayobot 镜像（国内） |
| `osu-roam-1.5.6.4-en-portable.zip` | 英文 | catboy / beatconnect（国际） |

**附带产品：[`OsuSv`](https://www.nuget.org/packages/OsuSv) / [`OsuSv.Cli`](https://www.nuget.org/packages/OsuSv.Cli)** —— 谱面变速（SV）评估算法单独出的库
和命令行（`dotnet tool install --global OsuSv.Cli`，或下 Release 里的单文件 exe）。输入 .osu 文本，输出
不等时 / 极端度 / 背谱度 / 不适度 / 夸张度五个量，口径见 [`docs/1.5.3-SV评估算法.md`](docs/1.5.3-SV评估算法.md)。
源码在 `src/OsuSv`、`src/OsuSv.Cli`，许可证 MIT。

## 1.5.6.4 新增

- **启动时窗口不再跳动**：位置在窗口显示之前就定好（以前是在 `Loaded` 里赋，会先出默认位置再挪过去）。
- **键型页跟模式走**：非 mania 模式下收起勾选框，改成一行说明——键型分析只有 mania 有。
- **新增「不善长条」**：长条（LN）占比高于 30% 的谱面排到候选池最后，池子满时可以被非长条的新候选换掉，
  推荐理由里会标出来。
- **四秒启动画面**：图标 + `osu!Roam` + “Your hands are for your life, not for osu.”，淡入淡出，
  之后主窗才出现 —— 启动期间点不到东西，也就不会把主窗点卡。

## 1.5.6.3 新增

这一版主要是把「点下一首只有那几首在转」修掉，另外更新后第一次启动会告诉你更新了什么。

- **候选池会一直从总池进货**：以前准入最多跑 3 轮（某一轮没找到达标就停），700 个候选常常只判了 30 多张，
  推荐池只能从这几十张里出。现在按水位驱动 —— 「还没给过的达标候选」低于水位
  （池大小 × 6，夹在 20–60）就一批批继续判；总池里没判过的判完了，还会**换一轮搜索**继续进货。
- **推荐池只加不换**：已经出现在列表里的卡片不会再被后台重排换掉，出场只靠「打完 / 跳过 / 点下一首」。
- **候选池分两档**：没给过的占候选池的位置；给过（导入过 / 玩过）的只排在最后当备用，
  一旦有新的够格谱面就把它们挤出去。
- **更新后第一次启动弹一次小窗**：「已更新到 1.5.6.3 —— 优化了推荐算法」，只显示一次
  （更新脚本留下的结果文件被读走即清）。

## 1.5.6.2 新增

- **软件能自己更新了**：设置 →「下载与高级设置」→「版本与更新」显示当前版本，查 GitHub 上最新的 release，
  有新版就下载便携包、就地替换程序文件（`data\` 里的配置 / 谱面缓存 / 日志不动），完成后自动重启到新版本。
  启动 20 秒后还会静默查一次（可关）：只查不下载，要点了按钮才下。代理出口把 GitHub 匿名额度（60 次/小时）
  用光时（国内开着加速器的常态）会自动改直连再试一次。
- **「下载与高级设置」里的改动立刻落盘**：文本框失焦、勾选框和推荐池大小选择即存，点更新前再存一次。
  以前这一页只在点「开始漫游」时才写盘，改完直接更新就会把设置丢掉。
- **LN 三角会“画”出来**：信息窗里那条粉色三角先按周长描边（320ms），描边过半后再淡入填充；
  画完的静止画面与之前逐像素一致。
- **启动 / 退出动画**，并且把启动阶段切成四段、段间让出调度 —— 刚启动就连点按钮不再排在配置/键型解析后面。

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

**用加速器（UU 等）启动 osu! 时读不到游戏状态**：这类加速器会用管理员权限把游戏拉起来（实测 osu! 的父进程
就是 `uu.exe`），普通权限下 `OpenProcess(PROCESS_VM_READ)` 直接 `ERROR_ACCESS_DENIED`，而 tosu 靠读内存拿状态
—— 表现就是 Roam 一直「认不出游戏」，日志里每秒刷 `tosu stderr: failed to query the process`。
Roam 现在会检测到这种情况：状态栏说明原因，右边出现「以管理员重启」，点它即可（Roam 拉起的 tosu 会跟着提权）。
也可以手动右键 → 以管理员身份运行。

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
