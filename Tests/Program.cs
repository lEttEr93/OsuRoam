using System.Text.Json;
using OsuRoam.Services;
using OsuRoam.Models;

testSuccessfulCompletion();
testAbortDoesNotComplete();
testFailureDoesNotComplete();
testPauseDoesNotStartOrComplete();
testResultsDetectedWithoutResultScreenState();
testStaleResultsFromPreviousPlayAreIgnored();
testQuotedExecutablePathIsNormalized();
await testArchiveStorePublishesCollectionChangesAsync();
testArchiveEntryDetailLayer();
testChangeKindDetection();
testTranslateStoryboardPathResolves();
testNoTemplateAnimatesFrozenTransforms();
testEmbeddedFont();
testUiFontIsDynamicResource();
testEmbeddedFontFamilyResolves();
testOszReaderReadsBeatmapText();
testBeatmapDetailParses();
testBeatmapDetailApproachRateFallsBackToOd();
testDifficultyColourSpectrum();
testPatternBarsSkipEmptyRows();
testDifficultyProfilesAreIndependent();
testUnlimitedDifficultyRange();
testManiaKeysAreFiltered();
testDuplicateBeatmapsetsAreCollapsed();
testRecommendationHistoryPrefersFreshCandidates();
testRecommendationHistoryIsNormalizedAndBounded();
testRecycledCandidatesAreFlagged();
testSearchSortPlanRotates();
testPatternAnalyzerDetectsStream();
testPatternAnalyzerDetectsJacks();
testPatternAnalyzerDetectsLnChart();
testPatternPreferenceShapesRanking();
testBpmDoesNotAffectRanking();
testPatternSelectionNormalizes();
testSelectedPatternShapesRanking();
testPatternPrefetchFillsLibrary();
testPatternSearchHints();
testCacheCleanerSkipsPoolItems();
testDirtyTagsAreFiltered();
testTagPoolComesFromWhitelist();
testTagAliasesCollapse();
testLegacyTagPoolMigratesToSeen();
testSeenTagsRejectJunk();
testTagPoolPayloadValidation();
testTagIntakeQueuesUnknownTags();
testTagSubmitGates();
testTagSubmitBodyFormat();
testCompoundTagsRemainTogether();
testCommonCompoundTagsUseLongestMatches();
testSimpleFiltersOnlyStrictAreAbsolute();
testSimpleFilterFitIsWeighted();
testStrictSimpleFiltersFilterCandidates();
testSimpleFilterSelectionStatesMatchSemantics();
testSimpleFilterLabelsFollowLanguage();
testLocalizationTablesAreInSync();
testLocalizationSwitchesAtRuntime();
testFlavorDownloadDefaults();
testInstalledLibraryParsesMetadata();
testSimpleFiltersUseApiGenreAndLanguageIds();
testTosuStringStateWorksInMultiplayerPayload();
testTosuSnapshotCarriesBeatmapMetadata();
testPlayVisibilityPredicate();
testTosuDashboardConfigIsDisabled();
testTosuExecutableResolution();
testOsuExecutableCandidatesSupportStable();
Console.WriteLine("All state transition, recommendation, mode profile, and tosu integration tests passed.");

static void testSuccessfulCompletion()
{
    TosuClient client = new();
    int started = 0;
    int succeeded = 0;
    client.PlayStarted += _ => started++;
    client.PlaySucceeded += _ => succeeded++;
    client.ProcessSnapshotJson(snapshot("play", paused: false, failed: false));
    client.ProcessSnapshotJson(snapshot("play", paused: true, failed: false));
    client.ProcessSnapshotJson(snapshot("play", paused: false, failed: false));
    client.ProcessSnapshotJson(snapshot("resultScreen", rank: "A", score: 123456));
    client.ProcessSnapshotJson(snapshot("resultScreen", rank: "A", score: 123456));
    assertEqual(1, started, "成功路径应只触发一次开始");
    assertEqual(1, succeeded, "成功路径应只触发一次结算");
}

static void testAbortDoesNotComplete()
{
    TosuClient client = new();
    int succeeded = 0;
    client.PlaySucceeded += _ => succeeded++;
    client.ProcessSnapshotJson(snapshot("play", paused: false, failed: false));
    client.ProcessSnapshotJson(snapshot("selectPlay"));
    assertEqual(0, succeeded, "中途退出不能触发成功结算");
}

static void testFailureDoesNotComplete()
{
    TosuClient client = new();
    int succeeded = 0;
    client.PlaySucceeded += _ => succeeded++;
    client.ProcessSnapshotJson(snapshot("play", paused: false, failed: false));
    client.ProcessSnapshotJson(snapshot("play", paused: false, failed: true));
    client.ProcessSnapshotJson(snapshot("resultScreen", rank: "F", score: 1234));
    assertEqual(0, succeeded, "失败结算不能触发成功事件");
}

static void testPauseDoesNotStartOrComplete()
{
    TosuClient client = new();
    int started = 0;
    int succeeded = 0;
    client.PlayStarted += _ => started++;
    client.PlaySucceeded += _ => succeeded++;
    client.ProcessSnapshotJson(snapshot("play", paused: true, failed: false));
    client.ProcessSnapshotJson(snapshot("selectPlay"));
    assertEqual(0, started, "暂停状态不能触发开始预下载");
    assertEqual(0, succeeded, "暂停后退出不能触发成功结算");
}

static void testResultsDetectedWithoutResultScreenState()
{
    // 跳过结算界面时 tosu 会直接从 play 跳到 selectPlay，结算数据仍在 payload 里。
    TosuClient client = new();
    int succeeded = 0;
    client.PlaySucceeded += _ => succeeded++;
    client.ProcessSnapshotJson(snapshot("play", paused: false, failed: false));
    client.ProcessSnapshotJson(snapshot("selectPlay", rank: "A", score: 123456));
    assertEqual(1, succeeded, "跳过结算界面后仍应判定本局成功");
}

static void testStaleResultsFromPreviousPlayAreIgnored()
{
    TosuClient client = new();
    int succeeded = 0;
    client.PlaySucceeded += _ => succeeded++;

    client.ProcessSnapshotJson(snapshot("play"));
    client.ProcessSnapshotJson(snapshot("resultScreen", rank: "A", score: 111111));
    assertEqual(1, succeeded, "第一局正常结算应触发一次");

    // 第二局中途退出：tosu 仍保留上一局的成绩，不能当成新结算。
    client.ProcessSnapshotJson(snapshot("selectPlay"));
    client.ProcessSnapshotJson(snapshot("play", rank: "A", score: 111111));
    client.ProcessSnapshotJson(snapshot("selectPlay", rank: "A", score: 111111));
    assertEqual(1, succeeded, "上一局残留的成绩不能重复触发结算");
}

static void testQuotedExecutablePathIsNormalized()
{
    string expected = @"C:\Users\me\AppData\Local\osulazer\osu!.exe";
    assertText(expected,
        BeatmapDownloadService.NormalizeExecutablePath("\"C:\\Users\\me\\AppData\\Local\\osulazer\\osu!.exe\""),
        "带引号的路径必须去掉引号");
    assertText(expected,
        BeatmapDownloadService.NormalizeExecutablePath("  \"C:\\Users\\me\\AppData\\Local\\osulazer\\osu!.exe\"  "),
        "首尾空白和引号都要清掉");
    assertText(string.Empty, BeatmapDownloadService.NormalizeExecutablePath(null), "null 应清理成空串");
    assertText(string.Empty, BeatmapDownloadService.NormalizeExecutablePath("   "), "空白应清理成空串");

    string expanded = BeatmapDownloadService.NormalizeExecutablePath("\"%LOCALAPPDATA%\\osulazer\\osu!.exe\"");
    assertText(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osulazer", "osu!.exe"),
        expanded,
        "环境变量应展开");
}

static async Task testArchiveStorePublishesCollectionChangesAsync()
{
    string directory = Path.Combine(Path.GetTempPath(), "osuroam-archive-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        ArchiveStore store = new(directory);
        int changes = 0;
        store.Entries.CollectionChanged += (_, _) => changes++;

        RecommendationItem item = new()
        {
            BeatmapId = 11,
            BeatmapsetId = 22,
            Artist = "artist",
            Title = "title",
            DifficultyName = "4K",
            Mapper = "mapper",
            Stars = 3,
            Bpm = 180,
            LengthSeconds = 120,
            RankedYear = 2024,
            Score = 0,
            ReasonStarFit = 0.5
        };
        RecommendationArchiveEntry entry = store.Add(item);
        assertEqual(1, changes, "新增历史必须通知已绑定的面板");
        assertEqual(1, store.Entries.Count, "新增后应有一条记录");
        assertTrue(ReferenceEquals(entry, store.Entries[0]), "Add 应返回刚插入的那条记录");

        store.Add(item);
        assertEqual(1, store.Entries.Count, "同一张谱面不应重复记录");
        assertEqual(3, changes, "重复推荐应先删除再插入，两次都要通知面板");

        await store.SaveAsync();
        ArchiveStore reloaded = new(directory);
        await reloaded.LoadAsync();
        assertEqual(1, reloaded.Entries.Count, "重新读取后应保留记录");
        assertEqual(11, reloaded.Entries[0].BeatmapId, "重新读取后内容应一致");
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void testArchiveEntryDetailLayer()
{
    ManiaPattern pattern = new()
    {
        BeatmapId = 11,
        BeatmapsetId = 22,
        KeyCount = 4,
        CoreShares = new() { ["Stream"] = 0.6, ["Jacks"] = 0.4 },
        SpecificShares = new() { ["Jumpstream"] = 0.5 },
        SkillAxes = [1, 2, 3, 4, 5, 6, 7],
        SkillSource = "MinaCalc 23.1",
        ModeTag = "RC",
        Category = "Stream",
        LnRatio = 0.1,
        NotesPerSecond = 7.5
    };

    RecommendationItem item = sampleRecommendationItem();
    item.ApplyPattern(pattern);
    RecommendationArchiveEntry entry = RecommendationArchiveEntry.FromItem(item);

    assertTrue(ReferenceEquals(pattern, entry.Pattern), "历史记录应带上键型快照");

    // 6 个核心键型的占比和为 1（此消彼长），只能进直方图；雷达图只留 7 轴技能。
    assertEqual(7, entry.RadarLabels.Count, "雷达应只有 7 轴技能");
    assertNear(3.0, entry.RadarValues[2], "技能雷达应使用 SkillAxes");
    assertEqual(6, entry.PatternBars.Count, "直方图应有 6 个核心键型");
    assertNear(0.6, entry.PatternBars[0], "直方图第一格应来自 CoreShares");
    assertEqual(6, entry.PatternBarLabels.Count, "直方图标签数应与数据对齐");

    entry.ToggleDetail();
    assertTrue(entry.IsDetailVisible && !entry.IsSummaryVisible, "详情层与摘要层必须互斥");
    assertTrue(entry.DetailLines.Any(line => line.Contains("MinaCalc", StringComparison.Ordinal)),
        "详情应显示技能轴来源");
    assertTrue(!entry.DetailLines.Any(line => line.StartsWith("Jumpstream", StringComparison.Ordinal)),
        "各键型占比应交给直方图，不要再用文字重复一遍");

    // 没有键型数据时直方图为空，避免画出一排 0
    RecommendationArchiveEntry bare = new() { Mapper = "m", Stars = 3, Bpm = 180, LengthSeconds = 100, RankedYear = 2024 };
    assertEqual(0, bare.PatternBars.Count, "没有键型时直方图应为空");

    // history.json 要能存下键型快照，否则面板里永远是空雷达
    string json = JsonSerializer.Serialize(entry);
    RecommendationArchiveEntry? restored = JsonSerializer.Deserialize<RecommendationArchiveEntry>(json);
    assertTrue(restored?.Pattern is not null, "键型快照应能存进 history.json");
    assertNear(0.6, restored!.Pattern!.CoreShares["Stream"], "键型数据往返后应保持一致");
    assertNear(3.0, restored.Pattern.SkillAxes[2], "技能轴往返后应保持一致");

    // 没有键型数据时给提示，而不是一片空白雷达
    RecommendationArchiveEntry empty = new()
    {
        Mapper = "mapper",
        Stars = 3,
        Bpm = 180,
        LengthSeconds = 100,
        RankedYear = 2024
    };
    assertTrue(!empty.HasPattern, "没有键型时应标记为缺失");
    assertTrue(empty.DetailLines.Any(line => line.Contains("没有键型数据", StringComparison.Ordinal)),
        "缺键型数据时应给出提示");

    // 取消已选 tag 后高亮必须消失（推荐池和历史面板共用这套刷新逻辑）
    RecommendationArchiveEntry tagged = new() { Tags = ["stream", "ln"] };
    tagged.RefreshTagInclusion(["stream"]);
    assertTrue(tagged.TagItems.First(tag => tag.Name == "stream").IsIncluded, "命中已选 tag 时应高亮");
    assertTrue(!tagged.TagItems.First(tag => tag.Name == "ln").IsIncluded, "未命中的 tag 不应高亮");
    tagged.RefreshTagInclusion([]);
    assertTrue(!tagged.TagItems.Any(tag => tag.IsIncluded), "取消已选 tag 后历史记录的高亮必须一起消失");
}

static RecommendationItem sampleRecommendationItem() => new()
{
    BeatmapId = 11,
    BeatmapsetId = 22,
    Artist = "artist",
    Title = "title",
    DifficultyName = "4K",
    Mapper = "mapper",
    Stars = 3,
    Bpm = 180,
    LengthSeconds = 120,
    RankedYear = 2024,
    Score = 0,
    ReasonStarFit = 0.5
};

static void testChangeKindDetection()
{
    MapIdentity empty = MapIdentity.Empty;
    MapIdentity songA = MapIdentity.From(1001, 100, "artist", "A");
    MapIdentity songAOtherDiff = MapIdentity.From(1002, 100, "artist", "A");
    MapIdentity songB = MapIdentity.From(2001, 200, "artist", "B");

    assertKind(ChangeKind.Initial, ChangeKindDetector.Detect(empty, empty), "空身份不应触发入场");
    assertKind(ChangeKind.Initial, ChangeKindDetector.Detect(empty, songA), "首次载入是 Initial");
    assertKind(ChangeKind.Song, ChangeKindDetector.ForEntrance(empty, songA), "首次载入按换歌入场");
    assertKind(ChangeKind.Mod, ChangeKindDetector.Detect(songA, songA), "同一张谱面重算应是 Mod");
    assertKind(ChangeKind.Difficulty, ChangeKindDetector.Detect(songA, songAOtherDiff), "同 set 换难度");
    assertKind(ChangeKind.Song, ChangeKindDetector.Detect(songA, songB), "不同 set 是换歌");

    // 没有 beatmapsetId 时 SongKey 退化成「作者 - 标题」，仍能区分换歌
    MapIdentity metaA = MapIdentity.From(3001, 0, "artist", "A");
    MapIdentity metaA2 = MapIdentity.From(3002, 0, "artist", "A");
    assertKind(ChangeKind.Difficulty, ChangeKindDetector.Detect(metaA, metaA2), "退化 key 下同曲换难度");
    assertKind(ChangeKind.Song, ChangeKindDetector.Detect(metaA2, MapIdentity.From(3003, 0, "artist", "C")),
        "退化 key 下换歌仍是 Song");

    // 缺失字段不能把一次续报判成换歌
    assertKind(ChangeKind.Difficulty, ChangeKindDetector.Detect(
        MapIdentity.From(4001, 0, "artist", "D"),
        MapIdentity.From(4002, 0, "artist", "D")), "字段缺失不等于换歌");
}

/// <summary>
/// XAML 里的入场动画用 (UIElement.RenderTransform).(TranslateTransform.Y) 这种属性路径。
/// 路径写错只会在运行时抛，编译期发现不了，所以在这里真的跑一次 Storyboard。
/// </summary>
static void testTranslateStoryboardPathResolves()
{
    Exception? captured = null;
    Thread thread = new(() =>
    {
        try
        {
            System.Windows.Controls.Border border = new()
            {
                RenderTransform = new System.Windows.Media.TranslateTransform(),
                Opacity = 0
            };

            System.Windows.Media.Animation.Storyboard storyboard = new();

            System.Windows.Media.Animation.DoubleAnimation fade =
                new(0, 1, TimeSpan.FromMilliseconds(30));
            System.Windows.Media.Animation.Storyboard.SetTarget(fade, border);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(
                fade, new System.Windows.PropertyPath("Opacity"));
            storyboard.Children.Add(fade);

            System.Windows.Media.Animation.DoubleAnimation slide =
                new(7, 0, TimeSpan.FromMilliseconds(30));
            System.Windows.Media.Animation.Storyboard.SetTarget(slide, border);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(
                slide,
                new System.Windows.PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            storyboard.Children.Add(slide);

            storyboard.Begin();
        }
        catch (Exception exception)
        {
            captured = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (captured is not null)
        throw new InvalidOperationException($"入场动画的属性路径解析失败：{captured.Message}", captured);
}

/// <summary>
/// 1.3.0 的崩溃教训：DataTemplate 里声明的 Freezable 会被模板冻结，再去动画它的子属性
/// 会在卡片实例化时抛 XamlParseException。这条用例扫描 XAML，禁止动画 RenderTransform 子属性
/// （入场/翻面动画必须走 Controls/Motion.cs，在运行时新建可变 Transform）。
/// </summary>
static void testNoTemplateAnimatesFrozenTransforms()
{
    string? root = null;
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    for (int depth = 0; depth < 6 && dir is not null; depth++, dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "OsuRoam.csproj")))
        {
            root = dir.FullName;
            break;
        }
    }
    if (root is null)
        return;

    foreach (string file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
    {
        if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            continue;

        int line = 0;
        foreach (string text in File.ReadLines(file))
        {
            line++;
            if (!text.Contains("Storyboard.TargetProperty", StringComparison.Ordinal))
                continue;
            if (!text.Contains("RenderTransform", StringComparison.Ordinal))
                continue;

            throw new InvalidOperationException(
                $"{Path.GetFileName(file)}:{line} 在动画 RenderTransform 子属性；" +
                "模板内声明的 Transform 会被冻结，运行时会抛 XamlParseException。请改用 Controls/Motion.cs。");
        }
    }
}

static void assertKind(ChangeKind expected, ChangeKind actual, string message){
    if (expected != actual)
        throw new InvalidOperationException($"{message}：expected={expected}, actual={actual}");
}

/// <summary>从测试输出目录往上找仓库根（认 OsuRoam.csproj）。</summary>
static string? findProjectRoot()
{
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    for (int depth = 0; depth < 6 && dir is not null; depth++, dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "OsuRoam.csproj")))
            return dir.FullName;
    }
    return null;
}

/// <summary>
/// 内置字体必须以 WPF Resource 的形式编进程序集，而且文件里的家族名要和
/// <see cref="Typography.FamilyName"/> 一致 —— Typography 按这个名字找家族，
/// 名字对不上就静默回退系统字体（1.3.2 踩过这个坑）。
/// </summary>
static void testEmbeddedFont()
{
    string? root = findProjectRoot();
    if (root is null)
        return;

    string project = File.ReadAllText(Path.Combine(root, "OsuRoam.csproj"));
    if (!project.Contains(@"Fonts\*.ttf", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(@"OsuRoam.csproj 里没有把 Fonts\*.ttf 作为 Resource 编进程序集");

    string[] files = Directory.GetFiles(Path.Combine(root, "Fonts"), "*.ttf");
    if (files.Length == 0)
        throw new InvalidOperationException("Fonts/ 里没有 ttf");

    foreach (string path in files)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (!isTrueType(bytes) && !isOpenTypeCff(bytes))
            throw new InvalidOperationException($"Fonts/{Path.GetFileName(path)} 不是可识别的字体文件（魔数不对）");

        if (!containsAscii(bytes, Typography.FamilyName) && !containsUtf16BigEndian(bytes, Typography.FamilyName))
            throw new InvalidOperationException(
                $"Fonts/{Path.GetFileName(path)} 的 name 表里找不到家族名 {Typography.FamilyName}，和 Typography.FamilyName 对不上");
    }

    if (!project.Contains("OFL", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("csproj 没有把 OFL 授权文件一起打进输出（SIL OFL 要求随字体附带）");
}

/// <summary>
/// 字体资源必须是 DynamicResource：设置面板里的开关要在不重启的情况下立刻生效。
/// 另外 App.xaml 里不能直接写 pack URI 形式的字体 —— 字符串建的 FontFamily 没有 BaseUri，
/// Typeface.TryGetGlyphTypeface 一律返回 false（见 Services/Typography.cs 的注释）。
/// </summary>
static void testUiFontIsDynamicResource()
{
    string? root = findProjectRoot();
    if (root is null)
        return;

    string app = File.ReadAllText(Path.Combine(root, "App.xaml"));
    int keyIndex = app.IndexOf("x:Key=\"UiFont\"", StringComparison.Ordinal);
    if (keyIndex < 0)
        throw new InvalidOperationException("App.xaml 里没有 UiFont 字体资源");

    int valueStart = app.IndexOf('>', keyIndex);
    int valueEnd = app.IndexOf("</FontFamily>", valueStart, StringComparison.Ordinal);
    if (valueStart < 0 || valueEnd < 0)
        throw new InvalidOperationException("App.xaml 的 UiFont 不是内联的 FontFamily 元素");

    string fontValue = app[(valueStart + 1)..valueEnd].Trim();
    if (fontValue.Contains('#'))
        throw new InvalidOperationException(
            $"App.xaml 的 UiFont 写成了 {fontValue}：字符串形式的 pack URI 建不出带 BaseUri 的 FontFamily，解析不出字形面；应交由 Services/Typography.cs");

    if (app.Contains("Value=\"Segoe UI\"", StringComparison.Ordinal))
        throw new InvalidOperationException("App.xaml 里还残留硬编码的 Segoe UI");

    foreach (string window in new[] { "MainWindow.xaml", "HistoryWindow.xaml", "MapInfoWindow.xaml" })
    {
        string text = File.ReadAllText(Path.Combine(root, window));
        if (!text.Contains("FontFamily=\"{DynamicResource UiFont}\"", StringComparison.Ordinal))
            throw new InvalidOperationException($"{window} 没有用 DynamicResource 绑定 UiFont");
    }
}

/// <summary>端到端：程序集里真的能解析出 <see cref="Typography.FamilyName"/> 家族（不只是文件存在）。</summary>
static void testEmbeddedFontFamilyResolves()
{
    Exception? captured = null;
    bool found = false;
    Thread thread = new(() =>
    {
        try
        {
            ICollection<System.Windows.Media.FontFamily> families =
                System.Windows.Media.Fonts.GetFontFamilies(new Uri("pack://application:,,,/OsuRoam;component/Fonts/"));
            found = families.Any(family => family.FamilyNames.Values.Any(
                name => name.Equals(Typography.FamilyName, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception exception)
        {
            captured = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (captured is not null)
        throw new InvalidOperationException($"读取内置字体失败：{captured.Message}", captured);
    if (!found)
        throw new InvalidOperationException($@"程序集里解析不出 {Typography.FamilyName} 家族（Fonts\*.ttf 没编进去？）");
}

/// <summary>
/// 谱面详情解析：导入时从 .osz 里的 .osu 头读 CS/OD/AR/HP/物件数/BPM。
/// 这些字段 API 的 beatmapset 接口不直接给，但信息窗要用。
/// </summary>
static void testBeatmapDetailParses()
{
    const string osu = """
        osu file format v14

        [General]
        Mode: 3
        PreviewTime: 1000

        [Metadata]
        Title:Test Song
        Artist:Test Artist
        Creator:Tester
        Version:4K Insane
        BeatmapID:123

        [Difficulty]
        CircleSize:4
        OverallDifficulty:8
        HPDrainRate:6
        ApproachRate:9.5

        [TimingPoints]
        0,333.333333333333,4,2,1,60,1,0
        1000,-100,4,2,1,60,0,0

        [HitObjects]
        64,192,1000,1,0,0:0:0:0:
        64,192,1200,1,0,0:0:0:0:
        64,192,1400,128,0,1600:0:0:0:0:0:0:0:
        192,192,1800,2,0,B|300:192|400:192,1,140
        256,192,2400,8,0,2400
        """;

    BeatmapDetail detail = BeatmapDetailReader.Parse(osu)
        ?? throw new InvalidOperationException("解析 .osu 返回了 null");

    assertEqual(3, detail.Mode, "模式应解析为 mania");
    assertEqual(4, detail.KeyCount, "mania 的键数取自 CircleSize");
    assertNear(4.0, detail.CircleSize, "CS 解析错误");
    assertNear(8.0, detail.OverallDifficulty, "OD 解析错误");
    assertNear(9.5, detail.ApproachRate, "AR 解析错误");
    assertNear(6.0, detail.HPDrainRate, "HP 解析错误");
    if (Math.Abs(detail.Bpm - 180) > 0.01)
        throw new InvalidOperationException($"BPM 应由第一个非继承时间点算出 180，实际 {detail.Bpm}");
    assertEqual(5, detail.ObjectCount, "物件总数解析错误");
    assertEqual(2, detail.CircleCount, "单点数解析错误");
    assertEqual(1, detail.HoldCount, "长条数解析错误");
    assertEqual(1, detail.SliderCount, "滑条数解析错误");
    assertEqual(1, detail.SpinnerCount, "转盘数解析错误");
    assertText("mania 4K", detail.ModeDisplay, "模式显示名错误");

    if (BeatmapDetailReader.Parse(string.Empty) is not null)
        throw new InvalidOperationException("空文本应返回 null");
}

/// <summary>osu! 的规矩：没写 ApproachRate 时 AR 等于 OD。</summary>
static void testBeatmapDetailApproachRateFallsBackToOd()
{
    const string osu = """
        osu file format v14

        [General]
        Mode: 0

        [Difficulty]
        CircleSize:3.6
        OverallDifficulty:7.2
        HPDrainRate:5

        [HitObjects]
        256,192,1000,1,0,0:0:0:0:
        """;

    BeatmapDetail detail = BeatmapDetailReader.Parse(osu)
        ?? throw new InvalidOperationException("解析 .osu 返回了 null");
    assertNear(7.2, detail.ApproachRate, "缺 ApproachRate 时 AR 应回退到 OD");
    assertEqual(0, detail.KeyCount, "非 mania 不应有键数");
    assertText("osu", detail.ModeDisplay, "模式显示名错误");
}

/// <summary>星级色标：低星偏蓝、高星偏紫暗，异常输入给中性灰。</summary>
static void testDifficultyColourSpectrum()
{
    System.Windows.Media.Color blue = OsuRoam.Controls.DifficultyColour.Sample(0);
    System.Windows.Media.Color mid = OsuRoam.Controls.DifficultyColour.Sample(3.5);
    System.Windows.Media.Color top = OsuRoam.Controls.DifficultyColour.Sample(10);

    assertEqual((byte)0x42, blue.R, "0★ 应为 osu 的起始蓝");
    assertEqual((byte)0x7C, mid.R, "3.5★ 应为绿色档");
    assertEqual((byte)0x18, top.R, "10★ 应为深蓝档");

    System.Windows.Media.Color unknown = OsuRoam.Controls.DifficultyColour.Sample(double.NaN);
    if (unknown.R == 0 && unknown.G == 0 && unknown.B == 0)
        throw new InvalidOperationException("NaN 星级不应得到全黑颜色");
}

/// <summary>从真 .osz 里挑出指定 beatmapId 的 .osu（导入流程就是走这条路）。</summary>
static void testOszReaderReadsBeatmapText()
{
    string directory = Path.Combine(Path.GetTempPath(), $"osuroam-osz-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        string oszPath = Path.Combine(directory, "1411397.osz");
        using (System.IO.Compression.ZipArchive archive =
               System.IO.Compression.ZipFile.Open(oszPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            writeEntry(archive, "other.osu", """
                osu file format v14
                [Metadata]
                BeatmapID:999
                Title:Other
                """);
            writeEntry(archive, "target.osu", """
                osu file format v14

                [General]
                Mode: 3

                [Metadata]
                BeatmapID:5075177
                Title:Target

                [Difficulty]
                CircleSize:4
                OverallDifficulty:8

                [TimingPoints]
                0,333.333333333333,4,2,1,60,1,0

                [HitObjects]
                64,192,1000,1,0,0:0:0:0:
                """);
        }

        if (!OszReader.TryReadBeatmapText(oszPath, 5075177, out string text, out string entry))
            throw new InvalidOperationException("应从 .osz 里读出 BeatmapID=5075177 的 .osu");
        if (!entry.EndsWith("target.osu", StringComparison.Ordinal))
            throw new InvalidOperationException($"读错了文件：{entry}");
        if (!OszReader.HasBeatmapId(text, 5075177))
            throw new InvalidOperationException(".osu 文本里应能找到 BeatmapID:5075177");

        if (!OszReader.TryReadBeatmapText(oszPath, 0, out _, out _))
            throw new InvalidOperationException("beatmapId=0 时应取第一个 .osu");

        if (OszReader.TryReadBeatmapText(oszPath, 424242, out _, out _) && !OszReader.HasBeatmapId("", 424242))
        {
            // 已经有两个候选、又都不匹配时应该失败（除非只有一个候选的回退分支）。
            throw new InvalidOperationException("找不到匹配 id 且候选多于一个时不应成功");
        }
    }
    finally
    {
        try { Directory.Delete(directory, true); } catch { }
    }
}

static void writeEntry(System.IO.Compression.ZipArchive archive, string name, string content)
{
    System.IO.Compression.ZipArchiveEntry entry = archive.CreateEntry(name);
    using StreamWriter writer = new(entry.Open());
    writer.Write(content);
}

/// <summary>直方图不该画 0% 的键型：0% 和四舍五入后是 0% 的行都跳过。</summary>
static void testPatternBarsSkipEmptyRows()
{
    string[] labels = ["流", "和弦流", "叠键", "协调", "密度", "杂项"];
    double[] shares = [0.46, 0.31, 0, 0.10, 0.001, 0.0049];

    int[] rows = OsuRoam.Controls.PatternBars.VisibleRowIndices(labels, shares);
    if (!rows.SequenceEqual([0, 1, 3]))
        throw new InvalidOperationException($"0% 的行应被跳过，实际留下 [{string.Join(",", rows)}]");

    // 1% 及以上要保留：0.006 * 100 = 0.6 → 四舍五入 1%
    int[] kept = OsuRoam.Controls.PatternBars.VisibleRowIndices(labels, [0.006, 0.994, 0, 0, 0, 0]);
    if (!kept.SequenceEqual([0, 1]))
        throw new InvalidOperationException($"0.6% 应四舍五入成 1% 并保留，实际 [{string.Join(",", kept)}]");

    // 全是 0 时不留任何行（卡片高度随之收缩）
    int[] none = OsuRoam.Controls.PatternBars.VisibleRowIndices(labels, [0, 0, 0, 0, 0, 0]);
    if (none.Length != 0)
        throw new InvalidOperationException("全部是 0 时不应有可见行");
}

static bool isTrueType(byte[] bytes) =>
    bytes.Length > 4 && bytes[0] == 0x00 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00;

static bool isOpenTypeCff(byte[] bytes) =>
    bytes.Length > 4 && bytes[0] == 'O' && bytes[1] == 'T' && bytes[2] == 'T' && bytes[3] == 'O';

static bool containsAscii(byte[] bytes, string text) =>
    bytes.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(text)) >= 0;

static bool containsUtf16BigEndian(byte[] bytes, string text) =>
    bytes.AsSpan().IndexOf(System.Text.Encoding.BigEndianUnicode.GetBytes(text)) >= 0;

static void testDifficultyProfilesAreIndependent()
{
    AppSettings settings = new();
    settings.Mode = "osu";
    settings.GetDifficultyRange().MinimumStars = 3.2;
    settings.GetDifficultyRange().MaximumStars = 3.8;
    settings.Mode = "mania";
    settings.ManiaKeys = 4;
    settings.GetDifficultyRange().MinimumStars = 4.7;
    settings.GetDifficultyRange().MaximumStars = 5.3;
    settings.ManiaKeys = 7;
    settings.GetDifficultyRange().MinimumStars = 2.2;
    settings.GetDifficultyRange().MaximumStars = 2.8;

    assertNear(3.2, settings.DifficultyRanges["osu"].MinimumStars, "standard 档案不应被 mania 覆盖");
    assertNear(4.7, settings.DifficultyRanges["mania-4k"].MinimumStars, "mania 4K 档案应独立");
    assertNear(2.2, settings.DifficultyRanges["mania-7k"].MinimumStars, "mania 7K 档案应独立");
}

static void testUnlimitedDifficultyRange()
{
    DifficultyRangeSettings finite = new() { MinimumStars = 2.4, MaximumStars = 11.0 };
    AppSettings.NormalizeDifficultyRange(finite);
    assertNear(11.0, finite.MaximumStars, string.Empty);

    DifficultyRangeSettings range = new() { MinimumStars = 2.4, MaximumStars = 12.0 };
    AppSettings.NormalizeDifficultyRange(range);
    assertNear(2.4, range.MinimumStars, string.Empty);
    assertNear(AppSettings.UnlimitedMaximumStars, range.MaximumStars, string.Empty);
    if (!AppSettings.IsUnlimitedMaximumStars(range.MaximumStars))
        throw new InvalidOperationException();
}

static void testManiaKeysAreFiltered()
{
    AppSettings settings = new()
    {
        Mode = "mania",
        ManiaKeys = 4,
        MinimumYear = 2007,
        MaximumYear = 2030,
        MinimumLengthSeconds = 0,
        MaximumLengthSeconds = 1000,
        ExcludeRecentlyPlayed = false
    };
    PlayerProfile profile = new(new OsuUser(), [], [], 1, 10, 180, 120, 0,
        new Dictionary<string, int>(), new HashSet<int>());
    List<OsuBeatmapset> sets =
    [
        maniaSet(400, 4001, 4),
        maniaSet(700, 7001, 7)
    ];
    RecommendationEngine engine = new();
    IReadOnlyList<RecommendationItem> fourKey = engine.Rank(sets, profile, settings, 1, 10, new HashSet<int>(), 10);
    assertEqual(1, fourKey.Count, "mania 4K 应只留下一个候选");
    assertEqual(400, fourKey[0].BeatmapsetId, "mania 4K 不应混入 7K");

    settings.ManiaKeys = 7;
    IReadOnlyList<RecommendationItem> sevenKey = engine.Rank(sets, profile, settings, 1, 10, new HashSet<int>(), 10);
    assertEqual(1, sevenKey.Count, "mania 7K 应只留下一个候选");
    assertEqual(700, sevenKey[0].BeatmapsetId, "mania 7K 不应混入 4K");
}

static void testDuplicateBeatmapsetsAreCollapsed()
{
    AppSettings settings = recommendationSettings();
    PlayerProfile profile = emptyProfile();
    List<OsuBeatmapset> sets =
    [
        maniaSet(400, 4001, 4),
        maniaSet(400, 4002, 4)
    ];

    IReadOnlyList<RecommendationItem> result = new RecommendationEngine().Rank(
        sets, profile, settings, 1, 10, new HashSet<int>(), 10);

    assertEqual(1, result.Count, "重复 beatmapset 只能生成一个推荐项");
    assertEqual(400, result[0].BeatmapsetId, "去重后应保留原谱面集");
}

static void testRecommendationHistoryPrefersFreshCandidates()
{
    AppSettings settings = recommendationSettings();
    PlayerProfile profile = emptyProfile();
    List<OsuBeatmapset> sets =
    [
        maniaSet(100, 1001, 4),
        maniaSet(200, 2001, 4),
        maniaSet(300, 3001, 4)
    ];

    IReadOnlyList<RecommendationItem> result = new RecommendationEngine().RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int>(), [100, 200], 2);

    assertEqual(2, result.Count, "推荐池应由新谱和必要的历史谱补满");
    assertEqual(300, result[0].BeatmapsetId, "存在新谱时应优先推荐新谱");
    assertEqual(100, result[1].BeatmapsetId, "新谱不足时应复用最久未推荐的谱面");
}

static void testRecommendationHistoryIsNormalizedAndBounded()
{
    AppSettings settings = new() { RecommendationHistory = [1, 2, 1, 0, 3] };
    settings.NormalizeRecommendationHistory();
    assertSequence([2, 1, 3], settings.RecommendationHistory, "历史应保留每个谱面最后出现的位置");

    settings.RecordRecommendation(2);
    assertSequence([1, 3, 2], settings.RecommendationHistory, "再次推荐后应将谱面移到历史末尾");

    settings.RecommendationHistory = Enumerable.Range(1, AppSettings.MaximumRecommendationHistory + 5).ToList();
    settings.NormalizeRecommendationHistory();
    assertEqual(AppSettings.MaximumRecommendationHistory, settings.RecommendationHistory.Count, "推荐历史必须受上限约束");
    assertEqual(6, settings.RecommendationHistory[0], "超出上限时应丢弃最旧记录");
}

static void testDirtyTagsAreFiltered()
{
    AppSettings settings = new() { TagPool = ["anime", "?", "x", "this-tag-is-way-too-long-to-be-useful-as-a-filter-tag"] };
    settings.NormalizeTags();
    if (settings.TagPool.Contains("?", StringComparer.Ordinal) || settings.TagPool.Contains("x", StringComparer.Ordinal) ||
        settings.TagPool.Contains("this-tag-is-way-too-long-to-be-useful-as-a-filter-tag", StringComparer.Ordinal))
        throw new InvalidOperationException("dirty tags should not enter the tag pool");
    if (!settings.TagPool.Contains("anime", StringComparer.Ordinal))
        throw new InvalidOperationException("meaningful tags should remain in the tag pool");
}

static void testTagPoolComesFromWhitelist()
{
    AppSettings settings = new();
    settings.NormalizeTags();

    // 池子来自白名单，而不是把每个候选谱面的 tag 无脑攒进来。
    assertTrue(settings.TagPool.Contains("electronic"), "白名单条目应进入池子");
    assertTrue(settings.TagPool.Contains("drum and bass"), "白名单条目应进入池子");
    assertTrue(settings.TagPool.Contains("ln"), "键型 tag 目前在白名单里（1.5.2 再废弃）");
    assertTrue(!settings.TagPool.Contains("the"), "歌名里常见的普通词不应进入池子");
    assertTrue(!settings.TagPool.Contains("fa"), "两字母噪声不应进入池子");

    // 白名单顺序 = 审计顺序，排在最前面（见 Resources/tag-pool.json 的组内排序）。
    int electronic = settings.TagPool.IndexOf("electronic");
    int ln = settings.TagPool.IndexOf("ln");
    assertTrue(electronic >= 0 && ln > electronic, "白名单顺序应保留在池子前面");
}

static void testTagAliasesCollapse()
{
    // dnb / j-pop / jacks / game music 都是别名，解析后应归一成同一个名字。
    IReadOnlyList<string> parsed = AppSettings.ParseTags("dnb j-pop jacks game music");
    assertSequenceStrings(["drum and bass", "jack", "jpop", "video game music"], parsed, "别名应折叠成同一个 tag");
}

static void testLegacyTagPoolMigratesToSeen()
{
    // 旧版本的池子是从候选里无条件攒出来的，升级时要搬进 SeenTags 并重置池子。
    AppSettings settings = new()
    {
        TagPool = ["complextro", "\"canon\"", "(cv:", "x", "the"],
        TagPoolVersion = 0
    };
    settings.NormalizeTags();

    assertEqual(AppSettings.TagPoolFormat, settings.TagPoolVersion, "迁移后应写入格式版本");
    assertTrue(settings.SeenTags.Contains("complextro"), "白名单以外的正常 tag 应进 SeenTags");
    assertTrue(settings.TagPool.Contains("complextro"), "SeenTags 仍然可选");
    assertTrue(!settings.TagPool.Contains("\"canon\""), "引号包起来的噪声应被丢掉");
    assertTrue(!settings.TagPool.Contains("x"), "单字符不应进池子");
}

static void testSeenTagsRejectJunk()
{
    AppSettings settings = new();
    settings.RecordTags(["electronic", "artist", "#lookatcurren", "s", "12", "长条"]);
    assertTrue(settings.TagPool.Contains("electronic"), "白名单命中后仍应在池子里");
    assertTrue(settings.SeenTags.Contains("artist"), "白名单外的正常词应进 SeenTags");
    assertTrue(!settings.SeenTags.Contains("#lookatcurren"), "井号开头的噪声不应进 SeenTags");
    assertTrue(!settings.SeenTags.Contains("s"), "单字符不应进 SeenTags");
    assertTrue(!settings.SeenTags.Contains("12"), "纯数字不应进 SeenTags");
    assertTrue(settings.SeenTags.Contains("长条"), "中文 tag 应可用");
}

static void testTagPoolPayloadValidation()
{
    // 远程/缓存里的池子必须先过校验，坏内容不能把池子清空。
    assertTrue(!TagCatalog.TryParse("not json"), "非 JSON 应判为无效");
    assertTrue(!TagCatalog.TryParse("""{"version":2,"groups":[]}"""), "空分组应判为无效");
    assertTrue(!TagCatalog.TryParse("""{"version":2,"groups":[{"id":"genre","tags":[]}]}"""), "空 tag 列表应判为无效");
    assertTrue(TagCatalog.TryParse("""{"version":2,"groups":[{"id":"genre","label":"曲风","tags":["electronic"]}]}"""),
        "正常内容应判为有效");
}

static void testTagIntakeQueuesUnknownTags()
{
    TagIntake.Clear();
    TagIntake.Offer(["electronic", "complextro", "#junk", "x", "长条"]);
    IReadOnlyList<TagIntake.Entry> queued = TagIntake.Snapshot();
    assertTrue(queued.Any(entry => entry.Name == "complextro"), "白名单外的正常 tag 应进待审队列");
    assertTrue(queued.Any(entry => entry.Name == "长条"), "中文 tag 应进待审队列");
    assertTrue(!queued.Any(entry => entry.Name == "electronic"), "白名单命中不应进待审队列");
    assertTrue(!queued.Any(entry => entry.Name == "#junk"), "粗筛应拦住井号开头的噪声");

    TagIntake.Offer(["complextro"]);
    assertEqual(2, TagIntake.Snapshot().First(entry => entry.Name == "complextro").Count, "重复出现应累加次数");
    assertTrue(TagIntake.BuildPayloadJson().Contains("complextro"), "提交载荷应含待审 tag");

    TagIntake.Clear();
    assertEqual(0, TagIntake.Count, "清空后队列应为空");
}

static void testTagSubmitGates()
{
    DateTimeOffset now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    AppSettings settings = new() { TagSubmitEnabled = true };

    assertTrue(!TagSubmission.ShouldSubmit(settings, 100, now, hasToken: false), "没有凭据时不应提交");
    assertTrue(!TagSubmission.ShouldSubmit(settings, TagSubmission.MinimumPending - 1, now, hasToken: true),
        "没攒够条数不应提交");
    assertTrue(TagSubmission.ShouldSubmit(settings, TagSubmission.MinimumPending, now, hasToken: true),
        "攒够了且没冷却时应提交");

    settings.TagSubmitEnabled = false;
    assertTrue(!TagSubmission.ShouldSubmit(settings, 100, now, hasToken: true), "关掉开关后不应提交");
    settings.TagSubmitEnabled = true;

    settings.LastTagSubmitUtc = now - TimeSpan.FromDays(3);
    assertTrue(!TagSubmission.ShouldSubmit(settings, 100, now, hasToken: true), "冷却期内不应重复提交");
    settings.LastTagSubmitUtc = now - TimeSpan.FromDays(8);
    assertTrue(TagSubmission.ShouldSubmit(settings, 100, now, hasToken: true), "过了冷却应可以再提交");
}

static void testTagSubmitBodyFormat()
{
    string body = TagSubmission.BuildBody([
        new TagIntake.Entry { Name = "probe-alpha", Count = 2 }
    ]);
    assertTrue(!body.Contains("\\n"), "issue 正文里不能出现字面量反斜杠 n");
    assertTrue(body.Contains("```json"), "必须带 json 代码块，审核脚本靠它解析");
    assertTrue(body.Contains("\"tag\":\"probe-alpha\""), "载荷应含 tag 名");
    assertTrue(body.Contains("\"count\":2"), "载荷应含出现次数");
}

static void testCompoundTagsRemainTogether()
{
    IReadOnlyList<string> parsed = AppSettings.ParseTags("anime drum and bass hip_hop");
    assertSequenceStrings(["anime", "drum and bass", "hip hop"], parsed, "复合标签不应按空格拆散");
}

static void testCommonCompoundTagsUseLongestMatches()
{
    IReadOnlyList<string> parsed = AppSettings.ParseTags(
        "electronic liquid drum and bass happy hardcore heavy metal ordinary tag video game music");
    assertSequenceStrings(
        ["electronic", "happy hardcore", "heavy metal", "liquid drum and bass", "ordinary", "tag", "video game music"],
        parsed,
        "常见复合标签应按最长连续词组匹配，普通词不应被误合并");

    IReadOnlyList<string> underscored = AppSettings.ParseTags("future_bass progressive_house");
    assertSequenceStrings(["future bass", "progressive house"], underscored, "下划线复合标签应保持完整");
}

static void testTosuDashboardConfigIsDisabled()
{
    string existing = "DEBUG_LOG=false\r\nOPEN_DASHBOARD_ON_STARTUP=true\r\nSERVER_PORT=24050\r\n";
    string updated = TosuProcessManager.BuildDashboardDisabledConfig(existing);
    if (!updated.Contains("OPEN_DASHBOARD_ON_STARTUP=false", StringComparison.Ordinal) ||
        updated.Contains("OPEN_DASHBOARD_ON_STARTUP=true", StringComparison.Ordinal))
        throw new InvalidOperationException("tosu Dashboard 配置没有被关闭。");

    string added = TosuProcessManager.BuildDashboardDisabledConfig("SERVER_PORT=24050");
    if (!added.Contains("OPEN_DASHBOARD_ON_STARTUP=false", StringComparison.Ordinal))
        throw new InvalidOperationException("缺少配置项时没有补充 Dashboard 开关。");
}

static void testTosuExecutableResolution()
{
    string expected = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "tosu.exe"));
    string? resolved = TosuProcessManager.ResolveExecutablePath(
        string.Empty,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    if (File.Exists(expected) && !string.Equals(expected, resolved, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("没有找到用户目录内的 tosu.exe。");
}

static void testOsuExecutableCandidatesSupportStable()
{
    IReadOnlyList<string> stable = BeatmapDownloadService.GetOsuExecutableCandidates(
        "stable", @"C:\Local", @"C:\Program Files (x86)", @"C:\Program Files");
    assertSequenceStrings(
        [
            @"C:\Local\osu!\osu!.exe",
            @"C:\Program Files (x86)\osu!\osu!.exe",
            @"C:\Program Files\osu!\osu!.exe"
        ],
        stable,
        "stable should be resolved from its standard installation locations");

    IReadOnlyList<string> lazer = BeatmapDownloadService.GetOsuExecutableCandidates(
        "lazer", @"C:\Local", @"C:\Program Files (x86)", @"C:\Program Files");
    assertSequenceStrings(
        [
            @"C:\Local\osulazer\osu!.exe",
            @"C:\Local\Programs\osu!\osu!.exe"
        ],
        lazer,
        "lazer candidate order must remain unchanged");
}

static OsuBeatmapset maniaSet(int setId, int beatmapId, int keys) => new()
{
    Id = setId,
    Artist = "artist",
    Title = $"{keys}K map",
    Creator = "mapper",
    Status = "ranked",
    RankedDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
    Beatmaps =
    [
        new OsuBeatmap
        {
            Id = beatmapId,
            BeatmapsetId = setId,
            Mode = "mania",
            CircleSize = keys,
            DifficultyRating = 3,
            Version = $"{keys}K",
            Bpm = 180,
            TotalLength = 120
        }
    ]
};

static AppSettings recommendationSettings() => new()
{
    Mode = "mania",
    ManiaKeys = 4,
    MinimumYear = 2007,
    MaximumYear = 2030,
    MinimumLengthSeconds = 0,
    MaximumLengthSeconds = 1000,
    ExcludeRecentlyPlayed = false,
    Exploration = 0
};

static PlayerProfile emptyProfile() => new(new OsuUser(), [], [], 1, 10, 180, 120, 0,
    new Dictionary<string, int>(), new HashSet<int>());

static string snapshot(string state, bool paused = false, bool failed = false, string rank = "", long score = 0) => $$"""
{
  "state": { "name": "{{state}}" },
  "game": { "paused": {{paused.ToString().ToLowerInvariant()}} },
  "beatmap": { "id": 1001, "set": 2001 },
  "profile": { "name": "tester" },
  "play": { "failed": {{failed.ToString().ToLowerInvariant()}} },
  "resultsScreen": { "rank": "{{rank}}", "score": {{score}} }
}
""";

static string stringStateSnapshot(string state) => $$"""
{
  "state": "{{state}}",
  "game": { "paused": false },
  "beatmap": { "id": 1001, "set": 2001 },
  "profile": { "name": "multiplayer" },
  "play": { "failed": false },
  "resultsScreen": { "rank": "", "score": 0 }
}
""";

static void testSimpleFiltersOnlyStrictAreAbsolute()
{
    AppSettings settings = new()
    {
        IncludedStatuses = ["ranked"],
        IncludedGenres = ["electronic"],
        IncludedLanguages = ["japanese"]
    };
    settings.NormalizeSimpleFilters();

    OsuBeatmapset matching = filteredSet(1, "ranked", "Electronic", "Japanese");
    OsuBeatmapset wrongStatus = filteredSet(2, "pending", "Electronic", "Japanese");
    OsuBeatmapset wrongGenre = filteredSet(3, "ranked", "Rock", "Japanese");
    OsuBeatmapset wrongLanguage = filteredSet(4, "ranked", "Electronic", "English");

    if (!settings.MatchesSimpleFilters(matching) ||
        !settings.MatchesSimpleFilters(wrongStatus) ||
        !settings.MatchesSimpleFilters(wrongGenre) ||
        !settings.MatchesSimpleFilters(wrongLanguage))
        throw new InvalidOperationException("Included simple filters must not exclude candidates");

    settings.IncludedGenres.Clear();
    settings.IncludedLanguages.Clear();
    settings.StrictStatuses = ["ranked"];
    settings.NormalizeSimpleFilters();
    if (!settings.MatchesSimpleFilters(matching) || settings.MatchesSimpleFilters(wrongStatus) ||
        !settings.MatchesSimpleFilters(wrongGenre) || !settings.MatchesSimpleFilters(wrongLanguage))
        throw new InvalidOperationException("Strict status filter should only keep matching statuses");

    settings.StrictStatuses.Clear();
    if (!settings.MatchesSimpleFilters(wrongGenre) || !settings.MatchesSimpleFilters(wrongLanguage))
        throw new InvalidOperationException("No strict filters should allow all values");
}

static void testSimpleFilterFitIsWeighted()
{
    AppSettings settings = new()
    {
        IncludedStatuses = ["ranked"],
        IncludedGenres = ["electronic"],
        IncludedLanguages = ["japanese"]
    };
    settings.NormalizeSimpleFilters();

    assertNear(1, settings.SimpleFilterFit(filteredSet(1, "ranked", "Electronic", "Japanese")), "All preferred simple filters should match");
    assertNear(2.0 / 3.0, settings.SimpleFilterFit(filteredSet(2, "pending", "Electronic", "Japanese")), "Preferred simple filters should be averaged by category");

    settings.StrictStatuses = ["ranked"];
    settings.NormalizeSimpleFilters();
    assertNear(1, settings.SimpleFilterFit(filteredSet(2, "pending", "Electronic", "Japanese")), "Strict selections must not be scored as preferences");

    settings.IncludedGenres.Clear();
    settings.IncludedLanguages.Clear();
    assertNear(0, settings.SimpleFilterFit(filteredSet(3, "ranked", "Rock", "English")), "Only preferred selections should affect the fit");
}

static void testStrictSimpleFiltersFilterCandidates()
{
    AppSettings settings = recommendationSettings();
    settings.IncludedStatuses = ["ranked", "pending"];
    settings.StrictStatuses = ["ranked"];
    settings.NormalizeSimpleFilters();

    assertSequenceStrings(["ranked"], settings.StrictStatuses, "Strict values must be included values");

    OsuBeatmapset ranked = maniaSet(501, 5011, 4);
    OsuBeatmapset pending = maniaSet(502, 5021, 4);
    pending.Status = "pending";
    IReadOnlyList<RecommendationItem> result = new RecommendationEngine().Rank(
        [pending, ranked], emptyProfile(), settings, 1, 10, new HashSet<int>(), 2);

    assertEqual(1, result.Count, "Strict status filter should exclude non-matching candidates");
    assertEqual(501, result[0].BeatmapsetId, "Strict status filter should keep matching candidates");
    if (settings.MatchesSimpleFilters(pending) || settings.StrictSimpleFilterMatchCount(pending) != 0)
        throw new InvalidOperationException("Strict filters did not limit the candidate range");

    settings.StrictStatuses = ["ranked", "pending"];
    settings.IncludedGenres = ["electronic", "rock"];
    settings.StrictGenres = ["electronic", "rock"];
    settings.IncludedLanguages = ["japanese"];
    settings.StrictLanguages = ["japanese"];
    settings.NormalizeSimpleFilters();
    OsuBeatmapset rankedElectronic = filteredSet(503, "ranked", "Electronic", "Japanese");
    OsuBeatmapset pendingRock = filteredSet(504, "pending", "Rock", "Japanese");
    OsuBeatmapset approvedElectronic = filteredSet(505, "approved", "Electronic", "Japanese");
    OsuBeatmapset rankedMetal = filteredSet(506, "ranked", "Metal", "Japanese");
    OsuBeatmapset rankedEnglish = filteredSet(507, "ranked", "Electronic", "English");
    if (!settings.MatchesSimpleFilters(rankedElectronic) || !settings.MatchesSimpleFilters(pendingRock) ||
        settings.MatchesSimpleFilters(approvedElectronic) || settings.MatchesSimpleFilters(rankedMetal) ||
        settings.MatchesSimpleFilters(rankedEnglish))
        throw new InvalidOperationException("Strict filters should be OR within a group and AND across groups");
}

static void testSimpleFilterSelectionStatesMatchSemantics()
{
    SimpleFilterOption option = new("ranked", "Ranked");
    option.IsChecked = true;
    if (option.Selection != SimpleFilterSelection.Included || option.IsStrict)
        throw new InvalidOperationException("Checked state should represent a preference");

    option.IsChecked = null;
    if (option.Selection != SimpleFilterSelection.Strict || !option.IsStrict)
        throw new InvalidOperationException("Indeterminate state should represent a strict filter");
}
static void testSimpleFilterLabelsFollowLanguage()
{
    try
    {
        Loc.Apply(UiLanguage.Chinese);
        if (new SimpleFilterOption("ranked", "Ranked").DisplayName != "\u4e0a\u67b6\u8c31\u9762" ||
            new SimpleFilterOption("all", "All").DisplayName != "\u5168\u90e8")
            throw new InvalidOperationException("简易筛选的中文显示名不对");

        Loc.Apply(UiLanguage.English);
        if (new SimpleFilterOption("ranked", "Ranked").DisplayName != "Ranked" ||
            new SimpleFilterOption("video game", "Video game").DisplayName != "Video game")
            throw new InvalidOperationException("英文界面下简易筛选应该用 osu! 自己的叫法");
    }
    finally
    {
        Loc.Apply(UiLanguage.System);
    }
}

/// <summary>
/// 双语表必须成对：任何一边漏键都会让那一侧悄悄回退成中文或裸键名；
/// 顺便检查 XAML 里 {DynamicResource L.xxx} 引用的键真的存在，以及 XAML 里不再有硬编码中文。
/// </summary>
static void testLocalizationTablesAreInSync()
{
    List<string> problems = [];

    foreach (string key in LocTable.Chinese.Keys)
        if (!LocTable.English.ContainsKey(key))
            problems.Add($"英文表缺键：{key}");
    foreach (string key in LocTable.English.Keys)
        if (!LocTable.Chinese.ContainsKey(key))
            problems.Add($"中文表缺键：{key}");

    foreach ((string key, string value) in LocTable.Chinese)
    {
        if (string.IsNullOrWhiteSpace(value))
            problems.Add($"中文表空值：{key}");
        if (!LocTable.English.TryGetValue(key, out string? english))
            continue;
        if (string.IsNullOrWhiteSpace(english))
            problems.Add($"英文表空值：{key}");
        if (placeholderIndices(value) != placeholderIndices(english))
            problems.Add($"占位符不一致：{key}");
    }

    string? root = findProjectRoot();
    if (root is not null)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = Path.GetFileName(file);
            string text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(text, @"\{DynamicResource\s+(L\.[A-Za-z0-9_.]+)\}"))
            {
                string key = match.Groups[1].Value["L.".Length..];
                if (!LocTable.Chinese.ContainsKey(key))
                    problems.Add($"{name} 引用了不存在的文案键：{key}");
            }

            int line = 0;
            foreach (string row in text.Split('\n'))
            {
                line++;
                if (!System.Text.RegularExpressions.Regex.IsMatch(row, "(Text|Content|ToolTip|Header|Title)=\"[^\"]*[\\u4e00-\\u9fff]"))
                    continue;
                problems.Add($"{name}:{line} 还有硬编码中文，应该用 {{DynamicResource L.xxx}}");
            }
        }
    }

    if (problems.Count > 0)
        throw new InvalidOperationException("本地化表不一致：\n" + string.Join("\n", problems));
}

/// <summary>切语言必须真的换掉 T() 的结果，并且把 L. 前缀的资源重新挂到 Application 上。</summary>
static void testLocalizationSwitchesAtRuntime()
{
    try
    {
        Loc.Apply(UiLanguage.Chinese);
        if (Loc.IsEnglish || Loc.T("btn.next") != "下一首" || Loc.T("btn.start") != "开始漫游")
            throw new InvalidOperationException("切到中文后文案没变");
        if (Loc.T("mini.nobeatmap") != "尚未导入谱面")
            throw new InvalidOperationException("切到中文后模板键取错");

        Loc.Apply(UiLanguage.English);
        if (!Loc.IsEnglish || Loc.T("btn.next") != "Next" || Loc.T("btn.start") != "Start roaming")
            throw new InvalidOperationException("切到英文后文案没变");
        if (Loc.F("status.built", 5) != "Pool ready — 5 picks")
            throw new InvalidOperationException("带参数的文案格式不对：" + Loc.F("status.built", 5));

        // 漏翻的键必须回退到中文，而不是把键名直接显示出来
        if (Loc.T("no.such.key") != "no.such.key")
            throw new InvalidOperationException("未知键应该原样返回");
    }
    finally
    {
        Loc.Apply(UiLanguage.System);
    }
}

/// <summary>
/// 两个风味只在默认值上分开：中文版走 sayobot 镜像，英文版走国际源。
/// 这条测试保证 AppSettings 的默认值真的接到了 Flavor 上。
/// </summary>
static void testFlavorDownloadDefaults()
{
    AppSettings settings = new();
    if (settings.DownloadUrlTemplate != Flavor.DefaultDownloadTemplate)
        throw new InvalidOperationException("DownloadUrlTemplate 没有接 Flavor 默认值");
    if (AppSettings.DefaultDownloadMirrors != Flavor.DefaultDownloadMirrors)
        throw new InvalidOperationException("DefaultDownloadMirrors 没有接 Flavor 默认值");

    string expected = Flavor.IsEnglish ? "catboy.best" : "sayobot";
    if (!settings.DownloadUrlTemplate.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"当前风味的默认下载源应该包含 {expected}，实际是 {settings.DownloadUrlTemplate}");
    if (!AppSettings.DefaultDownloadMirrors.Contains("{setId}", StringComparison.Ordinal))
        throw new InvalidOperationException("镜像模板必须包含 {setId}");

    string expectedLanguage = Flavor.IsEnglish ? Loc.EnglishTag : Loc.AutoTag;
    if (settings.Language != expectedLanguage)
        throw new InvalidOperationException($"默认语言应为 {expectedLanguage}，实际是 {settings.Language}");
}

/// <summary>本地谱面扫描只依赖 [Metadata] 里的两个 ID，解析错了会把整库都算错。</summary>
static void testInstalledLibraryParsesMetadata()
{
    const string chart = "osu file format v128\n\n[General]\nMode: 3\n\n[Metadata]\nTitle:X\nBeatmapID:1234\nBeatmapSetID:5678\n";
    if (!LocalBeatmapLibrary.tryParseIds(chart, out int beatmapId, out int setId) || beatmapId != 1234 || setId != 5678)
        throw new InvalidOperationException($"应解析出 beatmap=1234 set=5678，实际 {beatmapId}/{setId}");

    // 本地自建谱面没有 BeatmapID，不能算进「已安装」
    if (LocalBeatmapLibrary.tryParseIds("osu file format v128\n[Metadata]\nTitle:Y\n", out _, out _))
        throw new InvalidOperationException("缺少 BeatmapID 的谱面不应计入已安装");

    // 负数 ID 也要丢掉
    if (LocalBeatmapLibrary.tryParseIds("osu file format v128\n[Metadata]\nBeatmapID:-5\nBeatmapSetID:7\n", out _, out _))
        throw new InvalidOperationException("负数 BeatmapID 不应计入已安装");
}

static string placeholderIndices(string value)
{
    List<int> indices = [];
    foreach (System.Text.RegularExpressions.Match match in
             System.Text.RegularExpressions.Regex.Matches(value, @"\{(\d+)(?:[,}])"))
        indices.Add(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    indices.Sort();
    return string.Join(",", indices);
}

static void testSimpleFiltersUseApiGenreAndLanguageIds()
{
    AppSettings settings = new()
    {
        IncludedGenres = ["electronic"],
        IncludedLanguages = ["japanese"],
        StrictGenres = ["electronic"],
        StrictLanguages = ["japanese"]
    };
    settings.NormalizeSimpleFilters();

    OsuBeatmapset apiValues = new() { GenreId = 10, LanguageId = 3 };
    OsuBeatmapset wrongGenre = new() { GenreId = 4, LanguageId = 3 };
    OsuBeatmapset wrongLanguage = new() { GenreId = 10, LanguageId = 2 };
    if (!settings.MatchesSimpleFilters(apiValues) || settings.MatchesSimpleFilters(wrongGenre) ||
        settings.MatchesSimpleFilters(wrongLanguage))
        throw new InvalidOperationException("API genre_id and language_id values must participate in simple filters");
}

static void testTosuStringStateWorksInMultiplayerPayload()
{
    TosuClient client = new();
    int started = 0;
    int succeeded = 0;
    TosuSnapshot? latest = null;
    client.SnapshotReceived += snapshot => latest = snapshot;
    client.PlayStarted += _ => started++;
    client.PlaySucceeded += _ => succeeded++;

    client.ProcessSnapshotJson(snapshot("lobby"));
    client.ProcessSnapshotJson(stringStateSnapshot("play"));
    client.ProcessSnapshotJson(snapshot("resultScreen", rank: "A", score: 987654));

    assertEqual(1, started, "多人房间内的游玩状态应触发开始事件");
    assertEqual(1, succeeded, "多人房间内的结算状态应触发成功事件");
    if (latest is null || latest.StateName != "resultScreen" || latest.BeatmapsetId != 2001)
        throw new InvalidOperationException("多人 tosu payload 未正确读取状态或谱面集");
}

static OsuBeatmapset filteredSet(int id, string status, string genre, string language) => new()
{
    Id = id,
    Status = status,
    Genre = new OsuLookupValue { Name = genre },
    Language = new OsuLookupValue { Name = language }
};

static void assertEqual(int expected, int actual, string message){
    if (expected != actual)
        throw new InvalidOperationException($"{message}：expected={expected}, actual={actual}");
}

static void assertNear(double expected, double actual, string message)
{
    if (Math.Abs(expected - actual) > 0.0001)
        throw new InvalidOperationException($"{message}：expected={expected}, actual={actual}");
}

/// <summary>
/// 「信息栏显示游戏内选中的谱面」全靠 v2 payload 的 beatmap 段。
/// 这段 JSON 的嵌套比别处深（stats.stars.total、time.lastObject），解析错一位整块就是空的。
/// </summary>
static void testTosuSnapshotCarriesBeatmapMetadata()
{
    const string payload = """
    {"state":{"name":"selectPlay"},
     "beatmap":{"id":5397245,"set":2322139,"mode":"mania","artist":"Frums","title":"24eeev0-$",
                "mapper":"Asymetrical","version":"progression",
                "stats":{"stars":{"live":0,"hitWindow":43.5,"total":5.49},"cs":{"original":4,"converted":4}},
                "time":{"live":14982,"firstObject":2380,"lastObject":143811,"mp3Length":146235}},
     "profile":{"name":"tester"},"game":{"paused":false},"play":{"failed":false},
     "resultsScreen":{"rank":"","score":0}}
    """;

    TosuSnapshot? captured = null;
    TosuClient client = new();
    client.SnapshotReceived += value => captured = value;
    client.ProcessSnapshotJson(payload);

    if (captured is null)
        throw new InvalidOperationException("payload 没解析出快照");
    assertText("selectPlay", captured.StateName, "state");
    assertEqual(5397245, captured.BeatmapId, "beatmap id");
    assertEqual(2322139, captured.BeatmapsetId, "beatmapset id");
    assertText("Frums", captured.Artist, "artist");
    assertText("24eeev0-$", captured.Title, "title");
    assertText("progression", captured.DifficultyName, "version");
    assertText("Asymetrical", captured.Mapper, "mapper");
    assertNear(5.49, captured.Stars, "stats.stars.total");
    // 143811-2380 = 141431 ms
    assertNear(141.431, captured.LengthSeconds, "time.lastObject - firstObject");
}

/// <summary>
/// 「什么算在游玩」直接决定窗口什么时候收起。加载阶段是 play + paused（时间没走），
/// 如果按 paused 判断，窗口会在加载时弹回来、开局才收，两边就不同步了。
/// </summary>
static void testPlayVisibilityPredicate()
{
    // 加载中：state=play、paused=true、live=0 → 算游玩中，要收起
    if (!OsuRoam.MainWindow.IsInPlayState(new TosuSnapshot("play", 1, 1, "", true, false, "", 0, LiveTime: 0)))
        throw new InvalidOperationException("加载阶段应视为游玩中（否则窗口会先弹回来）");

    // 正常游玩
    if (!OsuRoam.MainWindow.IsInPlayState(new TosuSnapshot("play", 1, 1, "", false, false, "", 0, LiveTime: 5000)))
        throw new InvalidOperationException("游玩中应视为游玩中");

    // 真暂停（时间已经走过）：不收起，方便看推荐
    if (OsuRoam.MainWindow.IsInPlayState(new TosuSnapshot("play", 1, 1, "", true, false, "", 0, LiveTime: 5000)))
        throw new InvalidOperationException("暂停时应恢复窗口");

    // 结算 / 选歌 / 菜单都不算
    foreach (string state in new[] { "resultScreen", "selectPlay", "menu" })
        if (OsuRoam.MainWindow.IsInPlayState(new TosuSnapshot(state, 1, 1, "", false, false, "", 0, LiveTime: 5000)))
            throw new InvalidOperationException($"{state} 不该收起窗口");
}

static void assertText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{message}：expected=[{expected}], actual=[{actual}]");
}

static void assertSequence(IReadOnlyList<int> expected, IReadOnlyList<int> actual, string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"{message}：expected=[{string.Join(',', expected)}], actual=[{string.Join(',', actual)}]");
}

static void assertSequenceStrings(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string message)
{
    if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException($"{message}：expected=[{string.Join(',', expected)}], actual=[{string.Join(',', actual)}]");
}

static OsuBeatmapset taggedManiaSet(int setId, int beatmapId, string tags) => new()
{
    Id = setId,
    Artist = "artist",
    Title = $"set {setId}",
    Creator = "mapper",
    Status = "ranked",
    Tags = tags,
    RankedDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
    Beatmaps =
    [
        new OsuBeatmap
        {
            Id = beatmapId,
            BeatmapsetId = setId,
            Mode = "mania",
            CircleSize = 4,
            DifficultyRating = 3,
            Version = "4K",
            Bpm = 180,
            TotalLength = 120
        }
    ]
};

static void testBpmDoesNotAffectRanking()
{
    // BPM 已经不参与排序：两张只有 BPM 不同的谱，分数必须完全相同。
    AppSettings settings = recommendationSettings();
    PlayerProfile profile = emptyProfile();

    OsuBeatmapset slow = maniaSet(700, 7001, 4);
    slow.Beatmaps[0].Bpm = 120;
    OsuBeatmapset fast = maniaSet(701, 7002, 4);
    fast.Beatmaps[0].Bpm = 220;

    IReadOnlyList<RecommendationItem> ranked = new RecommendationEngine().RankAvoidingHistory(
        [slow, fast], profile, settings, 1, 10, new HashSet<int>(), [], 2);
    assertEqual(2, ranked.Count, "两张谱都应参与排序");
    assertNear(ranked[0].Score, ranked[1].Score, "只有 BPM 不同不应改变排序分数");
}

static void testPatternSelectionNormalizes()
{
    AppSettings settings = new() { SelectedPatterns = ["stream", " Jacks ", "nonsense", "STREAM"] };
    settings.NormalizePatternSelection();
    assertSequenceStrings(["Stream", "Jacks"], settings.SelectedPatterns, "勾选的键型应收敛成核心型的规范写法");
}

static void testSelectedPatternShapesRanking()
{
    AppSettings settings = recommendationSettings();
    PlayerProfile profile = emptyProfile();
    List<OsuBeatmapset> sets = [maniaSet(800, 8001, 4), maniaSet(801, 8002, 4)];

    ManiaPattern streamChart = new() { LnRatio = 0.0, CoreShares = new() { ["Stream"] = 0.9, ["Chordstream"] = 0.1 } };
    ManiaPattern lnChart = new() { LnRatio = 0.5, CoreShares = new() { ["Coordination"] = 0.6, ["Density"] = 0.4 } };
    Func<int, ManiaPattern?> lookup = id => id == 8001 ? streamChart : id == 8002 ? lnChart : null;

    settings.SelectedPatterns = ["Stream"];
    IReadOnlyList<RecommendationItem> streamFirst = new RecommendationEngine().RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int>(), [], 2, lookup);
    assertEqual(800, streamFirst[0].BeatmapsetId, "勾了 Stream 时流谱应排前面");

    settings.SelectedPatterns = ["Coordination"];
    IReadOnlyList<RecommendationItem> coordFirst = new RecommendationEngine().RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int>(), [], 2, lookup);
    assertEqual(801, coordFirst[0].BeatmapsetId, "勾了 Coordination 时长条谱应排前面");

    settings.SelectedPatterns = [];
    settings.PreferLn = true;
    IReadOnlyList<RecommendationItem> lnOnly = new RecommendationEngine().RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int>(), [], 2, lookup);
    assertEqual(1, lnOnly.Count, "「只要面谱」应滤掉长条占比不足的谱");
    assertEqual(801, lnOnly[0].BeatmapsetId, "剩下的应该是长条谱");

    // 没有键型数据时不硬筛（预取落地前只能这样）
    settings.PreferLn = true;
    IReadOnlyList<RecommendationItem> unknown = new RecommendationEngine().RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int>(), [], 2, _ => null);
    assertEqual(2, unknown.Count, "拿不到键型时不应把候选全滤掉");
}

static void testPatternPreferenceShapesRanking()
{
    AppSettings settings = recommendationSettings();
    PlayerProfile profile = emptyProfile();
    List<OsuBeatmapset> sets = [taggedManiaSet(500, 5001, "ln 长条"), taggedManiaSet(600, 6001, "stream 流")];

    // 600 是流谱；偏好向量只认 Stream
    double[] preference = [1, 0, 0, 0, 0, 0];
    Func<int, ManiaPattern?> lookup = id =>
    {
        OsuBeatmapset? set = sets.FirstOrDefault(candidate => candidate.Beatmaps.Any(beatmap => beatmap.Id == id));
        return set is null ? null : PatternTags.Infer(AppSettings.ParseTags(set.Tags).ToList(), 4);
    };

    IReadOnlyList<RecommendationItem> withoutPattern = new RecommendationEngine().RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int>(), [], 1, lookup, preference, 0);
    assertEqual(500, withoutPattern[0].BeatmapsetId, "权重为 0 时键型不参与排序，保持候选顺序");

    IReadOnlyList<RecommendationItem> withPattern = new RecommendationEngine().RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int>(), [], 1, lookup, preference, 0.3);
    assertEqual(600, withPattern[0].BeatmapsetId, "有键型偏好时命中的流谱应排到前面");
}

static void testCacheCleanerSkipsPoolItems()
{
    string directory = Path.Combine(Path.GetTempPath(), "osuroam-cache-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        for (int setId = 1; setId <= 3; setId++)
        {
            string path = Path.Combine(directory, $"{setId}.osz");
            File.WriteAllBytes(path, new byte[1024 * 1024]);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-setId));
        }

        // 共 3 MB，上限 2 MB；最旧的 3.osz 在推荐池里，必须跳过
        BeatmapCacheCleaner.Result result = BeatmapCacheCleaner.Clean(directory, 2L * 1024 * 1024, new HashSet<int> { 3 });
        assertEqual(1, result.DeletedFiles, "超限时应删除一个最旧的谱面");
        assertTrue(File.Exists(Path.Combine(directory, "3.osz")), "推荐池里的谱面不能被删除");
        assertEqual(2, Directory.GetFiles(directory, "*.osz").Length, "删除后应剩下两个谱面");
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static void assertTrue(bool actual, string message)
{
    if (!actual)
        throw new InvalidOperationException(message);
}

static string osuFile(int keys, IEnumerable<string> hitObjects) =>
    string.Join("\n",
        "osu file format v14",
        "",
        "[General]",
        "Mode: 3",
        "",
        "[Difficulty]",
        $"CircleSize:{keys}",
        "ApproachRate:5",
        "",
        "[TimingPoints]",
        "0,500,4,2,1,60,1,0",
        "",
        "[HitObjects]",
        string.Join("\n", hitObjects));

static string note(int keys, int column, int time, int? endTime = null)
{
    int x = (int)((column + 0.5) * 512 / keys);
    return endTime is int end
        ? $"{x},192,{time},128,0,{end}:0:0:0:0:"
        : $"{x},192,{time},1,0,0:0:0:0:";
}

static void testPatternPrefetchFillsLibrary()
{
    // 预取：把没有真实键型的候选抓一遍、解析、入库，命中够了就停。
    AppSettings settings = recommendationSettings();
    settings.PoolSize = 1;
    settings.PreferLn = true;
    List<OsuBeatmapset> sets = [maniaSet(900, 9001, 4), maniaSet(901, 9002, 4), maniaSet(902, 9003, 4)];
    PatternLibrary library = new();

    List<string> riceNotes = [];
    for (int index = 0; index < 20; index++)
        riceNotes.Add(note(4, index % 4, 1000 + index * 100));

    List<string> lnNotes = [];
    for (int index = 0; index < 12; index++)
        lnNotes.Add(note(4, index % 4, 1000 + index * 180, 1000 + index * 180 + 140));

    int fetched = 0;
    Task<string?> fetch(int beatmapId, CancellationToken _)
    {
        fetched += 1;
        return Task.FromResult<string?>(beatmapId == 9001 ? osuFile(4, riceNotes) : osuFile(4, lnNotes));
    }

    PrefetchResult result = PatternPrefetcher.RunAsync(
        sets, settings, library, fetch, PatternPrefetcher.DefaultBudget, null, CancellationToken.None)
        .GetAwaiter().GetResult();

    assertEqual(3, result.Attempted, "三张谱都该被抓一次");
    assertEqual(3, result.Parsed, "三张谱都该解析成功");
    assertEqual(2, result.Hits, "两张长条谱应算命中（只要面谱）");
    assertTrue(library.TryGet(9001) is { IsInferred: false }, "预取结果应以真实键型入库");
    assertEqual(3, fetched, "不该重复抓同一张谱");

    // 预算会限制单轮抓取量
    PatternLibrary second = new();
    PrefetchResult limited = PatternPrefetcher.RunAsync(
        sets, settings, second, fetch, 1, null, CancellationToken.None).GetAwaiter().GetResult();
    assertEqual(1, limited.Attempted, "预算为 1 时只抓一张");

    // tag 里点名了所选键型的候选要优先抓，哪怕它离目标星级更远
    AppSettings hinted = recommendationSettings();
    hinted.PoolSize = 1;
    hinted.SelectedPatterns = ["Jacks"];
    hinted.MinimumStars = 4;
    hinted.MaximumStars = 5;
    OsuBeatmapset tagged = maniaSet(910, 9101, 4);
    tagged.Tags = "jack chordjack";
    tagged.Beatmaps[0].DifficultyRating = 9;
    OsuBeatmapset plain = maniaSet(911, 9102, 4);
    plain.Beatmaps[0].DifficultyRating = 4.5;
    List<int> order = [];
    PatternPrefetcher.RunAsync([plain, tagged], hinted, new PatternLibrary(), (beatmapId, _) =>
    {
        order.Add(beatmapId);
        return Task.FromResult<string?>(osuFile(4, lnNotes));
    }, 1, null, CancellationToken.None).GetAwaiter().GetResult();
    assertEqual(9101, order[0], "tag 命中所选键型的候选应优先预取");
}

static void testPatternSearchHints()
{
    AppSettings settings = recommendationSettings();
    assertEqual(0, PatternSearchHints.For(settings).Count, "没勾键型时不该带关键字");

    settings.SelectedPatterns = ["Jacks"];
    assertSequenceStrings(["jack"], PatternSearchHints.For(settings), "Jacks 应翻译成 jack");

    settings.SelectedPatterns = ["Coordination", "Density"];
    assertSequenceStrings(["ln"], PatternSearchHints.For(settings), "协调/密度都是 ln，应去重成一个");

    settings.SelectedPatterns = ["Stream", "Jacks"];
    assertSequenceStrings(["stream", "jack"], PatternSearchHints.For(settings), "两个关键字都保留");

    settings.SelectedPatterns = [];
    settings.PreferLn = true;
    assertSequenceStrings(["ln"], PatternSearchHints.For(settings), "「只要面谱」应带 ln");

    settings.Mode = "osu";
    settings.SelectedPatterns = ["Jacks"];
    assertEqual(0, PatternSearchHints.For(settings).Count, "非 mania 不带键型关键字");
}

static void testPatternAnalyzerDetectsStream()
{
    // 20 个 100ms 间隔的单键，来回换列 → 纯流
    int[] pattern = [0, 1, 2, 3, 2, 1, 0, 1];
    List<string> notes = [];
    for (int index = 0; index < 20; index++)
        notes.Add(note(4, pattern[index % pattern.Length], 1000 + index * 100));

    ManiaPattern? result = ManiaPatternAnalyzer.Analyze(osuFile(4, notes), 1001, 100);
    assertTrue(result is not null, "4K 谱面应能解析出键型");
    assertEqual(4, result!.KeyCount, "键数应来自 CircleSize");
    assertEqual(20, result.TotalNotes, "音符总数应等于 hit object 数量");
    assertTrue(result.CoreShares.TryGetValue("Stream", out double stream) && stream > 0.5,
        $"纯流谱面应主导 Stream，实际={string.Join('|', result.CoreShares.Select(pair => $"{pair.Key}:{pair.Value}"))}");
    assertTrue(result.ModeTag == "RC", $"没有长条时应标为 RC，实际={result.ModeTag}");
}

static void testPatternAnalyzerDetectsJacks()
{
    // 8 行双键和弦，同两列反复 → 叠键/叠和弦
    List<string> notes = [];
    for (int index = 0; index < 8; index++)
    {
        notes.Add(note(4, 0, 1000 + index * 100));
        notes.Add(note(4, 1, 1000 + index * 100));
    }

    ManiaPattern? result = ManiaPatternAnalyzer.Analyze(osuFile(4, notes), 1002, 200);
    assertTrue(result is not null, "叠键谱面应能解析");
    assertTrue(result!.CoreShares.ContainsKey("Jacks"), "叠键谱面必须产出 Jacks 核心型");
    assertTrue(result.CoreShares.TryGetValue("Jacks", out double jackShare) && jackShare > 0.2,
        $"叠键谱面 Jacks 占比应明显，实际={jackShare}");
}

static void testPatternAnalyzerDetectsLnChart()
{
    // 半程长条 + 半程普通键 → LN 占比 0.5，模式标签应为 Mix 而非 RC
    List<string> notes = [];
    for (int index = 0; index < 8; index++)
        notes.Add(note(4, index % 4, 1000 + index * 200, 1000 + index * 200 + 150));
    for (int index = 0; index < 8; index++)
        notes.Add(note(4, index % 4, 3000 + index * 200));

    ManiaPattern? result = ManiaPatternAnalyzer.Analyze(osuFile(4, notes), 1003, 300);
    assertTrue(result is not null, "长条谱面应能解析");
    assertTrue(result!.LnRatio > 0.4, $"LN 占比应接近 0.5，实际={result.LnRatio}");
    assertTrue(result.ModeTag != "RC", $"有大量长条时不应标为 RC，实际={result.ModeTag}");
    assertEqual(8, result.TotalHolds, "长条数量应与 hold 头一致");
}

static void testRecycledCandidatesAreFlagged()
{
    AppSettings settings = recommendationSettings();
    PlayerProfile profile = emptyProfile();
    List<OsuBeatmapset> sets =
    [
        maniaSet(100, 1001, 4),
        maniaSet(200, 2001, 4),
        maniaSet(300, 3001, 4)
    ];

    IReadOnlyList<RecommendationItem> result = new RecommendationEngine().RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int>(), [100, 200], 2);

    assertEqual(2, result.Count, "推荐池应由新谱和回收谱补满");
    assertTrue(!result[0].IsRecycled, "新抓到的候选不应标记为回收");
    assertTrue(result[1].IsRecycled, "历史补位条目应标记为回收");
}

static void testSearchSortPlanRotates()
{
    string[] plan0 = OsuApiClient.BuildSortPlan(0, 8);
    assertEqual(8, plan0.Length, "排序计划应覆盖整个页数预算");
    assertTrue(plan0.Take(4).All(sort => sort == OsuApiClient.CandidateSorts[0]), "前四页应沿用同一种排序");
    assertTrue(plan0.Skip(4).All(sort => sort == OsuApiClient.CandidateSorts[1]), "后四页应切换到下一种排序");

    string[] plan1 = OsuApiClient.BuildSortPlan(1, 8);
    assertTrue(plan1[0] == OsuApiClient.CandidateSorts[1], "轮换一次后起始排序应后移一位");
    assertTrue(!plan0.SequenceEqual(plan1), "不同轮次必须产出不同排序计划");

    foreach (int rotation in new[] { 0, 1, 2, 3, 5, 6, 12, 99 })
    {
        assertTrue(
            OsuApiClient.BuildSortPlan(rotation, 8).All(sort => OsuApiClient.CandidateSorts.Contains(sort)),
            "排序计划只能使用受支持的排序值");
    }

    assertEqual(0, OsuApiClient.BuildSortPlan(0, 0).Length, "页数为 0 时应得到空计划");
}
