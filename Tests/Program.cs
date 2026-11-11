using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OsuSv;
using OsuRoam.Services;
using OsuRoam.Models;

// 探针模式：arg0 == "--probe" 时不跑测试，而是用本机 settings.json 的凭据拉一批真实候选，
// 抓 .osu 算键型，落一份 CSV 快照。用来离线评估「勾了 Jacks 到底排出来什么」。
if (args.Length > 0 && args[0] == "--probe")
    return await probeAsync(args);
if (args.Length > 0 && args[0] == "--diag")
    return await diagAsync(args);
if (args.Length > 0 && args[0] == "--ab")
    return await abTestAsync(args);
if (args.Length > 1 && args[0] == "--sets")
    return await setsAsync(args);
if (args.Length > 2 && args[0] == "--hint")
    return await hintAsync(args);
if (args.Length > 0 && args[0] == "--verify")
    return await verifyAsync(args);
if (args.Length > 0 && args[0] == "--preview-smoke")
    return PreviewSmoke.Run(args.Length > 1 ? args[1] : null);
if (args.Length > 0 && args[0] == "--preview-notes")
    return PreviewSmoke.Run(args.Length > 1 ? args[1] : null, steady: true, noteVisual: true);
if (args.Length > 0 && args[0] == "--preview-steady")
    return PreviewSmoke.Run(args.Length > 1 ? args[1] : null, steady: true);
if (args.Length > 0 && args[0] == "--preview-opaque")
    return PreviewSmoke.Run(args.Length > 1 ? args[1] : null, steady: true, opaque: true);
if (args.Length > 0 && args[0] == "--preview-hwnd-alpha")
    return PreviewSmoke.Run(args.Length > 1 ? args[1] : null, steady: true, opaque: true, hwndAlpha: true);
if (args.Length > 0 && args[0] == "--preview-heartbeat")
    return PreviewSmoke.Run(args.Length > 1 ? args[1] : null, steady: true, heartbeat: true);
if (args.Length > 0 && args[0] == "--preview-unpaced")
    return PreviewSmoke.Run(args.Length > 1 ? args[1] : null, steady: true, unpaced: true);

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
testPatternLibraryRejectsStaleCache();
testPatternLibraryVerifiedOnlyIgnoresTags();
testPoolReconcilerStreamsReplacements();
testStreamingRerankIsReproducible();
testStreamingVerificationReordersPool();
testCandidateDiagnosisExplainsEmptyPool();
testFallbackFillsWhenTagFilterTooNarrow();
testFallbackAllowsPlayedButKeepsExcludedTags();
testFallbackCardsGetReplacedByStrictOnes();
testKeywordCandidatesAreJudgedByVerifiedPattern();
testKeywordCandidatesEnterVerificationQueue();
testPoolDownloadOrderingPutsQualifyingFirst();
testDownloadCancelRuleUsesMembershipNotQueue();
testCandidateAdmissionJudgesByVerifiedPattern();
testLnThresholdCountsAsFaceChart();
testLnTriangleFillsCircleWhenBalancedAtFullLn();
testLnTriangleScalesWithLnRatioAndShares();
testManiaStarRatingHandlesHolds();
testManiaStarRatingScalesWithDensity();
testUpdateVersionOrdering();
testUpdateReleaseSelection();
testUpdatePrepareStagesPackage();
testSvEvaluatorOnSteadyChartIsClean();
testSvEvaluatorPunishesBaselineJumps();
testSvEvaluatorExaggerationNeedsMidFlightChange();
testSearchStatusFollowsWhitelist();
testReconcileRefreshesFallbackFlagOnReusedCard();
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
testOsuClientInference();
testOsuCommandLineParsing();
testOsuStorageIniParsing();
testOsuBeatmapRootsFollowInstallLocation();
testTosuMemoryFailureDetection();
testPreviewChartParsesColumnsAndHolds();
testPreviewChartRejectsOtherModes();
testPreviewChartFindsVisibleRange();
testPreviewClockFollowsSource();
testPreviewClockFreezesWhenSourceStops();
testPreviewClockSnapsOnJump();
testPreviewClockNeverGoesBackwards();
testPreviewClockCadenceIndependent();
testPreviewClockRepeatedSamplesStop();
testPreviewClockDelayedWakeStopsAtDeadline();
testPreviewMotionCommitContinuity();
testTosuSnapshotCarriesPreviewTime();
Console.WriteLine("All state transition, recommendation, mode profile, and tosu integration tests passed.");
return 0;

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

static void testTosuMemoryFailureDetection()
{
    assertTrue(TosuProcessManager.IsMemoryReadFailure("lazer 48796 global updatePreciseState Couldn't read long at 418"),
        "tosu 读内存失败的那行日志要认得出来");
    assertTrue(TosuProcessManager.IsMemoryReadFailure("couldn't READ long at 0"), "大小写不敏感");
    assertTrue(!TosuProcessManager.IsMemoryReadFailure("lazer 48796 Searching for osu! process..."),
        "正常日志不能误判");
    assertTrue(!TosuProcessManager.IsMemoryReadFailure(null), "null 不算");
    assertTrue(!TosuProcessManager.IsMemoryReadFailure("   "), "空白不算");
}

static void testOsuExecutableCandidatesSupportStable()
{
    IReadOnlyList<string> stable = OsuLocator.CommonExecutables(
        OsuClientKind.Stable, @"C:\Local", @"C:\Program Files", @"C:\Program Files (x86)");
    assertSequenceStrings(
        [
            @"C:\Local\osu!\osu!.exe",
            @"C:\Program Files (x86)\osu!\osu!.exe",
            @"C:\Program Files\osu!\osu!.exe"
        ],
        stable,
        "stable should be resolved from its standard installation locations");

    IReadOnlyList<string> lazer = OsuLocator.CommonExecutables(
        OsuClientKind.Lazer, @"C:\Local", @"C:\Program Files", @"C:\Program Files (x86)");
    assertSequenceStrings(
        [
            @"C:\Local\osulazer\osu!.exe",
            @"C:\Local\osulazer\current\osu!.exe",
            @"C:\Local\Programs\osu!\osu!.exe",
            @"C:\Local\osu\osu!.exe"
        ],
        lazer,
        "lazer candidates must include the Velopack 'current' folder and keep the order stable");
}

static void testOsuClientInference()
{
    assertTrue(OsuLocator.InferClient(@"C:\Users\me\AppData\Local\osulazer\osu!.exe") == OsuClientKind.Lazer,
        "osulazer 目录应判为 lazer");
    assertTrue(OsuLocator.InferClient(@"C:\Users\me\AppData\Local\osulazer\current\osu!.exe") == OsuClientKind.Lazer,
        "Velopack 的 current 目录应判为 lazer");
    assertTrue(OsuLocator.InferClient(@"D:\Games\osu!\osu!.exe") == OsuClientKind.Stable,
        "装在别的盘的 stable 应判为 stable");
    assertTrue(OsuLocator.InferClient(@"C:\Program Files (x86)\osu!\osu!.exe") == OsuClientKind.Stable,
        "Program Files 下的 stable 应判为 stable");

    assertText("auto", OsuLocator.NormalizeClient(null), "空设置必须归一成自动 —— 旧配置就是这么坏的");
    assertText("auto", OsuLocator.NormalizeClient("  "), "空白归一成自动");
    assertText("lazer", OsuLocator.NormalizeClient("osu!lazer"), "别名识别");
    assertText("stable", OsuLocator.NormalizeClient("STABLE"), "大小写不敏感");
}

static void testOsuCommandLineParsing()
{
    assertText(@"C:\Program Files\osu!\osu!.exe",
        OsuLocator.ParseCommandLinePath("\"C:\\Program Files\\osu!\\osu!.exe\" \"%1\"") ?? "<null>",
        "带引号的注册表命令行要能拆出 exe");
    assertText(@"C:\osu!\osu!.exe",
        OsuLocator.ParseCommandLinePath(@"C:\osu!\osu!.exe %1") ?? "<null>",
        "不带引号时按 .exe 截断，不能被路径里的空格骗到");
    assertTrue(OsuLocator.ParseCommandLinePath(null) is null, "空命令行返回 null");
    assertTrue(OsuLocator.ParseCommandLinePath("  ") is null, "空白命令行返回 null");
}

static void testOsuStorageIniParsing()
{
    assertText(@"D:\osu-data",
        OsuLocator.ParseStorageIniPath("[Storage]\nFullPath=D:\\osu-data\n") ?? "<null>",
        "lazer 便携版的 storage.ini 要能读出来");
    assertTrue(OsuLocator.ParseStorageIniPath("[Other]\nFullPath=D:\\nope\n") is null,
        "别的段里的 FullPath 不算");
    assertTrue(OsuLocator.ParseStorageIniPath("[Storage]\nFullPath=\n") is null,
        "空值当作没配");
    assertTrue(OsuLocator.ParseStorageIniPath("") is null, "空文件返回 null");
}

static void testOsuBeatmapRootsFollowInstallLocation()
{
    string sandbox = Path.Combine(Path.GetTempPath(), "osuroam-roots-" + Guid.NewGuid().ToString("N"));
    try
    {
        string localAppData = Path.Combine(sandbox, "Local");
        string roaming = Path.Combine(sandbox, "Roaming");
        string stableDirectory = Path.Combine(sandbox, "Games", "osu!");
        string lazerDirectory = Path.Combine(localAppData, "osulazer");
        string portableStorage = Path.Combine(sandbox, "portable-data");

        Directory.CreateDirectory(Path.Combine(stableDirectory, "Songs"));
        Directory.CreateDirectory(Path.Combine(roaming, "osu", "files"));
        Directory.CreateDirectory(Path.Combine(portableStorage, "files"));
        Directory.CreateDirectory(lazerDirectory);
        File.WriteAllText(Path.Combine(lazerDirectory, "storage.ini"), $"[Storage]\nFullPath={portableStorage}\n");

        IReadOnlyList<string> roots = OsuLocator.BeatmapRoots(localAppData, roaming, stableDirectory, lazerDirectory);

        assertTrue(roots.Any(root => root.EndsWith(Path.Combine("osu!", "Songs"), StringComparison.OrdinalIgnoreCase)),
            $"stable 装在别的盘时也要算上 exe 同级的 Songs：{string.Join(" | ", roots)}");
        assertTrue(roots.Any(root => root.EndsWith(Path.Combine("osu", "files"), StringComparison.OrdinalIgnoreCase)),
            "lazer 默认的 roaming\\osu\\files 要保留");
        assertTrue(roots.Any(root => root.EndsWith(Path.Combine("portable-data", "files"), StringComparison.OrdinalIgnoreCase)),
            $"便携 lazer 的 storage.ini 指向的仓库也要扫：{string.Join(" | ", roots)}");
        assertTrue(roots.Count == roots.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "谱面目录不能重复");
    }
    finally
    {
        try { Directory.Delete(sandbox, true); } catch { }
    }
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
    // 预取：把没有真实键型的候选抓一遍、解析、入库。
    AppSettings settings = recommendationSettings();
    settings.PoolSize = 10; // 目标数高于候选数，避免提前停导致次数不稳定
    settings.PreferLn = true;
    settings.MinimumStars = 1;
    settings.MaximumStars = 10;
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
        sets, settings, library, fetch, PatternPrefetcher.DefaultBudget, PatternPrefetcher.DefaultDeadline,
        null, CancellationToken.None).GetAwaiter().GetResult();

    assertEqual(3, result.Attempted, "三张谱都该被尝试一次");
    assertEqual(3, result.Parsed, "三张谱都该解析成功");
    assertEqual(2, result.Hits, "两张长条谱应算命中（只要面谱）");
    assertTrue(library.TryGetVerified(9001) is { IsInferred: false } verified &&
               verified.AnalyzerVersion == ManiaPatternAnalyzer.Version, "预取结果应是「当前分析器版本」的可信键型");
    assertEqual(3, fetched, "不该重复抓同一张谱");

    // 预算会限制单轮抓取量
    PatternLibrary second = new();
    PrefetchResult limited = PatternPrefetcher.RunAsync(
        sets, settings, second, fetch, 1, PatternPrefetcher.DefaultDeadline, null, CancellationToken.None)
        .GetAwaiter().GetResult();
    assertEqual(1, limited.Attempted, "预算为 1 时只抓一张");

    // 排序按「乐观分」：星级中心的谱优先于 tag 命中但星级偏远的谱
    AppSettings hinted = recommendationSettings();
    hinted.PoolSize = 10;
    hinted.SelectedPatterns = ["Jacks"];
    hinted.MinimumStars = 4;
    hinted.MaximumStars = 5;
    OsuBeatmapset farTagged = maniaSet(910, 9101, 4);
    farTagged.Tags = "jack chordjack";
    farTagged.Beatmaps[0].DifficultyRating = 4.0;
    OsuBeatmapset centerPlain = maniaSet(911, 9102, 4);
    centerPlain.Beatmaps[0].DifficultyRating = 4.5;
    List<int> order = [];
    PatternPrefetcher.RunAsync([farTagged, centerPlain], hinted, new PatternLibrary(), (beatmapId, _) =>
    {
        order.Add(beatmapId);
        return Task.FromResult<string?>(osuFile(4, lnNotes));
    }, 1, PatternPrefetcher.DefaultDeadline, null, CancellationToken.None).GetAwaiter().GetResult();
    assertEqual(9102, order[0], "星级中心的谱应优先验证，tag 命中不能越级抢占预算");

    // 星级相同时，tag 命中作为次级排序胜出
    OsuBeatmapset sameStarTagged = maniaSet(920, 9201, 4);
    sameStarTagged.Tags = "jack";
    sameStarTagged.Beatmaps[0].DifficultyRating = 4.5;
    OsuBeatmapset sameStarPlain = maniaSet(921, 9202, 4);
    sameStarPlain.Beatmaps[0].DifficultyRating = 4.5;
    order.Clear();
    PatternPrefetcher.RunAsync([sameStarPlain, sameStarTagged], hinted, new PatternLibrary(), (beatmapId, _) =>
    {
        order.Add(beatmapId);
        return Task.FromResult<string?>(osuFile(4, lnNotes));
    }, 1, PatternPrefetcher.DefaultDeadline, null, CancellationToken.None).GetAwaiter().GetResult();
    assertEqual(9201, order[0], "星级相同时 tag 命中应排在前面");
}

static void testPatternSearchHints()
{
    AppSettings settings = recommendationSettings();
    assertEqual(0, PatternSearchHints.For(settings).Count, "没勾键型时不该带关键字");

    settings.SelectedPatterns = ["Jacks"];
    assertSequenceStrings(["jack", "chordjack", "minijack"], PatternSearchHints.For(settings), "Jacks 应展开到同义词，按优先级排序");

    settings.SelectedPatterns = ["Coordination", "Density"];
    assertSequenceStrings(["ln", "handstream"], PatternSearchHints.For(settings), "协调/密度都归到 ln，再补 handstream");

    settings.SelectedPatterns = ["Stream", "Jacks"];
    assertSequenceStrings(["stream", "jack", "chordjack"], PatternSearchHints.For(settings), "多个键型共享最多 3 个关键字");

    settings.SelectedPatterns = ["Stream", "Jacks", "Chordstream", "Wildcard"];
    assertTrue(PatternSearchHints.For(settings).Count <= PatternSearchHints.MaximumHints, "关键字数量要有上限，别把请求数堆爆");

    settings.SelectedPatterns = [];
    settings.PreferLn = true;
    assertSequenceStrings(["ln"], PatternSearchHints.For(settings), "「只要面谱」应带 ln");

    settings.Mode = "osu";
    settings.SelectedPatterns = ["Jacks"];
    assertEqual(0, PatternSearchHints.For(settings).Count, "非 mania 不带键型关键字");
}

static void testPatternLibraryRejectsStaleCache()
{
    PatternLibrary library = new();
    library.Put(new ManiaPattern
    {
        BeatmapId = 1, Source = PatternSource.Osu, AnalyzerVersion = ManiaPatternAnalyzer.Version
    });
    assertTrue(library.TryGetVerified(1) is not null, "当前分析器版本的真实解析应可信");

    library.Put(new ManiaPattern
    {
        BeatmapId = 2, Source = PatternSource.Osu, AnalyzerVersion = ManiaPatternAnalyzer.Version - 1
    });
    assertTrue(library.TryGetVerified(2) is null, "旧分析器版本的缓存不可信，应重新解析");

    library.Put(new ManiaPattern
    {
        BeatmapId = 3, Source = PatternSource.Tags, AnalyzerVersion = ManiaPatternAnalyzer.Version
    });
    assertTrue(library.TryGetVerified(3) is null, "tag 推断不可信，只能当调度线索");
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

static void testPoolReconcilerStreamsReplacements()
{
    RecommendationItem first = poolCard(1);
    RecommendationItem downloading = poolCard(2);
    RecommendationItem stale = poolCard(3);
    RecommendationItem fresh = poolCard(4);

    // 池子里是 [1,2,3]；验证后新排序是 [4,1]，2 正在下载不能被换掉。
    List<RecommendationItem> next = PoolReconciler.Build(
        new[] { first, downloading, stale }, new[] { fresh, first }, new HashSet<int> { 2 }, 3);
    assertTrue(next.Select(item => item.BeatmapsetId).SequenceEqual(new[] { 2, 4, 1 }), "锁定项在最前，其余按新排序补齐");
    assertTrue(ReferenceEquals(next[0], downloading), "锁定项应复用原卡片实例");
    assertTrue(ReferenceEquals(next[2], first), "同 id 卡片应复用原实例，避免重排时封面闪烁");

    // 顺序没变就不该动 UI 集合。
    List<RecommendationItem> same = PoolReconciler.Build(
        next, new[] { downloading, fresh, first }, new HashSet<int>(), 3);
    assertTrue(PoolReconciler.SameSequence(next, same), "同一排序应判定为无变化");

    // 超过池子上限要截断。
    assertEqual(2, PoolReconciler.Build([], new[] { first, downloading, stale }, new HashSet<int>(), 2).Count, "池子上限应生效");
}

static void testPatternLibraryVerifiedOnlyIgnoresTags()
{
    PatternLibrary library = new();
    IReadOnlyList<string> tags = ["stream"];

    // 默认允许 tag 推断（刚装上、还没下载过谱面时也要有信号）。
    assertTrue(library.Resolve(11, 110, tags, 4) is { Source: PatternSource.Tags }, "默认设置下 tag 推断仍然可用");
    assertTrue(library.Resolve(11, 110, tags, 4, verifiedOnly: true) is null, "勾选键型时 tag 推断不能当结论");

    library.Put(new ManiaPattern
    {
        BeatmapId = 12, Source = PatternSource.Osu, AnalyzerVersion = ManiaPatternAnalyzer.Version,
        LnRatio = 0.4, CoreShares = new() { ["Coordination"] = 0.7 }
    });
    assertTrue(library.Resolve(12, 120, tags, 4, verifiedOnly: true) is { LnRatio: 0.4 }, "真实解析结果应通过验证门槛");

    library.Put(new ManiaPattern
    {
        BeatmapId = 13, Source = PatternSource.Osu, AnalyzerVersion = ManiaPatternAnalyzer.Version - 1
    });
    assertTrue(library.Resolve(13, 130, tags, 4, verifiedOnly: true) is null, "旧分析器版本的缓存要重抓");
}

static void testStreamingRerankIsReproducible()
{
    // 流式发布用同一个种子反复重排：同一批候选必须给出同一个顺序，否则卡片会一直洗牌。
    AppSettings settings = recommendationSettings();
    settings.Exploration = 0.45;
    PlayerProfile profile = emptyProfile();
    List<OsuBeatmapset> sets = [.. Enumerable.Range(0, 12).Select(index => maniaSet(900 + index, 9001 + index, 4))];

    List<int> ids = [.. sets.Select(set => set.Id)];

    List<int> rank(int seed) => [..
        new RecommendationEngine()
            .RankAvoidingHistory(sets, profile, settings, 1, 10, new HashSet<int>(), [], 5, null, null, 0, null, new Random(seed))
            .Select(item => item.BeatmapsetId)];

    List<int> first = rank(20261104);
    assertEqual(5, first.Count, "应排出一个完整的池子");
    assertTrue(first.SequenceEqual(rank(20261104)), "同一种子重排必须可复现");
    assertTrue(!first.SequenceEqual(rank(1)), "换种子后探索顺序应该不同");
    assertTrue(first.All(ids.Contains), "排序结果只能来自候选集合");
}

static RecommendationItem poolCard(int beatmapsetId) => new()
{
    BeatmapsetId = beatmapsetId,
    BeatmapId = beatmapsetId * 10,
    Artist = "artist",
    Title = $"set-{beatmapsetId}",
    DifficultyName = "4K",
    Mapper = "mapper",
    Stars = 3,
    Bpm = 180,
    LengthSeconds = 120,
    RankedYear = 2024,
    Score = 1
};

static void testStreamingVerificationReordersPool()
{
    // 流式发布的完整路径：先按「还没验证」排一个临时池，后台验证落地后再重排。
    AppSettings settings = recommendationSettings();
    settings.SelectedPatterns = ["Stream"];
    PlayerProfile profile = emptyProfile();
    List<OsuBeatmapset> sets = [maniaSet(710, 7101, 4), maniaSet(711, 7111, 4), maniaSet(712, 7121, 4)];

    PatternLibrary library = new();
    // 勾了键型时只看真实解析结果 —— 这正是 MainWindow.refreshPatternCache 的做法。
    Func<int, ManiaPattern?> lookup = id => library.Resolve(id, id / 10, [], 4, verifiedOnly: true);

    List<int> rank() => [..
        new RecommendationEngine()
            .RankAvoidingHistory(sets, profile, settings, 1, 10, new HashSet<int>(), [], 3,
                lookup, null, 0, null, new Random(7))
            .Select(item => item.BeatmapsetId)];

    List<int> provisional = rank();
    assertEqual(3, provisional.Count, "没有键型数据时也要先放出临时池，不能等验证");
    assertEqual(712, provisional[^1], "验证前只看基础分，同分时按候选顺序");

    // 后台验证到 7121 是纯流谱。
    int[] alternation = [0, 1, 2, 3, 2, 1, 0, 1];
    List<string> notes = [];
    for (int index = 0; index < 20; index += 1)
        notes.Add(note(4, alternation[index % alternation.Length], 1000 + index * 100));
    assertTrue(library.Ingest(osuFile(4, notes), 7121, 712), "流谱应能解析入库");

    List<int> verified = rank();
    assertEqual(712, verified[0], "验证出流谱后应重排到最前");
    assertEqual(3, verified.Count, "重排不应丢掉还没验证的候选");

    // 池子按新排序对齐：712 从最后挪到最前，其他卡片不重下封面。
    RecommendationItem a = poolCard(710);
    RecommendationItem b = poolCard(711);
    RecommendationItem c = poolCard(712);
    List<RecommendationItem> next = PoolReconciler.Build(
        new[] { a, b, c }, [c, a, b], new HashSet<int>(), 3);
    assertTrue(next.Select(item => item.BeatmapsetId).SequenceEqual(verified), "池子应对齐到验证后的排序");
    assertTrue(ReferenceEquals(next[0], c), "挪位置的卡片应复用原实例");
}

/// <summary>
/// 离线评估探针：用本机 settings.json 里的 OAuth 凭据按真实设置拉候选，
/// 抓 .osu 解析键型，输出一份 CSV 快照 + 关键指标。
///
/// 用法：<c>dotnet run --project Tests -- --probe Jacks 4.1 4.9 D:\AI\scratch\snap 40</c>
/// </summary>
static async Task<int> probeAsync(string[] args)
{
    string label = args.Length > 1 ? args[1] : "Jacks";
    double minStars = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 4.0;
    double maxStars = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 5.0;
    string outDir = args.Length > 4 ? args[4] : ".";
    int maxFetch = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 40;

    SettingsStore store = new();
    AppSettings settings = await store.LoadAsync();
    Console.WriteLine($"设置：{store.SettingsPathForDisplay}");
    if (!string.Equals(settings.Mode, "mania", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"当前模式是 {settings.Mode}，探针只测 mania");
        return 2;
    }

    string secret = SecretProtector.Unprotect(settings.ProtectedClientSecret);
    if (string.IsNullOrEmpty(settings.ClientId) || secret.Length == 0)
    {
        Console.WriteLine("拿不到 OAuth 凭据，退出");
        return 2;
    }

    Console.WriteLine($"  用户={settings.Username} 键数={settings.ManiaKeys} 探索={settings.Exploration} 星级={minStars}–{maxStars}");
    OsuApiClient api = new();
    api.Configure(settings.ClientId, secret);
    using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(20));
    PlayerProfile profile = await api.BuildProfileAsync(settings.Username, settings.Mode, settings.ManiaKeys, cancellation.Token);
    Console.WriteLine($"档案：成绩样本 {profile.DifficultySampleCount} 张，偏好时长 {profile.PreferredLength:0}s");

    // wide = 把 tag / 状态 / 类型过滤全放开，只看搜索与排序本身；
    // keep = 完全用本机配置（tag 白名单、严格状态、排除已玩…），复现真实推荐。
    string filterMode = args.Length > 6 ? args[6] : "wide";
    settings.SelectedPatterns = [label];
    settings.PreferLn = false;
    settings.MinimumStars = minStars;
    settings.MaximumStars = maxStars;
    if (filterMode == "wide")
    {
        settings.IncludedTags = [];
        settings.ExcludedTags = [];
        settings.MinimumYear = 2007;
        settings.MaximumYear = 2030;
        settings.MinimumLengthSeconds = 0;
        settings.MaximumLengthSeconds = 3600;
        settings.ExcludeRecentlyPlayed = false;
        settings.ExcludeInstalledBeatmaps = false;
        settings.IncludedStatuses = [.. AppSettings.StatusFilterValues];
        settings.StrictStatuses = [];
        settings.IncludedGenres = [.. AppSettings.GenreFilterValues];
        settings.StrictGenres = [];
        settings.IncludedLanguages = [.. AppSettings.LanguageFilterValues];
        settings.StrictLanguages = [];
        settings.RecommendationHistory = [];
    }
    Console.WriteLine($"过滤：{filterMode}（tag {settings.IncludedTags.Count} 条，严格状态 {settings.StrictStatuses.Count} 条，排除已玩 {settings.ExcludeRecentlyPlayed}）");

    List<OsuBeatmapset> candidates = [];
    await api.SearchCandidatesInBatchesAsync(settings, minStars, maxStars, (batch, _) =>
    {
        candidates.AddRange(batch);
        return Task.CompletedTask;
    }, cancellation.Token);
    int eligible = 0;
    foreach (OsuBeatmapset set in candidates)
    {
        foreach (OsuBeatmap beatmap in set.Beatmaps)
        {
            if (CandidateFilter.Eligible(set, beatmap, settings))
                eligible += 1;
        }
    }
    Console.WriteLine($"候选谱面集 {candidates.Count}，合格难度 {eligible}");

    // 用「没键型数据」时的排序挑要抓哪些：和 App 里预取的选法一致，抓出来才有代表性。
    IReadOnlyList<RecommendationItem> baseline = new RecommendationEngine().Rank(
        candidates, profile, settings, minStars, maxStars, new HashSet<int>(), maxFetch, false,
        null, null, 0, null, new Random(7));
    Console.WriteLine($"准备抓取前 {baseline.Count} 张（按无键型排序）");

    OsuFileFetcher fetcher = new();
    PatternLibrary library = new();
    int fetched = 0;
    int failed = 0;
    System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    foreach (RecommendationItem item in baseline)
    {
        string? text = await fetcher.TryFetchAsync(item.BeatmapId, cancellation.Token);
        if (text is null)
        {
            failed += 1;
            continue;
        }
        if (library.Ingest(text, item.BeatmapId, item.BeatmapsetId))
            fetched += 1;
    }
    clock.Stop();
    Console.WriteLine($"抓取解析 {fetched} 张，失败 {failed} 张（{clock.ElapsedMilliseconds} ms）");
    if (fetched == 0)
        return 3;

    // 同一批候选、同一个随机种子，只差「有没有键型数据」。
    Func<int, ManiaPattern?> lookup = id => library.TryGetVerified(id);
    IReadOnlyList<RecommendationItem> withoutPattern = new RecommendationEngine().Rank(
        candidates, profile, settings, minStars, maxStars, new HashSet<int>(), 40, false,
        null, null, 0, null, new Random(7));
    IReadOnlyList<RecommendationItem> withPattern = new RecommendationEngine().Rank(
        candidates, profile, settings, minStars, maxStars, new HashSet<int>(), 40, false,
        lookup, null, 0, null, new Random(7));

    Dictionary<int, double> noPatternScore = withoutPattern.ToDictionary(item => item.BeatmapsetId, item => item.Score);
    Dictionary<int, double> withPatternScore = withPattern.ToDictionary(item => item.BeatmapsetId, item => item.Score);
    Dictionary<int, string> withPatternReason = withPattern.ToDictionary(item => item.BeatmapsetId, item => item.Reason);

    StringBuilder csv = new();
    csv.AppendLine("beatmapsetId,beatmapId,artist,title,mapper,stars,length,year,lnRatio,jacks,chordstream,stream,coordination,density,wildcard,category,scoreNoPattern,scoreWithPattern");
    foreach (RecommendationItem item in baseline)
    {
        ManiaPattern? pattern = library.TryGetVerified(item.BeatmapId);
        if (pattern is null)
            continue;
        double share(string name) => pattern.CoreShares.TryGetValue(name, out double value) ? value : 0;
        csv.AppendLine(string.Join(",",
            item.BeatmapsetId,
            item.BeatmapId,
            quote(item.Artist),
            quote(item.Title),
            quote(item.Mapper),
            item.Stars.ToString("0.00", CultureInfo.InvariantCulture),
            item.LengthSeconds,
            item.RankedYear,
            pattern.LnRatio.ToString("0.000", CultureInfo.InvariantCulture),
            share("Jacks").ToString("0.000", CultureInfo.InvariantCulture),
            share("Chordstream").ToString("0.000", CultureInfo.InvariantCulture),
            share("Stream").ToString("0.000", CultureInfo.InvariantCulture),
            share("Coordination").ToString("0.000", CultureInfo.InvariantCulture),
            share("Density").ToString("0.000", CultureInfo.InvariantCulture),
            share("Wildcard").ToString("0.000", CultureInfo.InvariantCulture),
            quote(pattern.Category),
            noPatternScore.TryGetValue(item.BeatmapsetId, out double plain) ? plain.ToString("0.0000", CultureInfo.InvariantCulture) : "",
            withPatternScore.TryGetValue(item.BeatmapsetId, out double patterned) ? patterned.ToString("0.0000", CultureInfo.InvariantCulture) : "",
            quote(withPatternReason.TryGetValue(item.BeatmapsetId, out string? reason) ? reason : "")));
    }

    Directory.CreateDirectory(outDir);
    string csvPath = Path.Combine(outDir, "snapshot.csv");
    File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"快照：{csvPath}");

    static string quote(string value) => "\"" + (value ?? string.Empty).Replace("\"", "'") + "\"";
    return 0;
}

/// <summary>
/// 诊断模式：完全按本机 settings.json 跑一遍搜索，然后逐条统计是哪个筛选把候选清空的。
/// </summary>
static async Task<int> diagAsync(string[] args)
{
    SettingsStore store = new();
    AppSettings settings = await store.LoadAsync();
    Console.WriteLine($"设置：{store.SettingsPathForDisplay}");
    Console.WriteLine($"  用户={settings.Username} 模式={settings.Mode} 键数={settings.ManiaKeys}");
    Console.WriteLine($"  星级 {settings.MinimumStars}–{settings.MaximumStars} 长度 {settings.MinimumLengthSeconds}–{settings.MaximumLengthSeconds}s 年份 {settings.MinimumYear}–{settings.MaximumYear}");
    Console.WriteLine($"  tag 白名单 {settings.IncludedTags.Count} 条 / 排除 {settings.ExcludedTags.Count} 条");
    Console.WriteLine($"  状态 宽松=[{string.Join(",", settings.IncludedStatuses)}] 严格=[{string.Join(",", settings.StrictStatuses)}]");
    Console.WriteLine($"  类型 宽松=[{string.Join(",", settings.IncludedGenres)}] 严格=[{string.Join(",", settings.StrictGenres)}]");
    Console.WriteLine($"  语言 宽松=[{string.Join(",", settings.IncludedLanguages)}] 严格=[{string.Join(",", settings.StrictLanguages)}]");
    Console.WriteLine($"  排除已玩 {settings.ExcludeRecentlyPlayed} 排除已装 {settings.ExcludeInstalledBeatmaps} 已推荐历史 {settings.RecommendationHistory.Count} 条");

    string secret = SecretProtector.Unprotect(settings.ProtectedClientSecret);
    if (string.IsNullOrEmpty(settings.ClientId) || secret.Length == 0)
    {
        Console.WriteLine("拿不到 OAuth 凭据");
        return 2;
    }
    OsuApiClient api = new();
    api.Configure(settings.ClientId, secret);
    using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(10));
    PlayerProfile profile = await api.BuildProfileAsync(settings.Username, settings.Mode, settings.ManiaKeys, cancellation.Token);
    Console.WriteLine($"档案：样本 {profile.DifficultySampleCount} 张，已玩谱面集 {profile.PlayedBeatmapsetIds().Count} 个");

    // --diag pages:N [installed]：多翻几页，看服务端到底还有多少可拿。
    int pages = OsuApiClient.SearchPageBudget;
    foreach (string extra in args.Skip(1))
    {
        if (extra.StartsWith("pages:", StringComparison.Ordinal) && int.TryParse(extra[6..], out int parsed))
            pages = parsed;
    }
    List<OsuBeatmapset> candidates = [];
    System.Diagnostics.Stopwatch searchClock = System.Diagnostics.Stopwatch.StartNew();
    await api.SearchCandidatesInBatchesAsync(settings, settings.MinimumStars, settings.MaximumStars, (batch, _) =>
    {
        candidates.AddRange(batch);
        return Task.CompletedTask;
    }, cancellation.Token, wantsMore: () => true, maximumPages: pages);
    searchClock.Stop();
    Console.WriteLine($"搜索返回：{candidates.Count} 个谱面集（去重后 {candidates.Select(set => set.Id).Distinct().Count()}，页数上限 {pages}，{searchClock.ElapsedMilliseconds} ms）");
    if (candidates.Count == 0)
        return 0;

    HashSet<int> played = settings.ExcludeRecentlyPlayed ? [.. profile.PlayedBeatmapsetIds()] : [];
    Dictionary<string, int> rejected = [];
    int passed = 0;
    int eligibleDiffs = 0;
    List<string> samples = [];
    void reject(string reason, OsuBeatmapset set)
    {
        rejected[reason] = rejected.GetValueOrDefault(reason) + 1;
        if (samples.Count < 6)
            samples.Add($"{reason} ← {set.Artist} - {set.Title} [{set.Status}] tags={(set.Tags ?? "").Length}");
    }

    foreach (OsuBeatmapset set in candidates)
    {
        IReadOnlyList<string> setTags = AppSettings.ParseTags(set.Tags);
        if (played.Contains(set.Id)) { reject("已玩过", set); continue; }
        if (!settings.MatchesStrictSimpleFilters(set)) { reject("严格状态/类型/语言", set); continue; }
        if (settings.ExcludedTags.Any(tag => setTags.Contains(tag, StringComparer.OrdinalIgnoreCase))) { reject("命中排除 tag", set); continue; }
        if (settings.IncludedTags.Count > 0 && !settings.IncludedTags.Any(tag => setTags.Contains(tag, StringComparer.OrdinalIgnoreCase))) { reject("没有白名单 tag", set); continue; }
        int year = (set.RankedDate ?? set.SubmittedDate)?.Year ?? 0;
        if (year < settings.MinimumYear || year > settings.MaximumYear) { reject("年份", set); continue; }
        int diffs = 0;
        foreach (OsuBeatmap beatmap in set.Beatmaps)
        {
            if (!string.Equals(beatmap.Mode, settings.Mode, StringComparison.OrdinalIgnoreCase)) continue;
            if (settings.Mode == "mania" && (int)Math.Round(beatmap.CircleSize) != settings.ManiaKeys) continue;
            if (beatmap.DifficultyRating < settings.MinimumStars || beatmap.DifficultyRating > settings.MaximumStars) continue;
            if (beatmap.TotalLength < settings.MinimumLengthSeconds || beatmap.TotalLength > settings.MaximumLengthSeconds) continue;
            diffs += 1;
        }
        if (diffs == 0) { reject("没有合格难度（星级/时长）", set); continue; }
        passed += 1;
        eligibleDiffs += diffs;
    }

    Console.WriteLine();
    Console.WriteLine($"通过全部筛选的谱面集 {passed} 个，合格难度 {eligibleDiffs} 个");
    foreach ((string reason, int count) in rejected.OrderByDescending(pair => pair.Value))
        Console.WriteLine($"  被拒 {reason}: {count}");
    // --diag installed：连「排除已安装谱面」一起复现（App 里这个开关打开时最容易把池子筛空）。
    if (args.Skip(1).Contains("installed"))
    {
        await LocalBeatmapLibrary.RefreshAsync(store.CacheDirectory, cancellation.Token);
        settings.ExcludeInstalledBeatmaps = true;
        Console.WriteLine($"已安装库：{LocalBeatmapLibrary.InstalledBeatmapIds.Count} 个难度 / {LocalBeatmapLibrary.InstalledBeatmapsetIds.Count} 个谱面集（{LocalBeatmapLibrary.Note}）");
        int installedDiffs = 0;
        int survivors = 0;
        foreach (OsuBeatmapset set in candidates)
        {
            foreach (OsuBeatmap beatmap in set.Beatmaps)
            {
                if (!CandidateFilter.Eligible(set, beatmap, settings))
                    continue;
                installedDiffs += 1;
            }
        }
        Console.WriteLine($"打开「排除已安装」后，仍可推荐的难度：{installedDiffs} 个（0 就是池子被筛空的原因）");
        _ = survivors;
    }

    // 复现 publishPoolAsync 的那次排序：历史降权、已装排除都跟 App 一致。
    List<int> lowerPriority = [.. settings.RecommendationHistory];
    if (settings.ExcludeRecentlyPlayed)
        lowerPriority.AddRange(profile.PlayedBeatmapsetIds());
    int inHistory = candidates.Count(set => lowerPriority.Contains(set.Id));
    Console.WriteLine($"候选里有 {inHistory} 个在「已推荐/已玩」降权表里");
    IReadOnlyList<RecommendationItem> ranked = new RecommendationEngine().RankAvoidingHistory(
        candidates, profile, settings, settings.MinimumStars, settings.MaximumStars,
        new HashSet<int>(), lowerPriority, Math.Max(1, settings.PoolSize),
        null, null, settings.PatternWeight,
        settings.ExcludeInstalledBeatmaps && LocalBeatmapLibrary.HasData
            ? LocalBeatmapLibrary.InstalledBeatmapIds
            : null,
        new Random(1));
    Console.WriteLine($"排序结果：{ranked.Count} 首（池子大小 {settings.PoolSize}）");

    // 两段式：严格条件优先，不够再放宽补位（和 MainWindow.rankPool 一致）。
    IReadOnlySet<int>? installed = settings.ExcludeInstalledBeatmaps && LocalBeatmapLibrary.HasData
        ? LocalBeatmapLibrary.InstalledBeatmapIds
        : null;
    IReadOnlyList<RecommendationItem> strictPass = new RecommendationEngine().RankAvoidingHistory(
        candidates, profile, settings, settings.MinimumStars, settings.MaximumStars,
        new HashSet<int>(), lowerPriority, Math.Max(1, settings.PoolSize),
        null, null, settings.PatternWeight, installed, new Random(1));
    IReadOnlyList<RecommendationItem> fillPass = strictPass.Count >= Math.Max(1, settings.PoolSize)
        ? []
        : new RecommendationEngine().RankAvoidingHistory(
            candidates, profile, settings, settings.MinimumStars, settings.MaximumStars,
            new HashSet<int>(strictPass.Select(item => item.BeatmapsetId)), [], Math.Max(1, settings.PoolSize) - strictPass.Count,
            null, null, settings.PatternWeight, installed, new Random(1),
            relaxTagFilter: true, isFallback: true);
    Console.WriteLine($"两段式：严格 {strictPass.Count} 首，补位 {fillPass.Count} 首");
    foreach (RecommendationItem item in fillPass)
        Console.WriteLine($"  [补位] {item.Stars:0.00}★ {item.Artist} - {item.Title}");
    foreach (RecommendationItem item in ranked)
        Console.WriteLine($"  {item.Stars:0.00}★ {(item.IsRecycled ? "[回收] " : "")}{item.Artist} - {item.Title}");
    if (samples.Count > 0)
    {
        Console.WriteLine("样例：");
        foreach (string sample in samples)
            Console.WriteLine($"  {sample}");
    }
    return 0;
}

static void testCandidateDiagnosisExplainsEmptyPool()
{
    // 池子被筛空时必须能说清是哪一层挡的：tag 还是状态。
    AppSettings settings = recommendationSettings();
    settings.IncludedTags = ["jack"];
    settings.StrictStatuses = ["ranked"];
    settings.MinimumStars = 1;
    settings.MaximumStars = 10;

    OsuBeatmapset jack = taggedManiaSet(1800, 18001, "jack 叠键");
    OsuBeatmapset stream = taggedManiaSet(1801, 18011, "stream 流");
    OsuBeatmapset graveyard = taggedManiaSet(1802, 18021, "jack");
    graveyard.Status = "graveyard";

    CandidateDiagnosis diagnosis = CandidateFilter.Diagnose([jack, stream, graveyard], settings);
    assertEqual(3, diagnosis.Sets, "统计应覆盖全部候选谱面集");
    assertTrue(diagnosis.EligibleDiffs > 0, "符合 tag 与状态的谱面应算作可推荐");
    assertEqual(1, diagnosis.TagMissedSets, "tag 不匹配的谱面集应单独计数");
    assertEqual(1, diagnosis.StatusMissedSets, "状态不匹配的谱面集应单独计数");

    // 「排除已安装」只在库里真有数据时才生效，所以这里只验证它不会把未安装的算进去。
    settings.ExcludeInstalledBeatmaps = true;
    CandidateDiagnosis withInstalled = CandidateFilter.Diagnose([jack], settings);
    assertTrue(withInstalled.EligibleDiffs + withInstalled.InstalledDiffs > 0, "两种结局之和应等于之前可推荐的数量");
}

static void testFallbackFillsWhenTagFilterTooNarrow()
{
    // 白名单 tag 是硬筛时只有 1 个候选；补位（tag 降级为加权）应该把剩下的名额填上并标记。
    AppSettings settings = recommendationSettings();
    settings.IncludedTags = ["jack"];
    settings.MinimumStars = 1;
    settings.MaximumStars = 10;
    PlayerProfile profile = emptyProfile();
    List<OsuBeatmapset> sets = [taggedManiaSet(1900, 19001, "jack 叠键"), taggedManiaSet(1901, 19011, "stream 流")];
    RecommendationEngine engine = new();

    IReadOnlyList<RecommendationItem> strict = engine.RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int>(), [], 2);
    assertEqual(1, strict.Count, "严格条件下 tag 白名单是硬筛");
    assertTrue(!strict[0].IsFallback, "严格条件排出来的不是补位条目");

    IReadOnlyList<RecommendationItem> filler = engine.RankAvoidingHistory(
        sets, profile, settings, 1, 10, new HashSet<int> { strict[0].BeatmapsetId }, [], 1,
        null, null, 0, null, null, relaxTagFilter: true, isFallback: true);
    assertEqual(1, filler.Count, "补位应把不足的名额填上");
    assertEqual(1901, filler[0].BeatmapsetId, "补位拿的是 tag 不符合但其他条件合格的那张");
    assertTrue(filler[0].IsFallback, "补位条目必须标记出来，后面好替换");
}

static void testFallbackAllowsPlayedButKeepsExcludedTags()
{
    AppSettings settings = recommendationSettings();
    settings.IncludedTags = ["jack"];
    settings.ExcludedTags = ["gay"];
    settings.MinimumStars = 1;
    settings.MaximumStars = 10;
    PlayerProfile profile = emptyProfile();
    List<OsuBeatmapset> sets = [taggedManiaSet(1910, 19101, "jack"), taggedManiaSet(1911, 19111, "stream")];
    RecommendationEngine engine = new();

    // 严格条件：已推荐/已玩过的谱面被排除。
    IReadOnlyList<RecommendationItem> strict = engine.Rank(
        sets, profile, settings, 1, 10, new HashSet<int> { 1910 }, 2);
    assertEqual(0, strict.Count, "严格条件下被排除的谱面不该出现");

    // 补位：历史不再当排除项（MainWindow 会传空表），但排除 tag 依然硬。
    settings.ExcludedTags = ["gay", "jack"];
    IReadOnlyList<RecommendationItem> filler = engine.Rank(
        sets, profile, settings, 1, 10, new HashSet<int>(), 2, false, null, null, 0, null, null,
        relaxTagFilter: true, isFallback: true);
    assertTrue(filler.All(item => item.BeatmapsetId != 1910), "补位时「排除 tag」仍然生效");
    assertEqual(1, filler.Count, "补位允许 tag 不符合的谱面上来");
}

static void testFallbackCardsGetReplacedByStrictOnes()
{
    // 补位卡片只是占位：一旦够格的候选进来，重排就要把它换掉。
    RecommendationItem filler = poolCard(2001);
    RecommendationItem strict = poolCard(2002);
    List<RecommendationItem> next = PoolReconciler.Build([filler], [strict], new HashSet<int>(), 5);
    assertTrue(next.Select(item => item.BeatmapsetId).SequenceEqual(new[] { 2002 }), "够格的候选应替换补位卡片");
}

/// <summary>
/// A/B 实验：同一批真实候选，只在「有没有键型数据」上不同，比较 top5 的键型质量。
/// 用来回答「推出来的 jack 是算法还是运气」——如果带键型的 top5 明显更 jack，且覆盖率不低，就是算法。
/// 用法：--ab [轮数]
/// </summary>
static async Task<int> abTestAsync(string[] args)
{
    int rounds = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
    SettingsStore store = new();
    AppSettings settings = await store.LoadAsync();
    Console.WriteLine($"设置：{store.SettingsPathForDisplay} 用户={settings.Username}");
    Console.WriteLine($"  星级 {settings.MinimumStars}–{settings.MaximumStars}，tag 白名单 {settings.IncludedTags.Count} 条，排除已装 {settings.ExcludeInstalledBeatmaps}，排除已玩 {settings.ExcludeRecentlyPlayed}");

    string secret = SecretProtector.Unprotect(settings.ProtectedClientSecret);
    OsuApiClient api = new();
    api.Configure(settings.ClientId, secret);
    using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(30));
    PlayerProfile profile = await api.BuildProfileAsync(settings.Username, settings.Mode, settings.ManiaKeys, cancellation.Token);

    // --ab N noinstalled：临时关掉「排除已安装谱面」，用来对比「有供给」时算法的效果。
    if (args.Skip(1).Contains("noinstalled"))
    {
        settings.ExcludeInstalledBeatmaps = false;
        Console.WriteLine("已临时关闭「排除已安装谱面」");
    }

    PatternLibrary library = new();
    await library.LoadAsync();
    Console.WriteLine($"键型库：{library.Count} 条（真实解析 {library.ParsedCount}）");
    if (settings.ExcludeInstalledBeatmaps)
    {
        await LocalBeatmapLibrary.RefreshAsync(store.CacheDirectory, cancellation.Token);
        Console.WriteLine($"已安装库：{LocalBeatmapLibrary.InstalledBeatmapIds.Count} 个难度");
    }

    OsuFileFetcher fetcher = new();
    int startRotation = settings.SearchRotation;
    int withPatternHits = 0;
    int withoutPatternHits = 0;
    int sampled = 0;
    for (int round = 0; round < rounds; round += 1)
    {
        settings.SearchRotation = startRotation + round;
        List<OsuBeatmapset> candidates = [];
        await api.SearchCandidatesInBatchesAsync(settings, settings.MinimumStars, settings.MaximumStars, (batch, _) =>
        {
            candidates.AddRange(batch);
            return Task.CompletedTask;
        }, cancellation.Token);
        candidates = [.. candidates.GroupBy(set => set.Id).Select(group => group.First())];
        List<int> lowerPriority = [.. settings.RecommendationHistory];
        if (settings.ExcludeRecentlyPlayed)
            lowerPriority.AddRange(profile.PlayedBeatmapsetIds());
        IReadOnlySet<int>? installed = settings.ExcludeInstalledBeatmaps && LocalBeatmapLibrary.HasData
            ? LocalBeatmapLibrary.InstalledBeatmapIds
            : null;

        IReadOnlyList<RecommendationItem> withPattern = new RecommendationEngine().RankAvoidingHistory(
            candidates, profile, settings, settings.MinimumStars, settings.MaximumStars,
            new HashSet<int>(), lowerPriority, Math.Max(1, settings.PoolSize),
            id => library.TryGetVerified(id), null, settings.PatternWeight, installed, new Random(round + 1));
        IReadOnlyList<RecommendationItem> withoutPattern = new RecommendationEngine().RankAvoidingHistory(
            candidates, profile, settings, settings.MinimumStars, settings.MaximumStars,
            new HashSet<int>(), lowerPriority, Math.Max(1, settings.PoolSize),
            null, null, settings.PatternWeight, installed, new Random(round + 1));

        Dictionary<int, double> shares = [];
        foreach (RecommendationItem item in withPattern.Concat(withoutPattern))
        {
            if (shares.ContainsKey(item.BeatmapId))
                continue;
            ManiaPattern? known = library.TryGetVerified(item.BeatmapId);
            if (known is null)
            {
                string? text = await fetcher.TryFetchAsync(item.BeatmapId, cancellation.Token);
                if (text is not null && library.Ingest(text, item.BeatmapId, item.BeatmapsetId))
                    known = library.TryGetVerified(item.BeatmapId);
            }
            shares[item.BeatmapId] = known is not null && known.CoreShares.TryGetValue("Jacks", out double jack) ? jack : -1;
        }

        string Describe(IReadOnlyList<RecommendationItem> items)
        {
            IEnumerable<string> cells = items.Select(item =>
            {
                double share = shares.GetValueOrDefault(item.BeatmapId, -1);
                return $"{item.Title[..Math.Min(item.Title.Length, 22)]}={share:0.00}";
            });
            return string.Join(" | ", cells);
        }
        int Hits(IReadOnlyList<RecommendationItem> items) => items.Count(item => shares.GetValueOrDefault(item.BeatmapId, -1) >= 0.25);
        int VerifiedCoverage(IReadOnlyList<RecommendationItem> items) => items.Count(item => library.TryGetVerified(item.BeatmapId) is not null);

        // 库里叠键占比高的谱，为什么没进候选？逐条给原因。
        if (round == 0)
        {
            HashSet<int> candidateSets = [.. candidates.Select(set => set.Id)];
            HashSet<int> installedSets = settings.ExcludeInstalledBeatmaps && LocalBeatmapLibrary.HasData
                ? [.. LocalBeatmapLibrary.InstalledBeatmapsetIds]
                : [];
            HashSet<int> historySets = [.. lowerPriority];
            Console.WriteLine("库里叠键 ≥50% 的谱，为什么没进候选：");
            foreach (var pair in library.PatternsWhereJackAtLeast(0.5).Take(6))
            {
                ManiaPattern pattern = pair.Value;
                bool inCandidates = candidateSets.Contains(pattern.BeatmapsetId);
                bool isInstalledSet = installedSets.Contains(pattern.BeatmapsetId);
                bool inHistory = historySets.Contains(pattern.BeatmapsetId);
                Console.WriteLine($"  set={pattern.BeatmapsetId} 叠键 {pair.Key:0.00} 星级≈? 候选={inCandidates} 已安装={isInstalledSet} 已推荐/已玩={inHistory}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"轮次 {round + 1}（rotation={settings.SearchRotation}）：候选 {candidates.Count} 个谱面集，池子 {settings.PoolSize}");
        Console.WriteLine($"  带键型 top{withPattern.Count}（其中已验证 {VerifiedCoverage(withPattern)} 首）：{Describe(withPattern)}");
        Console.WriteLine($"  无键型 top{withoutPattern.Count}：{Describe(withoutPattern)}");
        Console.WriteLine($"  叠键 ≥25%：带键型 {Hits(withPattern)}/{withPattern.Count}，无键型 {Hits(withoutPattern)}/{withoutPattern.Count}");
        withPatternHits += Hits(withPattern);
        withoutPatternHits += Hits(withoutPattern);
        sampled += withPattern.Count;
    }

    Console.WriteLine();
    Console.WriteLine($"合计：带键型 {withPatternHits}/{sampled}，无键型 {withoutPatternHits}/{sampled} 首叠键 ≥25%");
    return 0;
}

/// <summary>
/// 按 id 查谱面集元数据（星级/状态/时长/年份/tag），用来判断「为什么这张谱进不了候选」：
/// 是星级窗口外、还是被 tag 白名单挡掉、还是根本没被搜索翻到。
/// 用法：--sets 2007310 1751372 ...
/// </summary>
static async Task<int> setsAsync(string[] args)
{
    SettingsStore store = new();
    AppSettings settings = await store.LoadAsync();
    string secret = SecretProtector.Unprotect(settings.ProtectedClientSecret);
    using HttpClient http = new();
    using HttpResponseMessage tokenResponse = await http.PostAsJsonAsync(
        "https://osu.ppy.sh/oauth/token",
        new { client_id = settings.ClientId, client_secret = secret, grant_type = "client_credentials", scope = "public" });
    tokenResponse.EnsureSuccessStatusCode();
    string token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString() ?? "";
    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("osu-roam-investigation");

    Console.WriteLine($"查询星级窗口 {settings.MinimumStars}–{settings.MaximumStars}，tag 白名单 {settings.IncludedTags.Count} 条");
    foreach (string raw in args.Skip(1))
    {
        if (!int.TryParse(raw, out int setId))
            continue;
        using HttpResponseMessage response = await http.GetAsync($"https://osu.ppy.sh/api/v2/beatmapsets/{setId}");
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"  {setId}: HTTP {(int)response.StatusCode}");
            continue;
        }
        JsonElement root = await response.Content.ReadFromJsonAsync<JsonElement>();
        string status = root.GetProperty("status").GetString() ?? "";
        string artist = root.TryGetProperty("artist", out JsonElement a) ? a.GetString() ?? "" : "";
        string title = root.TryGetProperty("title", out JsonElement t) ? t.GetString() ?? "" : "";
        int year = 0;
        foreach (string field in new[] { "ranked_date", "submitted_date" })
        {
            if (root.TryGetProperty(field, out JsonElement date) && date.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(date.GetString(), out DateTimeOffset parsedDate))
            {
                year = parsedDate.Year;
                break;
            }
        }
        string[] tags = (root.TryGetProperty("tags", out JsonElement tagElement) ? tagElement.GetString() ?? "" : "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<string> setTags = AppSettings.ParseTags(string.Join(' ', tags));
        bool tagOk = settings.IncludedTags.Count == 0 ||
                     settings.IncludedTags.Any(tag => setTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        Console.WriteLine();
        Console.WriteLine($"{setId} {artist} - {title}  状态={status} 年份={year} tag命中={tagOk}");
        foreach (JsonElement beatmap in root.GetProperty("beatmaps").EnumerateArray())
        {
            string mode = beatmap.GetProperty("mode").GetString() ?? "";
            if (mode != "mania")
                continue;
            double cs = beatmap.GetProperty("cs").GetDouble();
            double star = beatmap.GetProperty("difficulty_rating").GetDouble();
            int length = beatmap.TryGetProperty("total_length", out JsonElement len) ? len.GetInt32() : 0;
            bool inRange = star >= settings.MinimumStars && star <= settings.MaximumStars;
            Console.WriteLine($"   {beatmap.GetProperty("id").GetInt32()} {cs:0}K {star:0.00}★ {length}s 版本={beatmap.GetProperty("version").GetString()}" +
                              $" 星级内={inRange} 键数对={(int)Math.Round(cs) == settings.ManiaKeys}");
        }
    }
    return 0;
}

/// <summary>
/// 关键字搜索能翻多深：q=jack 这类查询按页取，看看能捞出多少候选、能不能覆盖到已知的 jack 谱。
/// 用法：--hint jack 6 [要检查的 set id...]
/// </summary>
static async Task<int> hintAsync(string[] args)
{
    SettingsStore store = new();
    AppSettings settings = await store.LoadAsync();
    string secret = SecretProtector.Unprotect(settings.ProtectedClientSecret);
    string keyword = args[1];
    int pages = int.Parse(args[2], CultureInfo.InvariantCulture);
    HashSet<int> wanted = [.. args.Skip(3).Select(int.Parse)];
    using HttpClient http = new();
    using HttpResponseMessage tokenResponse = await http.PostAsJsonAsync("https://osu.ppy.sh/oauth/token",
        new { client_id = settings.ClientId, client_secret = secret, grant_type = "client_credentials", scope = "public" });
    tokenResponse.EnsureSuccessStatusCode();
    string token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString() ?? "";
    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("osu-roam-investigation");

    string query = FormattableString.Invariant(
        $"stars>={settings.MinimumStars:0.0} stars<={settings.MaximumStars:0.0} length>={settings.MinimumLengthSeconds} length<={settings.MaximumLengthSeconds} keys={settings.ManiaKeys} {keyword}");
    string? cursor = null;
    int seen = 0;
    int total = 0;
    HashSet<int> found = [];
    for (int page = 0; page < pages; page += 1)
    {
        string path = $"https://osu.ppy.sh/api/v2/beatmapsets/search?m=3&s=any&sort=relevance&q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrEmpty(cursor))
            path += $"&cursor_string={Uri.EscapeDataString(cursor)}";
        using HttpResponseMessage response = await http.GetAsync(path);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"  第 {page + 1} 页 HTTP {(int)response.StatusCode}");
            break;
        }
        JsonElement root = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (root.TryGetProperty("total", out JsonElement totalElement) && totalElement.ValueKind == JsonValueKind.Number)
            total = Math.Max(total, totalElement.GetInt32());
        JsonElement sets = root.GetProperty("beatmapsets");
        int count = sets.GetArrayLength();
        int matchedSets = 0;
        foreach (JsonElement set in sets.EnumerateArray())
        {
            int id = set.GetProperty("id").GetInt32();
            if (wanted.Contains(id))
                found.Add(id);
            IReadOnlyList<string> tags = AppSettings.ParseTags(set.GetProperty("tags").GetString() ?? "");
            if (settings.IncludedTags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                matchedSets += 1;
        }
        seen += count;
        Console.WriteLine($"  第 {page + 1} 页：{count} 个谱面集（其中通过 tag 白名单 {matchedSets}），累计 {seen}");
        cursor = root.TryGetProperty("cursor_string", out JsonElement cursorElement) && cursorElement.ValueKind == JsonValueKind.String
            ? cursorElement.GetString()
            : null;
        if (string.IsNullOrEmpty(cursor))
            break;
    }
    Console.WriteLine($"q={keyword} 翻 {pages} 页：累计 {seen} 个谱面集，服务端命中约 {total}");
    if (wanted.Count > 0)
        Console.WriteLine($"  目标 set 命中：{string.Join(",", found)}（缺 {string.Join(",", wanted.Except(found))}）");
    return 0;
}

static void testKeywordCandidatesAreJudgedByVerifiedPattern()
{
    // 关键字召回但 tag 白名单没命中：不靠 tag 判断，靠真实解析出的键型裁定。
    AppSettings settings = recommendationSettings();
    settings.IncludedTags = ["artcore"];
    settings.SelectedPatterns = ["Jacks"];
    settings.MinimumStars = 1;
    settings.MaximumStars = 10;
    PlayerProfile profile = emptyProfile();
    OsuBeatmapset untaggedJack = taggedManiaSet(2100, 21001, "electronic");
    OsuBeatmapset untaggedStream = taggedManiaSet(2101, 21011, "electronic");

    ManiaPattern Pattern(int beatmapId, int beatmapsetId, string core, double share) => new()
    {
        BeatmapId = beatmapId,
        BeatmapsetId = beatmapsetId,
        Source = PatternSource.Osu,
        AnalyzerVersion = ManiaPatternAnalyzer.Version,
        CoreShares = new() { [core] = share }
    };
    ManiaPattern jack = Pattern(21001, 2100, "Jacks", 0.8);
    ManiaPattern stream = Pattern(21011, 2101, "Stream", 0.9);
    Func<int, ManiaPattern?> lookup = id => id == 21001 ? jack : id == 21011 ? stream : null;

    RecommendationEngine engine = new();
    IReadOnlyList<RecommendationItem> withoutKeywordFlag = engine.RankAvoidingHistory(
        [untaggedJack, untaggedStream], profile, settings, 1, 10, new HashSet<int>(), [], 2,
        lookup, null, 0, null, new Random(1));
    assertEqual(0, withoutKeywordFlag.Count, "tag 不符且不是关键字召回的谱面不该进池");

    HashSet<int> keywordSets = [2100, 2101];
    IReadOnlyList<RecommendationItem> withKeywordFlag = engine.RankAvoidingHistory(
        [untaggedJack, untaggedStream], profile, settings, 1, 10, new HashSet<int>(), [], 2,
        lookup, null, 0, null, new Random(1), false, false, keywordSets);
    assertEqual(1, withKeywordFlag.Count, "关键字召回里只有真实键型符合的那张能进池");
    assertEqual(2100, withKeywordFlag[0].BeatmapsetId, "进池的应该是叠键占比 0.8 的谱");
}

static void testKeywordCandidatesEnterVerificationQueue()
{
    // 关键字召回的谱面即使不符合 tag 白名单，也要能进入验证队列（否则永远拿不到真实键型）。
    AppSettings settings = recommendationSettings();
    settings.IncludedTags = ["artcore"];
    settings.SelectedPatterns = ["Jacks"];
    settings.MinimumStars = 1;
    settings.MaximumStars = 10;
    OsuBeatmapset set = taggedManiaSet(2200, 22001, "electronic");
    int[] alternation = [0, 1, 2, 3];
    List<string> notes = [];
    for (int index = 0; index < 20; index += 1)
        notes.Add(note(4, alternation[index % alternation.Length], 1000 + index * 100));
    string text = osuFile(4, notes);

    List<int> fetched = [];
    PatternPrefetcher.RunAsync([set], settings, new PatternLibrary(), (beatmapId, _) =>
    {
        fetched.Add(beatmapId);
        return Task.FromResult<string?>(text);
    }, 1, PatternPrefetcher.DefaultDeadline, null, CancellationToken.None).GetAwaiter().GetResult();
    assertEqual(0, fetched.Count, "tag 不符的普通候选不该占用预取预算");

    PatternPrefetcher.RunAsync([set], settings, new PatternLibrary(), (beatmapId, _) =>
    {
        fetched.Add(beatmapId);
        return Task.FromResult<string?>(text);
    }, 1, PatternPrefetcher.DefaultDeadline, null, CancellationToken.None, new HashSet<int> { 2200 }).GetAwaiter().GetResult();
    assertEqual(1, fetched.Count, "关键字召回的谱面应进入验证队列");
}

/// <summary>
/// 推荐算法自验证：完全按 App 的管线跑一遍（关键字翻页 → 抓 .osu 验证 → 严格/补位排序），
/// 再用「进池谱面的真实键型」算精度，和「没有键型数据时的同一排序」对比。
///
/// 用法：--verify [轮数]
/// 输出：每轮的候选/验证/池子明细 + 精度汇总（选中的键型占比 >= 25% 视作命中）。
/// </summary>
static async Task<int> verifyAsync(string[] args)
{
    int rounds = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
    SettingsStore store = new();
    AppSettings settings = await store.LoadAsync();
    Console.WriteLine($"设置：{store.SettingsPathForDisplay}");
    Console.WriteLine($"  用户={settings.Username} 星级 {settings.MinimumStars}–{settings.MaximumStars} 键数={settings.ManiaKeys}");
    Console.WriteLine($"  勾选键型=[{string.Join(",", settings.SelectedPatterns)}] 只要面谱={settings.PreferLn} 池大小={settings.PoolSize}");
    Console.WriteLine($"  tag 白名单 {settings.IncludedTags.Count} 条，排除已装 {settings.ExcludeInstalledBeatmaps}，排除已玩 {settings.ExcludeRecentlyPlayed}");

    string secret = SecretProtector.Unprotect(settings.ProtectedClientSecret);
    OsuApiClient api = new();
    api.Configure(settings.ClientId, secret);
    using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(40));
    PlayerProfile profile = await api.BuildProfileAsync(settings.Username, settings.Mode, settings.ManiaKeys, cancellation.Token);
    PatternLibrary library = new();
    await library.LoadAsync();
    if (settings.ExcludeInstalledBeatmaps)
        await LocalBeatmapLibrary.RefreshAsync(store.CacheDirectory, cancellation.Token);
    Console.WriteLine($"  键型库 {library.Count} 条（真实解析 {library.ParsedCount}）；已安装 {LocalBeatmapLibrary.InstalledBeatmapIds.Count} 个难度");

    OsuFileFetcher fetcher = new();
    int startRotation = settings.SearchRotation;
    int poolSize = Math.Max(1, settings.PoolSize);
    int poolHits = 0;
    int poolSamples = 0;
    int baselineHits = 0;
    int baselineSamples = 0;
    int fallbackFilled = 0;

    for (int round = 0; round < rounds; round += 1)
    {
      try
      {
        settings.SearchRotation = startRotation + round;
        List<OsuBeatmapset> candidates = [];
        HashSet<int> keywordSets = [];
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        await api.SearchCandidatesInBatchesAsync(settings, settings.MinimumStars, settings.MaximumStars, (batch, fromKeyword) =>
        {
            candidates.AddRange(batch);
            if (fromKeyword)
                keywordSets.UnionWith(batch.Select(set => set.Id));
            return Task.CompletedTask;
        }, cancellation.Token, maximumPages: OsuApiClient.SearchPageBudget, hintPages: OsuApiClient.HintPageBudget);
        candidates = [.. candidates.GroupBy(set => set.Id).Select(group => group.First())];
        long searchMs = clock.ElapsedMilliseconds;

        // 准入：按「先判键型再进候选」跑 —— 够格攒到 PoolSize×2 就停，最多 3 轮 × 12 张。
        clock.Restart();
        HashSet<int> admitted = [];
        HashSet<int> mismatch = [];
        int wanted = poolSize * 2;
        PrefetchResult prefetch = new(0, 0, 0, 0, 0, false, false);
        for (int admitRound = 0; admitRound < 3; admitRound += 1)
        {
            int before = admitted.Count;
            prefetch = await PatternPrefetcher.RunAsync(
                candidates, settings, library, (beatmapId, token) => fetcher.TryFetchAsync(beatmapId, token),
                12, TimeSpan.FromSeconds(8), null, cancellation.Token, keywordSets,
                onIngested: (beatmapId, beatmapsetId) =>
                {
                    if (CandidateAdmission.IsStrict(library.TryGetVerified(beatmapId), settings))
                        admitted.Add(beatmapsetId);
                    else
                        mismatch.Add(beatmapsetId);
                },
                targetOverride: wanted);
            if (admitted.Count >= wanted || admitted.Count == before)
                break;
        }
        long verifyMs = clock.ElapsedMilliseconds;
        List<OsuBeatmapset> admittedSets = [.. candidates.Where(set => admitted.Contains(set.Id))];
        List<OsuBeatmapset> mismatchSets = [.. candidates.Where(set => mismatch.Contains(set.Id))];
        Console.WriteLine($"  准入：达标 {admittedSets.Count} 个谱面集，备用 {mismatchSets.Count} 个（从 {candidates.Count} 个候选里判出）");

        List<int> lowerPriority = [.. settings.RecommendationHistory];
        if (settings.ExcludeRecentlyPlayed)
            lowerPriority.AddRange(profile.PlayedBeatmapsetIds());
        IReadOnlySet<int>? installed = settings.ExcludeInstalledBeatmaps && LocalBeatmapLibrary.HasData
            ? LocalBeatmapLibrary.InstalledBeatmapIds
            : null;
        Random seed = new(round + 1);
        Func<int, ManiaPattern?> lookup = id => library.TryGetVerified(id);

        IReadOnlyList<RecommendationItem> strict = new RecommendationEngine().RankAvoidingHistory(
            admittedSets, profile, settings, settings.MinimumStars, settings.MaximumStars,
            new HashSet<int>(), lowerPriority, poolSize, lookup, null, settings.PatternWeight, installed, seed,
            false, false, keywordSets);
        IReadOnlyList<RecommendationItem> filler = strict.Count >= poolSize
            ? []
            : new RecommendationEngine().RankAvoidingHistory(
                mismatchSets, profile, settings, settings.MinimumStars, settings.MaximumStars,
                new HashSet<int>(strict.Select(item => item.BeatmapsetId)), [], poolSize - strict.Count,
                lookup, null, settings.PatternWeight, installed, seed, true, true, keywordSets);
        List<RecommendationItem> pool = [.. strict, .. filler];
        // 公平基线：同样的候选与放开的 tag（否则关键字召回的谱面会被 tag 拦掉，
        // 等于让基线对着一个更小的池子，比出来的差距是假的），只是没有键型数据。
        IReadOnlyList<RecommendationItem> baseline = new RecommendationEngine().RankAvoidingHistory(
            candidates, profile, settings, settings.MinimumStars, settings.MaximumStars,
            new HashSet<int>(), lowerPriority, poolSize, null, null, settings.PatternWeight, installed, seed,
            true, false, null);

        // 池内还没验证的谱面补抓一次，否则精度算不准（这就是 App 里预下载后解析的那一步）。
        foreach (RecommendationItem item in pool.Concat(baseline))
        {
            if (lookup(item.BeatmapId) is not null)
                continue;
            string? text = await fetcher.TryFetchAsync(item.BeatmapId, cancellation.Token);
            if (text is not null)
                library.Ingest(text, item.BeatmapId, item.BeatmapsetId);
        }

        double Share(RecommendationItem item)
        {
            ManiaPattern? pattern = lookup(item.BeatmapId);
            if (pattern is null)
                return -1;
            if (settings.SelectedPatterns.Count > 0)
                return pattern.SelectedShare(settings.SelectedPatterns);
            return pattern.IsLnChart ? 1 : 0;
        }
        int Hits(IReadOnlyList<RecommendationItem> items) => items.Count(item => Share(item) >= 0.25);
        if (filler.Count > 0)
            fallbackFilled += 1;
        poolHits += Hits(pool);
        poolSamples += pool.Count;
        baselineHits += Hits(baseline);
        baselineSamples += baseline.Count;

        Console.WriteLine();
        Console.WriteLine($"轮次 {round + 1}（rotation={settings.SearchRotation}）：候选 {candidates.Count}（关键字召回 {keywordSets.Count} 个谱面集）");
        Console.WriteLine($"  检索 {searchMs} ms；验证 {prefetch.Attempted} 次 / 解析 {prefetch.Parsed} / 命中 {prefetch.Hits}（{verifyMs} ms）");
        Console.WriteLine($"  严格 {strict.Count} 首 + 补位 {filler.Count} 首 = 池 {pool.Count}/{poolSize}");
        foreach (RecommendationItem item in pool)
        {
            double share = Share(item);
            Console.WriteLine($"    {(item.IsFallback ? "[补位] " : string.Empty)}{share:0.00}  {item.Stars:0.00}★ {item.Title[..Math.Min(item.Title.Length, 34)]}");
        }
        Console.WriteLine($"  基线（无键型数据）top{baseline.Count}：{Hits(baseline)} 首命中；带键型：{Hits(pool)}/{pool.Count}");
      }
      catch (Exception exception) when (exception is not OperationCanceledException)
      {
        Console.WriteLine();
        Console.WriteLine($"轮次 {round + 1} 失败（网络）：{exception.GetType().Name} {exception.Message}");
        Console.WriteLine("  —— 该轮跳过，继续下一轮");
      }
    }

    Console.WriteLine();
    Console.WriteLine("=== 汇总 ===");
    Console.WriteLine($"带键型：{poolHits}/{poolSamples} 首命中（选用键型占比 >= 25%）");
    Console.WriteLine($"无键型：{baselineHits}/{baselineSamples} 首命中");
    Console.WriteLine($"补位轮次：{fallbackFilled}/{rounds}");
    double precision = poolSamples == 0 ? 0 : (double)poolHits / poolSamples;
    double baselinePrecision = baselineSamples == 0 ? 0 : (double)baselineHits / baselineSamples;
    Console.WriteLine($"精度：{precision:P0} vs 基线 {baselinePrecision:P0}（差 {(precision - baselinePrecision):P0}）");
    if (poolSamples == 0)
        Console.WriteLine("结论：没有可评估的池子（候选/筛选条件过窄）");
    else if (precision - baselinePrecision < 0.10)
        Console.WriteLine("结论：键型项没有显著拉开差距 —— 供给或权重还需要调");
    else
        Console.WriteLine("结论：键型项显著有效（进池谱面的真实键型占比明显高于无键型基线）");
    return 0;
}

static void testPoolDownloadOrderingPutsQualifyingFirst()
{
    // 冷启动时先出现的补位卡不能占住下载通道：够格的候选要排在它们前面。
    RecommendationItem playing = poolCard(3001);
    RecommendationItem fallbackA = new()
    {
        BeatmapsetId = 3002, BeatmapId = 30021, Artist = "a", Title = "fallback-a", DifficultyName = "4K",
        Mapper = "m", Stars = 3, Bpm = 180, LengthSeconds = 120, RankedYear = 2024, Score = 1, IsFallback = true
    };
    RecommendationItem strict = poolCard(3003);
    RecommendationItem downloaded = poolCard(3004);
    downloaded.DownloadState = DownloadState.Downloaded;
    RecommendationItem failed = poolCard(3005);
    failed.DownloadState = DownloadState.Failed;

    List<RecommendationItem> order = PoolDownloadPlanner.Order(
        [fallbackA, strict, downloaded, failed, playing], playing, null, new HashSet<int>());

    assertTrue(order[0].BeatmapsetId == 3001, "正在玩的那首要排第一");
    int strictIndex = order.FindIndex(item => item.BeatmapsetId == 3003);
    int fallbackIndex = order.FindIndex(item => item.BeatmapsetId == 3002);
    assertTrue(strictIndex >= 0 && fallbackIndex >= 0 && strictIndex < fallbackIndex, "够格候选要排在补位卡前面");
    assertTrue(order.All(item => item.BeatmapsetId != 3004), "已经下完的不该再排队");

    // 失败过一次的不再自动重试，避免死循环。
    List<int> retried = [3005];
    List<RecommendationItem> again = PoolDownloadPlanner.Order([failed], null, null, new HashSet<int>(retried));
    assertEqual(0, again.Count, "失败过一次的谱面集不再自动重试");
}

static void testDownloadCancelRuleUsesMembershipNotQueue()
{
    // 线上事故：用「待下载清单」判断要不要取消，会把刚启动的下载立刻取消 → 无限重启。
    RecommendationItem waiting = poolCard(3101);
    RecommendationItem downloading = poolCard(3102);
    downloading.DownloadState = DownloadState.Downloading;
    RecommendationItem queued = poolCard(3103);
    queued.DownloadState = DownloadState.Queued;
    RecommendationItem done = poolCard(3104);
    done.DownloadState = DownloadState.Downloaded;
    List<RecommendationItem> pool = [waiting, downloading, queued, done];

    assertTrue(!PoolDownloadPlanner.ShouldCancel(3101, pool), "还在排队、需要下载的不能取消");
    assertTrue(!PoolDownloadPlanner.ShouldCancel(3102, pool), "正在下载的不能取消（这正是事故点）");
    assertTrue(!PoolDownloadPlanner.ShouldCancel(3103, pool), "已排队的不能取消");
    assertTrue(PoolDownloadPlanner.ShouldCancel(3104, pool), "已经下完的不再需要下载，可以释放通道");
    assertTrue(PoolDownloadPlanner.ShouldCancel(3199, pool), "已经不在池子里的必须取消");
}

static void testCandidateAdmissionJudgesByVerifiedPattern()
{
    AppSettings settings = recommendationSettings();
    settings.SelectedPatterns = ["Jacks"];
    // SelectedShare 是「所选键型在六个核心型里的时间占比」，所以没被选中的键型也要写进去。
    ManiaPattern jack = new() { CoreShares = new() { ["Jacks"] = 0.4, ["Stream"] = 0.6 }, LnRatio = 0 };
    ManiaPattern weak = new() { CoreShares = new() { ["Jacks"] = 0.1, ["Stream"] = 0.9 }, LnRatio = 0 };
    assertTrue(CandidateAdmission.IsStrict(jack, settings), "叠键 40% 应判定为达标候选");
    assertTrue(!CandidateAdmission.IsStrict(weak, settings), "叠键 10% 只能当备用");
    assertTrue(!CandidateAdmission.IsStrict(null, settings), "没解析出键型就不算达标（准入必判）");

    settings.SelectedPatterns = [];
    settings.PreferLn = true;
    ManiaPattern ln = new() { LnRatio = 0.4, CoreShares = [] };
    ManiaPattern rice = new() { LnRatio = 0.05, CoreShares = [] };
    assertTrue(CandidateAdmission.IsStrict(ln, settings), "「只要面谱」时 LN 谱达标");
    assertTrue(!CandidateAdmission.IsStrict(rice, settings), "非 LN 谱只能当备用");
}

static void testLnThresholdCountsAsFaceChart()
{
    assertTrue(new ManiaPattern { LnRatio = 0.21 }.IsLnChart, "LN 21% 应算面谱（1.5.3 门槛 0.20）");
    assertTrue(new ManiaPattern { LnRatio = 0.20 }.IsLnChart, "刚好 20% 算面谱");
    assertTrue(!new ManiaPattern { LnRatio = 0.19 }.IsLnChart, "LN 19% 不算面谱");
}

static void testLnTriangleFillsCircleWhenBalancedAtFullLn()
{
    // 锚点：LN 100% + 三项等分 ⇒ 三个顶点都贴到外接圆（长度 1.0）。
    ManiaPattern pattern = new()
    {
        LnRatio = 1.0,
        CoreShares = new() { ["Coordination"] = 0.3, ["Density"] = 0.3, ["Wildcard"] = 0.3 }
    };

    double[]? lens = LnRadarBuilder.Build(pattern);
    assertTrue(lens is { Length: 3 }, "LN 100% 且三项等分应给出三个顶点");
    assertNear(1.0, lens![0], "协调顶点应贴外接圆");
    assertNear(1.0, lens[1], "密度顶点应贴外接圆");
    assertNear(1.0, lens[2], "杂项顶点应贴外接圆");
}

static void testLnTriangleScalesWithLnRatioAndShares()
{
    // 份额按最大项归一，整体再乘 f(LN) = 2·LN/(1+LN)。
    ManiaPattern pattern = new()
    {
        LnRatio = 0.5,
        CoreShares = new() { ["Coordination"] = 0.2, ["Density"] = 0.4, ["Wildcard"] = 0.1 }
    };

    double[]? lens = LnRadarBuilder.Build(pattern);
    assertTrue(lens is { Length: 3 }, "份额合法时应给出三个顶点");
    assertNear(2.0 / 3.0, lens![1], "最大份额那根应等于 f(0.5) = 2/3");
    assertNear(1.0 / 3.0, lens[0], "协调 = 0.2/0.4 × f");
    assertNear(1.0 / 6.0, lens[2], "杂项 = 0.1/0.4 × f");

    // LN 占比越高三角越大（单调）。
    ManiaPattern lighter = new()
    {
        LnRatio = 0.25,
        CoreShares = new() { ["Coordination"] = 0.2, ["Density"] = 0.4, ["Wildcard"] = 0.1 }
    };
    double[]? lighterLens = LnRadarBuilder.Build(lighter);
    assertTrue(lighterLens![1] < lens[1], "LN 25% 的三角应小于 LN 50%");

    // 没有长条、或者三个 LN 核心型都没命中时不该画。
    assertTrue(LnRadarBuilder.Build(new ManiaPattern { LnRatio = 0, CoreShares = new() { ["Coordination"] = 0.4 } }) is null,
        "LN 为 0 时不画三角");
    assertTrue(LnRadarBuilder.Build(new ManiaPattern { LnRatio = 0.6, CoreShares = new() { ["Stream"] = 1 } }) is null,
        "三个 LN 核心型全为 0 时不画三角");
}

static void testUpdateVersionOrdering()
{
    static UpdateService.UpdateVersion V(string text) => UpdateService.UpdateVersion.Parse(text);

    assertTrue(V("v1.5.6.2") > V("1.5.6fix"), "补丁版要排在 fix 之后");
    assertTrue(V("1.5.6fix") > V("1.5.6"), "fix 要排在正式版之后");
    assertTrue(V("1.5.6") > V("1.5.3-beta2"), "正式版要排在 beta 之后");
    assertTrue(V("1.5.6.10") > V("1.5.6.9"), "数字段按数值比，不能按字符串比");
    assertTrue(V("1.5.6.1") > V("1.5.6"), "多一段数字算更新");
    assertTrue(V("v1.5.6") == V("1.5.6"), "tag 前面的 v 不算差异");
    assertTrue(!UpdateService.UpdateVersion.TryParse("sv-v0.1.0", out _), "不是版本号开头的 tag 要拒绝");
    assertTrue(!UpdateService.UpdateVersion.TryParse("", out _), "空字符串要拒绝");
}

static void testUpdateReleaseSelection()
{
    // 三条 release：别的产品（只有 exe 资源）、草稿、预发布 —— 都不该被选中。
    const string json = """
    [
      {"tag_name":"sv-v0.1.0","name":"OsuSv 0.1.0","draft":false,"prerelease":false,
       "published_at":"2026-11-09T12:38:25Z","html_url":"https://github.com/x/y/releases/tag/sv-v0.1.0",
       "assets":[{"name":"osu-sv-0.1.0-win-x64.exe","size":1,"browser_download_url":"https://example.com/sv.exe","digest":null}]},
      {"tag_name":"v1.5.6.2","name":"osu! Roam 1.5.6.2","draft":false,"prerelease":false,
       "published_at":"2026-11-10T00:00:00Z","html_url":"https://github.com/x/y/releases/tag/v1.5.6.2",
       "assets":[
         {"name":"osu-roam-1.5.6.2-en-portable.zip","size":100,"browser_download_url":"https://example.com/en.zip","digest":"sha256:AABB"},
         {"name":"osu-roam-1.5.6.2-portable.zip","size":200,"browser_download_url":"https://example.com/zh.zip","digest":null},
         {"name":"osu-roam-v1.5.6.2-source.zip","size":10,"browser_download_url":"https://example.com/src.zip","digest":null}]},
      {"tag_name":"v1.5.7","name":"草稿","draft":true,"prerelease":false,
       "published_at":"2026-11-11T00:00:00Z","html_url":"https://github.com/x/y/releases/tag/v1.5.7",
       "assets":[{"name":"osu-roam-1.5.7-portable.zip","size":1,"browser_download_url":"https://example.com/d.zip","digest":null}]},
      {"tag_name":"v1.5.8-beta","name":"预发布","draft":false,"prerelease":true,
       "published_at":"2026-11-12T00:00:00Z","html_url":"https://github.com/x/y/releases/tag/v1.5.8-beta",
       "assets":[{"name":"osu-roam-1.5.8-beta-portable.zip","size":1,"browser_download_url":"https://example.com/b.zip","digest":null}]}
    ]
    """;

    UpdateService.UpdateRelease? zh = UpdateService.ParseReleases(json, english: false);
    assertTrue(zh is not null, "应该挑出一个可用于更新的 release");
    assertText("v1.5.6.2", zh!.Tag, "选中的 release");
    assertText("osu-roam-1.5.6.2-portable.zip", zh.Asset.Name, "中文版应拿不带 -en 的那个包");
    assertTrue(zh.Asset.Size == 200, "包大小要读出来");
    assertTrue(zh.Asset.Sha256 is null, "没给 digest 时不该编一个校验值");

    UpdateService.UpdateRelease? en = UpdateService.ParseReleases(json, english: true);
    assertText("osu-roam-1.5.6.2-en-portable.zip", en!.Asset.Name, "英文版应拿 -en-portable 那个包");
    assertText("aabb", en.Asset.Sha256!, "digest 要转小写并去掉 sha256: 前缀");

    // 只有别的产品（比如 OsuSv）的 release 时，不能误判成「有更新」。
    const string onlyOther = """
    [{"tag_name":"sv-v0.1.0","name":"OsuSv","draft":false,"prerelease":false,"published_at":"2026-11-09T12:38:25Z",
      "html_url":"https://github.com/x/y/releases/tag/sv-v0.1.0",
      "assets":[{"name":"osu-sv-0.1.0-win-x64.exe","size":1,"browser_download_url":"https://example.com/sv.exe","digest":null}]}]
    """;
    assertTrue(UpdateService.ParseReleases(onlyOther, english: false) is null, "没有便携包的 release 不算更新");
}

static void testUpdatePrepareStagesPackage()
{
    string temp = Path.Combine(Path.GetTempPath(), $"roam-update-test-{Guid.NewGuid():N}");
    string package = Path.Combine(temp, "package");
    string work = Path.Combine(temp, "work");
    Directory.CreateDirectory(Path.Combine(package, "Assets"));
    File.WriteAllText(Path.Combine(package, "OsuRoam.exe"), "new exe");
    File.WriteAllText(Path.Combine(package, "Assets", "marker.txt"), "new asset");

    string zip = Path.Combine(temp, "update.zip");
    try
    {
        ZipFile.CreateFromDirectory(package, zip);
        UpdateService.UpdatePlan plan = UpdateService.Prepare(zip, temp, work);

        assertTrue(File.Exists(Path.Combine(plan.StagedRoot, "OsuRoam.exe")), "暂存区里应有 exe");
        assertTrue(File.Exists(Path.Combine(plan.StagedRoot, "Assets", "marker.txt")), "资源也要解出来");

        byte[] script = File.ReadAllBytes(plan.Script);
        assertTrue(script.All(value => value < 128), "更新脚本必须是纯 ASCII");
        string text = Encoding.ASCII.GetString(script);
        assertTrue(text.Contains("\r\n", StringComparison.Ordinal), "更新脚本必须是 CRLF");
        assertTrue(text.Contains("ROAM_UPDATE_PID", StringComparison.Ordinal), "脚本要从环境变量取参数");
        assertTrue(text.Contains("robocopy", StringComparison.Ordinal), "脚本要用 robocopy 覆盖");

        // 带一层顶层目录的包也要能找到 exe 那一层。
        string wrapped = Path.Combine(temp, "wrapped");
        string inner = Path.Combine(wrapped, "osu-roam-1.5.6.2");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "OsuRoam.exe"), "wrapped exe");
        string wrappedZip = Path.Combine(temp, "wrapped.zip");
        ZipFile.CreateFromDirectory(wrapped, wrappedZip);
        UpdateService.UpdatePlan nested = UpdateService.Prepare(wrappedZip, temp, Path.Combine(temp, "work2"));
        assertTrue(File.Exists(Path.Combine(nested.StagedRoot, "OsuRoam.exe")), "带顶层目录的包也要定位到 exe");

        // 没有 exe 的包要明确报错，别写脚本。
        string bad = Path.Combine(temp, "bad");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "readme.txt"), "no exe here");
        string badZip = Path.Combine(temp, "bad.zip");
        ZipFile.CreateFromDirectory(bad, badZip);
        bool threw = false;
        try
        {
            UpdateService.Prepare(badZip, temp, Path.Combine(temp, "work3"));
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        assertTrue(threw, "包里没有 OsuRoam.exe 时必须报错");
    }
    finally
    {
        try { Directory.Delete(temp, true); } catch (IOException) { }
    }
}

static void testManiaStarRatingHandlesHolds()
{
    string withHolds = syntheticManiaChart(keys: 4, bpm: 180, notes: 400, everyNthIsHold: 4);
    (double Star, double RiceStar)? pair = ManiaStarRating.ComputePair(withHolds);

    assertTrue(pair is not null, "合成面谱应能解析出星数");
    assertTrue(pair!.Value.Star > 0, "面谱星数应大于 0");
    assertTrue(pair.Value.RiceStar < pair.Value.Star,
        $"删掉长条后星数应下降：原 {pair.Value.Star:0.000} vs 米线 {pair.Value.RiceStar:0.000}");

    assertTrue(ManiaStarRating.Compute(withHolds.Replace("Mode: 3", "Mode: 0")) is null, "非 mania 模式不应给星数");
    assertTrue(ManiaStarRating.Compute("") is null, "空文本不应给星数");
}

static void testManiaStarRatingScalesWithDensity()
{
    double slow = ManiaStarRating.Compute(syntheticManiaChart(keys: 4, bpm: 150, notes: 500, everyNthIsHold: 0)) ?? 0;
    double fast = ManiaStarRating.Compute(syntheticManiaChart(keys: 4, bpm: 300, notes: 500, everyNthIsHold: 0)) ?? 0;

    assertTrue(slow > 0 && fast > slow, $"同样音符数下密度翻倍应加难度：{slow:0.000} → {fast:0.000}");
    assertTrue(fast < slow * 4, $"星数不应随密度线性膨胀：{slow:0.000} → {fast:0.000}");
}

/// <summary>匀速谱：四个量都该落在「稳」那一端。</summary>
static void testSvEvaluatorOnSteadyChartIsClean()
{
    SvMetrics? steady = Evaluator.Compute(syntheticSvChart(bpm: 180, seconds: 30, sv: null));
    assertTrue(steady is not null, "带时间点的合成谱应能算出 SV 指标");
    assertTrue(steady!.Irregularity < 0.05, $"匀速谱的等时性应该接近满格：{steady.Irregularity:0.000}");
    assertTrue(steady.Discomfort < 0.01, $"匀速谱不该有不适：{steady.Discomfort:0.000}");
    assertTrue(steady.Exaggeration < 0.05, $"匀速谱不该有夸张：{steady.Exaggeration:0.000}");
    assertTrue(steady.Memorization < 0.05, $"匀速谱不该背谱：{steady.Memorization:0.000}");
    assertTrue(steady.Isochrony > 0.95 && steady.Comfort > 0.99, "界面量：等时性/舒适度都应接近 1");

    assertTrue(Evaluator.Compute("") is null, "空文本不应给 SV 指标");
    assertTrue(Evaluator.Compute(syntheticManiaChart(keys: 4, bpm: 180, notes: 400, everyNthIsHold: 0)) is null,
        "没有 [TimingPoints] 的文本算不出 SV");
}

/// <summary>基线跳变：等时性掉下去、舒适度掉下去、背谱起来。</summary>
static void testSvEvaluatorPunishesBaselineJumps()
{
    // 每 4 秒在 1.0 和 2.0 之间来回跳：中位飞行时长成段翻倍，属于典型「跳变型 SV」。
    List<(double From, double To, double Sv)> sv = [];
    for (int index = 0; index < 8; index++)
        sv.Add((index * 4000, (index + 1) * 4000, index % 2 == 0 ? 1.0 : 2.0));

    SvMetrics? jumpy = Evaluator.Compute(syntheticSvChart(bpm: 180, seconds: 34, sv: sv));
    SvMetrics? steady = Evaluator.Compute(syntheticSvChart(bpm: 180, seconds: 34, sv: null));

    assertTrue(jumpy is not null && steady is not null, "两张合成谱都应算得出来");
    assertTrue(jumpy!.Irregularity > 0.3, $"来回跳变应判成结构不稳：{jumpy.Irregularity:0.000}");
    assertTrue(jumpy.SwitchesPerMinute > 2, $"跳变次数应被识别：{jumpy.SwitchesPerMinute:0.0}/分");
    assertTrue(jumpy.Discomfort > 0.3, $"跳变时屏幕上有音符，应扣不适：{jumpy.Discomfort:0.000}");
    assertTrue(jumpy.Comfort < steady!.Comfort, "跳变谱的舒适度必须低于匀速谱");
    assertTrue(jumpy.Memorization > steady.Memorization, "跳变谱的背谱度必须高于匀速谱");
    assertTrue(jumpy.Isochrony < 0.8, $"跳变谱的等时性不该是满的：{jumpy.Isochrony:0.000}");
}

/// <summary>夸张只在「飞行途中速度变了」时出现：整整段匀速换速不算夸张。</summary>
static void testSvEvaluatorExaggerationNeedsMidFlightChange()
{
    // 每拍前半拍 1.0、后半拍 2.0 —— 飞行时长 459ms，一张谱里几乎每个音符都会横跨一次变速。
    List<(double From, double To, double Sv)> sv = [];
    double beat = 60000.0 / 180.0;
    for (int index = 0; index < 120; index++)
    {
        double from = 1000 + index * beat;
        sv.Add((from, from + beat / 2, 1.0));
        sv.Add((from + beat / 2, from + beat, 2.0));
    }

    SvMetrics? chopped = Evaluator.Compute(syntheticSvChart(bpm: 180, seconds: 42, sv: sv));
    SvMetrics? steady = Evaluator.Compute(syntheticSvChart(bpm: 180, seconds: 42, sv: null));

    assertTrue(chopped is not null && steady is not null, "两张合成谱都应算得出来");
    assertTrue(chopped!.Exaggeration > 0.2, $"飞行途中反复变速应算夸张：{chopped.Exaggeration:0.000}");
    assertTrue(chopped.Exaggeration > steady!.Exaggeration * 4, "夸张度必须明显高于匀速谱");
    assertTrue(chopped.Intensity > 0.2, $"界面强度应被抬起来：{chopped.Intensity:0.000}");
}

/// <summary>
/// 造一张带时间点的 4K mania 谱：固定 1/4 拍距，SV 由 <paramref name="sv"/> 指定的区间逐段给出
/// （null = 全程 1.0）。区间以外的部分沿用最后一段的值。
/// </summary>
static string syntheticSvChart(double bpm, double seconds, IReadOnlyList<(double From, double To, double Sv)>? sv)
{
    StringBuilder builder = new();
    double beatLength = 60000.0 / bpm;
    builder.AppendLine("osu file format v14");
    builder.AppendLine("[General]");
    builder.AppendLine("Mode: 3");
    builder.AppendLine("[Difficulty]");
    builder.AppendLine("CircleSize:4");
    builder.AppendLine("[TimingPoints]");
    builder.AppendLine($"0,{beatLength.ToString("0.######", CultureInfo.InvariantCulture)},4,2,0,60,1,0");
    if (sv is not null)
    {
        foreach ((double from, _, double value) in sv)
        {
            string line = $"{from.ToString("0.###", CultureInfo.InvariantCulture)},{-100.0 / Math.Clamp(value, 0.01, 10.0)}" +
                          ",4,2,0,60,0,0";
            builder.AppendLine(line);
        }
    }

    builder.AppendLine("[HitObjects]");
    double step = beatLength / 4.0;
    int notes = (int)(seconds * 1000 / step);
    for (int index = 0; index < notes; index++)
    {
        int column = index % 4;
        int x = (int)((column + 0.5) * 512 / 4);
        int time = (int)(index * step);
        builder.AppendLine($"{x},192,{time},1,0,0:0:0:0:");
    }

    return builder.ToString();
}

/// <summary>造一张可解析的 4K mania 谱：固定 1/4 拍距，列循环，每隔 N 个出一个长条。</summary>
static string syntheticManiaChart(int keys, double bpm, int notes, int everyNthIsHold)
{
    StringBuilder builder = new();
    builder.AppendLine("osu file format v14");
    builder.AppendLine("[General]");
    builder.AppendLine("Mode: 3");
    builder.AppendLine("[Difficulty]");
    builder.AppendLine($"CircleSize:{keys}");
    builder.AppendLine("[HitObjects]");

    double step = 60000.0 / bpm / 4.0;
    for (int index = 0; index < notes; index++)
    {
        int column = index % keys;
        int x = (int)((column + 0.5) * 512 / keys);
        int time = (int)(1000 + index * step);
        bool hold = everyNthIsHold > 0 && index % everyNthIsHold == 0;
        builder.AppendLine(hold
            ? $"{x},192,{time},128,0,{time + (int)(step * 2)}:0:0:0:0:"
            : $"{x},192,{time},1,0,0:0:0:0:");
    }

    return builder.ToString();
}

static void testSearchStatusFollowsWhitelist()
{
    AppSettings settings = recommendationSettings();
    settings.IncludedStatuses = ["ranked", "loved"];
    assertTrue(OsuApiClient.StatusQuery(settings) == "ranked", "勾了 ranked+loved 时应按 ranked 查（loved 靠本地过滤）");

    settings.IncludedStatuses = ["loved"];
    assertTrue(OsuApiClient.StatusQuery(settings) == "loved", "只勾 loved 就查 loved");

    settings.IncludedStatuses = [];
    assertTrue(OsuApiClient.StatusQuery(settings) == "any", "没勾状态时不限制");

    settings.IncludedStatuses = ["ranked", "graveyard"];
    assertTrue(OsuApiClient.StatusQuery(settings) == "ranked", "含 ranked 时优先 ranked，别把 graveyard 引进页面");
}

static void testReconcileRefreshesFallbackFlagOnReusedCard()
{
    // 卡片实例被复用（保留封面/详情）时，补位标记必须跟着这次排序刷新，
    // 否则一张卡从严格变成补位，界面上还挂着旧状态。
    RecommendationItem strictCard = poolCard(3201);
    RecommendationItem fallbackCard = new()
    {
        BeatmapsetId = 3202, BeatmapId = 32021, Artist = "a", Title = "fallback", DifficultyName = "4K",
        Mapper = "m", Stars = 3, Bpm = 180, LengthSeconds = 120, RankedYear = 2024, Score = 1, IsFallback = true
    };
    List<RecommendationItem> pool = [strictCard, fallbackCard];

    // 新排序：3202 变成严格，3201 变成补位（desired 里都是全新实例）。
    RecommendationItem desiredStrict = poolCard(3202);
    RecommendationItem desiredFallback = new()
    {
        BeatmapsetId = 3201, BeatmapId = 32011, Artist = "a", Title = "now-fallback", DifficultyName = "4K",
        Mapper = "m", Stars = 3, Bpm = 180, LengthSeconds = 120, RankedYear = 2024, Score = 1, IsFallback = true
    };
    List<RecommendationItem> next = PoolReconciler.Build(
        pool, [desiredStrict, desiredFallback], new HashSet<int>(), 5);

    assertTrue(next.Any(item => item.BeatmapsetId == 3202 && !item.IsFallback), "3202 这次是严格，标记要清掉");
    assertTrue(next.Any(item => item.BeatmapsetId == 3201 && item.IsFallback), "3201 这次是补位，标记要打上");
    assertTrue(ReferenceEquals(next[0], fallbackCard), "复用原实例（封面等不重载）");
}

/// <summary>
/// 谱面预览只认 mania 的圈和长条：列号按 floor(x * keys / 512) 换算，
/// 长条读第 6 段的 endTime，滑条/转盘（转换谱面的残留）要丢掉。
/// </summary>
static void testPreviewChartParsesColumnsAndHolds()
{
    const string osu = """
    osu file format v14

    [General]
    Mode:3

    [Difficulty]
    CircleSize:4
    OverallDifficulty:8

    [HitObjects]
    64,192,1000,1,0,0:0:0:0:
    192,192,2000,128,0,2600:0:0:0:0:
    448,192,4000,1,0,0:0:0:0:
    511,192,5000,1,0,0:0:0:0:
    100,192,6000,2,0,B|200:200|300:300,1,50
    """;

    ManiaPreviewChart? chart = ManiaPreviewChart.Parse(osu);
    if (chart is null)
        throw new InvalidOperationException("4K mania 谱面应能解析出预览物件");

    assertEqual(4, chart.KeyCount, "键数取 Difficulty.CircleSize");
    assertEqual(4, chart.Notes.Count, "滑条/转盘不算物件");
    assertEqual(0, chart.Notes[0].Column, "x=64 → 第 0 列");
    assertEqual(1, chart.Notes[1].Column, "x=192 → 第 1 列");
    assertEqual(3, chart.Notes[2].Column, "x=448 → 第 3 列");
    assertEqual(3, chart.Notes[3].Column, "x=511 会算到 4，要夹到最后一列");
    assertTrue(chart.Notes[1].IsHold, "type 128 是长条");
    assertNear(2600, chart.Notes[1].End, "长条结束时间取第 6 段的 endTime");
    assertNear(600, chart.Notes[1].Length, "长条长度 = endTime - start");
    assertNear(1000, chart.FirstNoteTime, "第一个物件时间");
    assertNear(5000, chart.LastNoteTime, "最后一个物件结束时间");
    assertNear(600, chart.LongestHold, "最长的长条（二分查找要往前让出的时间）");

    // 列换算本身也单独钉一下：0/512 的边界最容易算错一列。
    assertEqual(1, ManiaPreviewChart.ColumnOf(255, 4), "255*4/512 = 1.99 → 第 1 列");
    assertEqual(2, ManiaPreviewChart.ColumnOf(256, 4), "256*4/512 = 2 → 第 2 列");
    assertEqual(6, ManiaPreviewChart.ColumnOf(512, 7), "x 超界时夹到最后一列");
}

/// <summary>预览是 mania 专属的：别的模式宁可不显示，也不要把 std 的滑条画成方块。</summary>
static void testPreviewChartRejectsOtherModes()
{
    const string standard = """
    osu file format v14

    [General]
    Mode:0

    [Difficulty]
    CircleSize:4

    [HitObjects]
    64,192,1000,1,0,0:0:0:0:
    """;

    if (ManiaPreviewChart.Parse(standard) is not null)
        throw new InvalidOperationException("osu!standard 谱面不该解析出预览物件");

    const string empty = """
    osu file format v14

    [General]
    Mode:3

    [Difficulty]
    CircleSize:4
    """;

    if (ManiaPreviewChart.Parse(empty) is not null)
        throw new InvalidOperationException("没有物件的 mania 谱面不该出预览");
    if (ManiaPreviewChart.Parse(null) is not null)
        throw new InvalidOperationException("空文本不该出预览");
}

/// <summary>
/// 二分查找用「当前时间 - 最长长条」去查，保证还在屏幕上的长条不会被跳过，
/// 同时又不会从头遍历整张谱（两万物件的话每帧都会卡）。
/// </summary>
static void testPreviewChartFindsVisibleRange()
{
    const string osu = """
    osu file format v14

    [General]
    Mode:3

    [Difficulty]
    CircleSize:4

    [HitObjects]
    64,192,1000,1,0,0:0:0:0:
    192,192,2000,128,0,4200:0:0:0:0:
    448,192,4000,1,0,0:0:0:0:
    511,192,8000,1,0,0:0:0:0:
    """;

    ManiaPreviewChart? chart = ManiaPreviewChart.Parse(osu);
    if (chart is null)
        throw new InvalidOperationException("测试谱面应能解析");

    assertEqual(0, chart.FirstIndexAt(0), "还没开始：从第一个物件画起");
    assertEqual(0, chart.FirstIndexAt(1000), "正好落在第一个物件上，要包含它");
    assertEqual(1, chart.FirstIndexAt(1001), "过了第一个物件就从第二个开始");
    assertEqual(3, chart.FirstIndexAt(5000), "跳过已经不露头的物件");
    assertEqual(4, chart.FirstIndexAt(9000), "全都在后面：返回物件数");
    // 当前 4000、最长长条 2200：起点 1800 的长条还在画面上，不能被二分跳过。
    assertEqual(1, chart.FirstIndexAt(4000 - chart.LongestHold - 1), "长条不能因为起点早被跳过");
}

/// <summary>
/// 预览时钟：tosu 每 ~100ms 报一次，本地每 16ms 推进一帧。
/// 一秒下来本地时间要跟得上（差不到两帧），且不能因为纠偏往回缩。
/// </summary>
static void testPreviewMotionCommitContinuity()
{
    PreviewMotion motion = new();
    motion.Update(0, 10000, 1, 200, true);
    double previousNow = 0;
    double expectedPosition = motion.Position;
    double expectedVelocity = motion.Velocity;
    for (int i = 1; i <= 100; i++)
    {
        double delta = i % 3 == 0 ? 0.073 : 0.049;
        double t = Math.Min(delta, motion.Blend);
        expectedPosition = motion.Position + motion.Velocity * t + motion.Acceleration * t * t
            + (motion.Velocity + 2 * motion.Acceleration * t) * (delta - t);
        expectedVelocity = motion.Velocity + 2 * motion.Acceleration * t;
        previousNow += delta;
        motion.Update(previousNow, 10000 + previousNow * 1000 + (i % 2 == 0 ? 12 : -12), 1, 200, false);
        assertTrue(Math.Abs(motion.Position - expectedPosition) < 0.00001,
            "Animation replacement must not jump position when samples jitter");
        assertTrue(Math.Abs(motion.Velocity - expectedVelocity) < 0.00001,
            "Animation replacement must preserve velocity across irregular commits");
    }
    motion.Update(previousNow + 1, 500, 1, 200, true);
    assertTrue(motion.Position == 500, "An explicit seek must reanchor the animation");
    motion.Update(previousNow + 2, 500, 0, 0, false);
    double stopped = motion.Position;
    motion.Update(previousNow + 4, 500, 0, 0, false);
    assertTrue(motion.Position == stopped, "An expired animation must remain stopped");
}

static void testPreviewClockFollowsSource()
{
    PreviewClock clock = new();
    clock.Sync(10000, 0);
    assertNear(10000, clock.Time, "第一条直接对齐");

    double now = 0;
    double reported = 10000;
    for (int frame = 1; frame <= 62; frame++)
    {
        now += 16;
        if (frame % 6 == 0)
        {
            reported += 96;
            clock.Sync(reported, now);
        }
        clock.Advance(now, 16);
    }

    // 62 帧 ≈ 992ms：理想位置 = 10000 + 10×96 + 32 = 10992
    assertTrue(Math.Abs(clock.Time - 10992) < 40,
        $"一秒后本地时间应跟上预览位置，实际 {clock.Time:0}");
}

/// <summary>预览音频暂停 / tosu 断流：画面必须停在原地，不能自己往前跑。</summary>
static void testPreviewClockFreezesWhenSourceStops()
{
    PreviewClock clock = new();
    clock.Sync(5000, 0);
    double now = 0;
    // 先跑过「停更判定」的门槛（PreviewClock.StallThreshold），再取样；
    // 这段本来还该往前走，所以用 60 帧（≈960ms）确保已经越界。
    for (int frame = 0; frame < 60; frame++)
    {
        now += 16;
        clock.Advance(now, 16);
    }

    double frozen = clock.Time;
    for (int frame = 0; frame < 60; frame++)
    {
        now += 16;
        clock.Advance(now, 16);
    }

    assertTrue(Math.Abs(clock.Time - frozen) < 0.001, $"停更后不该再前进，从 {frozen:0} 变成 {clock.Time:0}");
    assertTrue(clock.Time < 5000 + PreviewClock.StallThreshold + 40,
        $"最多领先一个停更阈值，实际 {clock.Time:0}");
}

/// <summary>换歌 / 玩家把预览拖回去：差异大的时候直接对齐，别慢慢磨几秒。</summary>
/// <summary>
/// 采样抖动 / 样本迟到时，画面绝不能倒退 —— 那是肉眼能看到的「倒回去一点点」。
/// 只有源真的跳回去一大截（预览重播）才允许倒退。
/// </summary>
static void testPreviewClockNeverGoesBackwards()
{
    PreviewClock clock = new();
    clock.Sync(50000, 0);
    for (int i = 1; i <= 20; i++)
        clock.Advance(i * 16, 16);

    double before = clock.Time;
    // 一条「迟到」的样本：比预测值落后 60ms（小于 SnapThreshold，但足够触发旧逻辑的回拉）。
    clock.Sync(before - 60, 320);
    clock.Advance(336, 16);
    assertTrue(clock.Time >= before, $"样本迟到时不能倒退：{before:0.0} → {clock.Time:0.0}");

    // 源跳回去一大截（预览音频重播）：这条允许倒退。
    clock.Sync(1000, 400);
    clock.Advance(416, 16);
    assertTrue(Math.Abs(clock.Time - 1000) < 60, $"明显回跳要直接对齐，实际 {clock.Time:0.0}");
}

static void testPreviewClockCadenceIndependent()
{
    static double run(int step)
    {
        PreviewClock clock = new();
        clock.Sync(10000, 0);
        for (int now = step; now <= 2000; now += step)
        {
            if (now % 100 == 0)
                clock.Sync(10000 + now - 25, now);
            clock.Advance(now, step);
        }
        return clock.Time;
    }
    assertTrue(Math.Abs(run(4) - run(50)) < 3,
        "240Hz rendering and 20Hz maintenance must follow the same audio timeline");
}

static void testPreviewClockRepeatedSamplesStop()
{
    PreviewClock clock = new();
    clock.Sync(5000, 0);
    for (int now = 50; now <= 2000; now += 50)
    {
        clock.Sync(5000, now);
        clock.Advance(now, 50);
    }
    assertNear(5000 + PreviewClock.StallThreshold, clock.Time, "repeated paused samples cannot extend extrapolation");
    assertNear(0, clock.Rate, "paused animation must stop");
    clock.Sync(double.PositiveInfinity, 2050);
    assertTrue(double.IsFinite(clock.Time), "invalid samples cannot poison the timeline");
}

static void testPreviewClockDelayedWakeStopsAtDeadline()
{
    PreviewClock clock = new();
    clock.Sync(7000, 0);
    clock.Advance(1000, 1000);
    assertNear(7000 + PreviewClock.StallThreshold, clock.Time,
        "late worker wake cannot move past the animation deadline");
    assertNear(0, clock.PredictionRemaining(1000), "expired prediction window");
}

static void testPreviewClockSnapsOnJump()
{
    PreviewClock clock = new();
    clock.Sync(20000, 0);
    clock.Advance(16, 16);
    assertTrue(clock.Time > 19990, "对齐后开始走");

    clock.Sync(12000, 100);
    clock.Advance(100, 16);
    assertTrue(Math.Abs(clock.Time - 12000) < 40, $"跳变要直接对齐，实际 {clock.Time:0}");

    // 换歌：Reset 之后的第一条必须精确落到该去的位置，不能从上一首滑过去。
    clock.Reset();
    clock.Sync(3000, 500);
    assertNear(3000, clock.Time, "Reset 后第一条直接落到目标位置");
}

/// <summary>
/// 预览窗的时钟来自 tosu：菜单段（gosumemory 兼容）优先，没有就退回 v2 的
/// <c>beatmap.time.live</c>。两个都读不到时是 0，窗口会拿它判断「要不要提示没时间」。
/// </summary>
static void testTosuSnapshotCarriesPreviewTime()
{
    const string withMenu = """
    {"state":{"name":"selectPlay"},
     "menu":{"state":5,"bm":{"time":{"firstObj":11136,"current":80295,"full":278136,"mp3":281353}}},
     "beatmap":{"id":5397245,"set":2322139,"time":{"live":80295,"firstObject":11136,"lastObject":278136,"mp3Length":281353}},
     "profile":{"name":"tester"},"game":{"paused":false},"play":{"failed":false},
     "resultsScreen":{"rank":"","score":0}}
    """;

    TosuSnapshot? captured = null;
    TosuClient client = new();
    client.SnapshotReceived += value => captured = value;
    client.ProcessSnapshotJson(withMenu);
    if (captured is null)
        throw new InvalidOperationException("payload 没解析出快照");
    assertNear(80295, captured.PreviewTime, "menu.bm.time.current 优先");

    const string withoutMenu = """
    {"state":{"name":"selectPlay"},
     "beatmap":{"id":5397245,"set":2322139,"time":{"live":41234,"firstObject":2380,"lastObject":143811,"mp3Length":146235}},
     "profile":{"name":"tester"},"game":{"paused":false},"play":{"failed":false},
     "resultsScreen":{"rank":"","score":0}}
    """;

    captured = null;
    client.ProcessSnapshotJson(withoutMenu);
    if (captured is null)
        throw new InvalidOperationException("payload 没解析出快照");
    assertNear(41234, captured.PreviewTime, "没有 menu 段时退回 beatmap.time.live");
}

