# osu! Roam v1.5.0

[English](#english) · [中文](#中文)

---

# English

A beatmap roaming recommendation tool for osu!. Windows / WPF / .NET 8. **One repository, two builds:**

| Build | UI language | Default download source |
|---|---|---|
| `osu-roam-v1.5.0-en-portable.zip` | English | catboy / beatconnect (international) |
| `osu-roam-v1.5.0-portable.zip` | follows the system language | sayobot mirrors (China) |

The code is identical; only a compile-time constant (`EN_BUILD`) and the download defaults differ. The Chinese
mirrors are a liability outside China and the international sources are the wrong choice on Chinese networks,
so each audience gets its own build instead of a settings decision. Both builds can switch language at runtime
(**Interface → Language**) — only the factory default differs. Each build keeps its own `data` folder, so install
them side by side rather than on top of each other.

osu! Roam reads player profile and score samples through the osu! Web API to build a "star rating / BPM / length / mapper" preference profile, then generates a capped recommendation pool by star rating range, year, length, quick filters and tags. Together with [tosu](https://github.com/KotRikD/tosu) it can do continuous roaming: **pre-download the next map at play start, auto-import the next map after results**.

No password needed — only your player name plus an OAuth Client ID / Secret that you register yourself.

---

## How recommendations are computed

1. **Profile**: pull the current mode's `recent` + `best` scores (mania filters by the selected key count), and compute the 62nd percentile star rating (±0.45 as the suggested range), median BPM, median length, mapper frequency, and the set of recently played beatmaps.
2. **Hard filtering** (fail any one and it is out): mode / mania key count, star rating range, length range, year range, the "strict" tier of quick filters, an excluded tag hit, none of the recommended tags hit, beatmaps already recommended or recently played.
3. **Scoring**: the closer the star rating to the range center the better (relative weight 62), BPM close to preference (15), length close to preference (10), mapper match (13); on top of that an exploration random term, tag hit ratio, quick filter preference hit rate, `ranked` / `loved` status priority, and for mania the **pattern fit**.
4. **Sorting**: strict filter hit count → status priority → composite score, take the top N into the recommendation pool.

The candidate source rotates between 6 search sort orders on every roam (newest / highest rated / oldest / most played / most favourited / highest difficulty); each round takes 4 pages of two sort orders, up to 8 pages totalling roughly 400 beatmapsets. That way the next round's candidates differ from this round's, so the same batch of maps is not recommended over and over.

---

## Recommendation history

The **History** button in the title bar opens the recommendation history window, listing the last **50** recommended beatmaps with the same card layout as the recommendation pool: cover, title, difficulty, star rating / BPM / length, recommendation reason and recommendation time. The **Tags** below each card can be expanded to show all tags of that beatmap; **double-click any tag to add it directly to the recommended tags**, and already matched ones are highlighted.

History is stored in `%LOCALAPPDATA%\OsuRoam\history.json`; when the same beatmap is recommended again it moves to the front.

## Key patterns (mania)

In mania mode, key patterns are used to make recommendations more targeted. The classification system is ported from [osumania_map_analyser](https://github.com/LeoBlackMT/osumania_map_analyser) (MIT; its RC pattern algorithm originates from [Interlude](https://github.com/YAVSRG/YAVSRG)):

- 6 core types: `Stream` / `Chordstream` / `Jacks` / `Coordination` / `Density` / `Wildcard (variable-speed misc)`
- 4K subtypes: `Rolls` `Trills` `Minitrills` `Handstream` `Split Trill` `Jumptrill` `Jumpstream` `Longjacks` `Quadstream` `Gluts` `Chordjacks` `Minijacks` `Column Lock` `Release` `Shield` `Inverse` `JS Density` `HS Density` `Jacky WC` `Speedy WC`
- Mode tags: `RC` (LN ratio ≤15%), `LN` (≥90%), `Mix`, `HB` (long notes and normal notes mixed)

How it works:

1. After a beatmap in the recommendation pool is downloaded, its pattern is parsed by reading the `.osu` directly out of the `.osz`, and the result is stored in the pattern library `%LOCALAPPDATA%\OsuRoam\patterns.json`; the recommendation reason on the pool card carries the recognized classification (e.g. `Jumpstream Tech`).
2. Using the scores with the same key count from your recent + best, average the patterns of those beatmaps to get a **pattern preference vector**.
3. When sorting, points are added by the cosine fit between the candidate's pattern and the preference vector, with the weight controlled by `PatternWeight` (default 0.12).
4. Candidates that have not been downloaded yet have no `.osu`, so tags are used for a rough inference first (`ln` / `rice` / `jack` / `chordjack` / `handstream` / `tech` / `sv` etc.); the real pattern takes over automatically once it is in the library.

---

## Quick start

1. Extract `osu-roam-v1.5.0-en-portable.zip` (download from [Releases](https://github.com/lEttEr93/OsuRoam/releases/latest); nothing else to install — the .NET runtime is baked into `OsuRoam.exe` and `tosu.exe` is already in the folder).
2. Double-click `OsuRoam.exe`.
3. Under "Account & connection" fill in the OAuth Client ID, Client Secret and player name.
4. Choose the game mode (osu! / taiko / catch / mania 1K–10K).
5. Drag the star rating double slider, or click "Suggested star rating" to compute it once from your scores.
6. Click "Start roaming".

## OAuth credentials

- Create an application in [osu! account settings → OAuth](https://osu.ppy.sh/home/account/edit#oauth) and grab the **Client ID** and **Client Secret**.
- It uses the `client_credentials` flow with `scope=public`, so it can only read public data.
- The Client Secret is encrypted with Windows user credentials (DPAPI) and stored in the local configuration, **so re-entering is required after switching Windows users or machines**.
- Access to `osu.ppy.sh` is required.

## Recommendation range

- **Difficulty star rating**: double slider, 0–11; dragging to "∞" means no upper bound.
- **Suggested star rating**: computed once from recent score samples, writing `center ± 0.45` into the current mode / key count range. **Starting a roam does not overwrite manual settings**; after you drag manually, the automatic mark for that range is turned off.
- Star rating ranges are **stored separately for 13 groups**: `osu` / `taiko` / `catch` / `mania-1k` … `mania-10k`.
- **Earliest / latest year** (default 2007–current year), **shortest / longest seconds** (default 45–360).
- **Exploration** (default 0.45, 0–1): the higher, the more it favours beatmaps you have not heard (the random discovery term gains weight and familiarity loses weight); the lower, the closer it stays to the mappers / BPM / length you usually play.
- **Recommendation pool size** (default 5), **exclude recently played** (on by default).

## Quick filters (three-state)

Each of "category (status) / genre / language" is a three-state checkbox:

| State | Meaning |
|---|---|
| Checked | **Preference** — a hit adds points, a miss does not exclude |
| Intermediate | **Strict filter** — a mismatch is excluded outright |
| Unchecked | No restriction |

By default only `ranked` / `approved` / `qualified` / `loved` are preferred, and genre and language are unrestricted.

## Tag filtering

The tag pool ships with preset genres, mania pattern characteristics (including `米` `面` `叠` `技` `乱` `切`) and common multi-word combinations, and it keeps collecting new tags from API search results, **storing them permanently in the local configuration**.

- **Recommended tags**: once set, a candidate **must hit at least one of them** to be selected (not a pure bonus); the higher the hit ratio the higher it sorts. Leaving it empty means no restriction.
- **Excluded tags**: absolute exclusion — a hit on any one of them means no recommendation.
- The search box allows multi-select; click "Add to recommended tags" / "Exclude these tags"; clicking a tag in the selected area removes it.
- Every card in the recommendation pool has an expandable **Tags** button that shows all tags of that beatmap; **double-click any tag to add it directly to the recommended tags**, and already matched ones are highlighted.
- Dirty tags are filtered out automatically: length < 2 or > 40, containing replacement characters / question marks / control characters, or too low an alphanumeric ratio are all dropped; known combinations such as `drum and bass` and `video game music` are merged longest-first so they are not split apart by spaces.

## Preview and mini player

- "Play preview" on a card fetches osu!'s official preview audio (`b.ppy.sh/preview/{setId}.mp3`) and plays it while downloading; it can be paused / resumed, and switching tracks stops it automatically and cleans up the temporary file.
- The "mini player" (`−` in the title bar) collapses the window into a 64px-high strip with only cover, title and "Next", convenient to park beside the game; `+` restores it.
- The window periodically re-asserts itself on top so osu!lazer does not push it behind; the window position is restored automatically on the next launch.

## Download and import

- This build fetches `.osz` files from `catboy.best` and `beatconnect.io`, with `{setId}` in the template substituted; the standard build uses sayobot's Chinese mirrors instead. The list is editable in "Downloads & advanced".
- Some international mirrors reject an empty `User-Agent`, so requests carry `osu-roam/<version>`.
- **Auto download** / **Auto import** can be switched off separately; ↓ on a card is "download and import", ↷ is "skip".
- The client can be `osu!lazer` or `osu!stable`, or an exe path can be specified manually; when left empty the install location is auto-detected.
- Import hands the `.osz` to osu! to handle itself (import by launching the process); the download cache stays local.

## tosu integration

- The WebSocket defaults to `ws://127.0.0.1:24050/websocket/v2` and can be changed.
- **Auto-start tosu** (can be off; if not found, leave it empty to auto-locate `tosu.exe`), **end it when Roam closes** (can be off).
- Recognizes `song select / playing / paused / failed this run / results`:
  - Play start → pre-download the **next** beatmap in the recommendation pool;
  - Successful results → move the just-played map out of the pool, auto-import the next one, and top the pool back up.
- It works fine without tosu, just without pre-download and auto-advance; you click "Next" manually.

## Configuration, cache and logs

| Content | Location |
|---|---|
| Configuration (encrypted Secret, tag pool, recommendation history) | `%LOCALAPPDATA%\OsuRoam\settings.json` |
| Recommendation history records (last 50) | `%LOCALAPPDATA%\OsuRoam\history.json` |
| Pattern library (parsed beatmap patterns) | `%LOCALAPPDATA%\OsuRoam\patterns.json` |
| Beatmap download cache | `%LOCALAPPDATA%\OsuRoam\Beatmaps` |
| Logs | `%LOCALAPPDATA%\OsuRoam\Logs\osu-roam-YYYYMMDD-HHmmss.log` |

- Recommendation history keeps at most 500 entries, used to reduce duplicate recommendations.
- The download cache **is not cleaned automatically**; for long-term use remember to clear the `Beatmaps` directory by hand.
- Deleting `patterns.json` only loses the pattern records; re-downloading beatmaps parses them again.
- `settings.json` contains the OAuth Secret (encrypted for the current Windows user); do not share it with others, and do not copy it into another user's environment.

## Cover art display

osu!'s cover art is a **900×250 banner** (3.6:1). When squeezed into a square thumbnail with `UniformToFill`, WPF scales the image larger than the container and pins it to the top-left, so **only the leftmost 28% is visible** (measured: a 230×64 image in a 64×64 frame).
So cards no longer use thumbnails:

- The cover art **in full** (`Stretch=Uniform`, no cropping) is laid over the lowest layer of the card, top-aligned, `Opacity 0.5`;
- **The alpha of the bottom edge is baked straight into the bitmap**: the lower 70% fades to fully transparent with a smoothstep (`t²(3−2t)`).
  An overlaid gradient cannot cover a hard edge (the image's bottom edge is a horizontal line on screen whose position varies with the card's aspect ratio); only writing alpha into the pixels truly removes the edge;
- Then a **blurred copy filling the whole card** (`Stretch=UniformToFill`, 0.18) is overlaid,
  so the lower half of the card also carries the cover art's colors instead of pure black;
- Finally a 44%→86% darkening gradient is layered on to guarantee text contrast (it no longer goes fully opaque, otherwise the lower half would be crushed to black);
- The blur does not use `BlurEffect`: the cover art is first scaled down to **96px wide** and then up to the card width; the upscale itself is the blur.
  That way the card corners stay precisely rounded (`BlurEffect` blurs the rounded clipping into square corners), and there is no per-frame offscreen rendering cost;
- A 40px ▶ preview button is left on the left side of the card (it used to sit on the thumbnail, so with the thumbnail gone it has to show itself).

The mini player (the whole 64px-high strip) and the info window (the entire panel) use the same logic and no longer carry a small square image either:

- **Mini player**: the cover art fills the whole title bar strip. This strip is only 64px high, so the full image is filled by **height alignment** and leaves gaps on the left and right; that layer therefore uses the version with "22% horizontal feather on each side" (`BackdropBar`), and the edges blend into the blurred layer behind it; there is no vertical fade.
- **Info window**: the cover art fills the whole panel, same as the card — "blurred base + full image at the top (vertical alpha fade) + darkening".

All three backdrop bitmaps are generated at once in `CoverLoader` (96px wide): `Backdrop` (vertical fade), `BackdropBar` (horizontal feather), `Cover` (256px sharp version); the fade and the feather are both baked into the pixels with smoothstep.

## Beatmap info window

After Roam initiates an import, an info window pops up **below** the main window (usually the mini player), so there is no need to open or restore the window by hand:

- Header: cover, `title - mapper`, difficulty name, mapper / year / key count
- Data capsules: star rating (colored by the osu! difficulty color scale), BPM, length, plus mode, OD / AR / CS / HP and object count parsed out of the `.osz`
- **7-axis skill radar** + **core pattern share histogram** (the bars grow horizontally on entry; patterns whose share rounds to 0% get no row)
  - Why not draw a radar for the core patterns as well: those 6 shares always sum to exactly 1 — it is "as one grows the other shrinks" data, and putting it into a radar would be read as mutually independent dimensions; horizontal bars can be compared by length directly and use less space.
- The card detail layer and the history detail layer are likewise changed to "7-axis skill radar + histogram"; the pattern shares previously listed as text next to the radar have been replaced by the histogram (non-share information such as mapper / star rating / mode·LN·NPS is kept)
- Details: object composition (circles / long notes / sliders / spinners), mode tags (RC / LN / HB / Mix), LN ratio, NPS, dominant pattern, and the top four specific subtypes by share
- Bottom: the recommendation reason for this pick

Behavior:

- **Auto-closes on the next play start** (when tosu reports PlayStarted); the `×` in the top-right closes it at any time too; after closing it appears again on the next import. Both entering and leaving are animated (180ms fade-in + rise / 170ms fade-out + sink + slight shrink).
- "Downloads & advanced" → "Show the beatmap info window after import" turns it off entirely (on by default).
- The window does not steal focus (`ShowActivated=false`) and does not affect osu!'s input; it sticks below the main window, follows it when it moves or resizes, and falls back to the right side of the main window when there is no room.

Data source: `CS / OD / AR / HP / object count / actual BPM` are parsed at import time from the `.osu` header inside the `.osz` (the osu! API's beatmapset endpoint does not provide these directly); parsing happens on a background thread and shares a single extraction with the pattern analysis.

## Interface language

The UI is fully bilingual (Chinese / English) and switches at runtime — no restart:

- **Interface → Language**: `System default` / `简体中文` / `English`. "System default" picks Chinese on a
  Chinese Windows and English everywhere else.
- The English build ships with `English` pre-selected, the standard build with `System default`; the choice is
  saved to `settings.json`.
- Everything follows the switch: buttons, tooltips, the status line, card details, skill radar labels and the
  beatmap info window. Logs and code comments stay Chinese — they are for troubleshooting, not for the UI.

## UI font

Numbers and Latin letters in the UI use **Outfit** (`Fonts/`, three faces — Regular / SemiBold / Bold — compiled into the assembly as WPF Resources). Like the Torus used by osu!(lazer) it is a geometric sans-serif with a similar look, but it is licensed under **SIL OFL 1.1** and can be freely distributed with the program (Torus is a commercial font and needs a license to be embedded in a release package).

Outfit only has Latin letters and digits and **has no CJK glyphs**: Chinese is still rendered by the system font `Microsoft YaHei UI` (WPF falls back per character), the same idea as osu!lazer using Noto to cover CJK.

- "Downloads & advanced" → "Use the bundled font (Outfit)" can be switched off at any time; it **takes effect immediately, no restart needed**.
- The startup log has a line `字体：内置 Outfit（N 字形）；汉字回退 Microsoft YaHei UI` ("font: bundled Outfit (N glyphs); CJK fallback Microsoft YaHei UI");
  if the font fails to load it prints `不可用` ("unavailable") and automatically falls back to the system font (the UI will not turn into boxes).
- `google/fonts` only ships variable fonts, and WPF does not honor variable axes, so the repository contains static faces cut with fonttools `varLib.instancer`; see `Fonts/ATTRIBUTION.md` for how they were cut.

## Build

Requires the .NET 8 SDK:

```powershell
dotnet build OsuRoam.csproj -c Release

# standard build: one self-contained exe (Fonts\*.ttf are compiled in as WPF Resources)
dotnet publish OsuRoam.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=none -p:RoamFlavor=zh -o bin\v1.5.0-zh

# English build
dotnet publish OsuRoam.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=none -p:RoamFlavor=en -o bin\v1.5.0-en

# both packages also ship tosu.exe next to the exe
copy tosu.exe bin\v1.5.0-zh\
copy tosu.exe bin\v1.5.0-en\
```

`PublishSingleFile` bundles the whole .NET runtime into `OsuRoam.exe`; six native WPF / audio DLLs
(`wpfgfx_cor3`, `PresentationNative_cor3`, `D3DCompiler_47_cor3`, `PenImc_cor3`, `vcruntime140_cor3`,
`wasmtime`) stay next to it, which is why a package is nine entries instead of a few hundred.

`-p:RoamFlavor=en` only adds the `EN_BUILD` constant, which flips the default UI language and the download
sources. Delete `obj\` and `bin\Release\` when switching flavors: incremental compilation does not reliably
notice a `DefineConstants` change, and a release package must not accidentally ship the other flavor's defaults.

Before publishing, make sure no `OsuRoam.exe` is running; publishing will fail on a file lock.

## Project structure

```
MainWindow.xaml(.cs)          Main window and interaction, roam loop
HistoryWindow.xaml(.cs)       Recommendation history window
Models/AppSettings.cs         Settings model, tag parsing, quick filter semantics
Models/OsuData.cs             osu! API data structures
Services/OsuApiClient.cs      OAuth, user / scores / search (sort rotation)
Services/RecommendationEngine.cs  Candidate filtering and sorting
Services/ManiaPatternAnalyzer.cs  mania pattern classification (ported MMA / Interlude algorithm)
Services/PatternLibrary.cs    Pattern library, tag inference, pattern preference
Services/ArchiveStore.cs      Recommendation history records
Services/BeatmapDownloadService.cs  .osz download and import
Services/TosuClient.cs        tosu WebSocket state
Services/TosuProcessManager.cs    tosu process management
Services/SettingsStore.cs     Settings read/write
Services/SecretProtector.cs   DPAPI encryption
Services/AppLog.cs            Logging
Services/Loc.cs               UI language switching (DynamicResource based)
Services/LocTable.cs          Chinese / English strings
Services/Flavor.cs            Build flavor: default language and download sources
Tests/                        Console project for local verification
```

## Known limitations

- Search is capped at 8 pages / roughly 400 candidates; if the pool is too small, widen the star rating range or lower exploration.
- API tags are free text written by mappers, not a strict music classification field, so dirty data and inconsistent spellings are common.
- Windows only (WPF / `net8.0-windows`).
- The download sources are third-party mirrors; when a site goes down you need to switch the mirror template in "Downloads & advanced".

---

# 中文

osu! 谱面漫游推荐工具。Windows / WPF / .NET 8。**一个仓库，两个构建**：

| 构建 | 界面语言 | 默认下载源 |
|---|---|---|
| `osu-roam-v1.5.0-portable.zip` | 跟随系统（中文系统即中文） | sayobot 镜像（国内） |
| `osu-roam-v1.5.0-en-portable.zip` | 英文 | catboy / beatconnect（国际） |

两份代码完全一致，只有编译期常量（`EN_BUILD`）和下载源默认值不同 —— 国内镜像对国外玩家是负担，
国际源对国内网络也不合适，所以干脆各给一份，省得用户在设置里纠结。两个版本都自带语言切换
（「界面」→「界面语言」），只是出厂默认不同；各自的 `data` 目录互不相干，**分开放、别覆盖安装**。

osu! Roam 通过 osu! Web API 读取玩家资料和成绩样本，建立「星级 / BPM / 时长 / 谱师」偏好画像，再按星级区间、年份、时长、简易筛选和 tag 生成一个有上限的推荐池。配合 [tosu](https://github.com/KotRikD/tosu) 可以做到 **开局预下载下一首、结算后自动导入下一首** 的连续漫游。

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

1. 解压 `osu-roam-v1.5.0-portable.zip`（从 [Releases](https://github.com/lEttEr93/OsuRoam/releases/latest) 下载；不用另装 .NET，运行时已打进 `OsuRoam.exe`，[tosu](https://github.com/tosuapp/tosu) 的 `tosu.exe` 也已经在包里）。
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

- 中文版默认从 sayobot 多镜像取 `.osz`（`dl.sayobot.cn` + `txy1.sayobot.cn`，模板里的 `{setId}` 会被替换）；英文版默认是 `catboy.best` + `beatconnect.io`。两边都能在「下载与高级设置」里改。
- 部分国际镜像会拒绝空 `User-Agent`，所以请求一律带 `osu-roam/<version>`。
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
- **下边缘的 alpha 直接烘进位图**：下面 70% 用 smoothstep（`t²(3−2t)`）渐隐到全透明。
  叠加式渐变盖不住硬边（图的下边缘在屏幕上是一条横线，位置随卡片宽高比变），
  只有把 alpha 写进像素才真正没有边；
- 再叠一张**铺满整卡的模糊副本**（`Stretch=UniformToFill`，0.18），
  让卡片下半部分也带曲绘的颜色，而不是纯黑；
- 最后压一层 44%→86% 的压暗渐变保证文字对比度（不再到全不透明，否则下半部分会被压死黑）；
- 模糊不挂 `BlurEffect`：曲绘会先缩到 **96px 宽**再放大到卡片宽度，放大本身就是模糊。
  这样卡片四角仍然是精确的圆角（`BlurEffect` 会把圆角裁切糊成直角），而且没有逐帧的离屏渲染开销；
- 卡片左侧留一个 40px 的 ▶ 预览按钮（原来它叠在缩略图上，缩略图没了就得自己露出来）。

迷你播放器（整条 64px 高）和信息窗（整块面板）用同一套逻辑，也不再放小方图：

- **迷你播放器**：整条标题栏铺曲绘。这条只有 64px 高，整幅是按**高度对齐**填满的，
  左右会留出空档，所以这一层用的是「左右各 22% 横向羽化」的版本（`BackdropBar`），
  边缘融进后面那层模糊底；纵向不渐隐。
- **信息窗**：整块面板铺曲绘，和卡片一样是「模糊底 + 顶部整幅（纵向 alpha 渐隐）+ 压暗」。

三种背影位图都在 `CoverLoader` 里一次生成（96px 宽）：`Backdrop`（纵向渐隐）、
`BackdropBar`（横向羽化）、`Cover`（256px 清晰版），渐隐/羽化都按 smoothstep 烘进像素。

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

## 界面语言

界面中英双语，切换即时生效、不用重启：

- 「界面」→「界面语言」：`跟随系统` / `简体中文` / `English`。跟随系统时，中文 Windows 显示中文，其余显示英文。
- 英文版出厂预选 `English`，中文版预选 `跟随系统`；选择会存进 `settings.json`。
- 按钮、提示、状态栏、卡片详情、技能雷达标签、谱面信息窗全都跟着切。
- 日志和代码注释保持中文 —— 那是排障用的，不属于界面。

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

# 中文版：自包含单文件（Fonts\*.ttf 作为 WPF Resource 编进程序集）
dotnet publish OsuRoam.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=none -p:RoamFlavor=zh -o bin\v1.5.0-zh

# 英文版
dotnet publish OsuRoam.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=none -p:RoamFlavor=en -o bin\v1.5.0-en

# 两个包都把 tosu.exe 放在 exe 旁边
copy tosu.exe bin\v1.5.0-zh\
copy tosu.exe bin\v1.5.0-en\
```

`PublishSingleFile` 把整个 .NET 运行时打进了 `OsuRoam.exe`；六个 WPF / 音频的原生 DLL
（`wpfgfx_cor3`、`PresentationNative_cor3`、`D3DCompiler_47_cor3`、`PenImc_cor3`、`vcruntime140_cor3`、
`wasmtime`）留在 exe 外面，所以一个包里只有九个条目，而不是几百个。

`-p:RoamFlavor=en` 只多加一个 `EN_BUILD` 常量，用来切换界面默认语言和下载源。
**切风味前先删掉 `obj\` 和 `bin\Release\`**：增量编译不一定能察觉 `DefineConstants` 变了，
而发布包里混进另一个风味的默认值是要命的。

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
Services/Loc.cs               界面语言切换（基于 DynamicResource）
Services/LocTable.cs          中英文文案表
Services/Flavor.cs            构建风味：默认语言与下载源
Tests/                        本地验证用控制台工程
```

## 已知限制

- 搜索上限 8 页 / 约 400 个候选；池子太小可以把星级区间放宽或降探索度。
- API 的 tag 是谱师自由填写的文本，不是严格的音乐分类字段，脏数据和拼写不一致都常见。
- 仅 Windows（WPF / `net8.0-windows`）。
- 下载源是第三方镜像，站点挂掉时需要在「下载与高级设置」里换镜像模板。
