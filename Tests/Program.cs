using OsuRoam.Services;
using OsuRoam.Models;

testSuccessfulCompletion();
testAbortDoesNotComplete();
testFailureDoesNotComplete();
testPauseDoesNotStartOrComplete();
testDifficultyProfilesAreIndependent();
testUnlimitedDifficultyRange();
testManiaKeysAreFiltered();
testDuplicateBeatmapsetsAreCollapsed();
testRecommendationHistoryPrefersFreshCandidates();
testRecommendationHistoryIsNormalizedAndBounded();
testDirtyTagsAreFiltered();
testCompoundTagsRemainTogether();
testCommonCompoundTagsUseLongestMatches();
testSimpleFiltersOnlyStrictAreAbsolute();
testSimpleFilterFitIsWeighted();
testStrictSimpleFiltersFilterCandidates();
testSimpleFilterSelectionStatesMatchSemantics();
testSimpleFilterLabelsAreChinese();
testSimpleFiltersUseApiGenreAndLanguageIds();
testTosuStringStateWorksInMultiplayerPayload();
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
static void testSimpleFilterLabelsAreChinese()
{
    if (new SimpleFilterOption("ranked", "Ranked").DisplayName != "\u4e0a\u67b6\u8c31\u9762" ||
        new SimpleFilterOption("all", "All").DisplayName != "\u5168\u90e8")
        throw new InvalidOperationException("简易筛选显示名必须使用中文");
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

static void assertEqual(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException($"{message}：expected={expected}, actual={actual}");
}

static void assertNear(double expected, double actual, string message)
{
    if (Math.Abs(expected - actual) > 0.0001)
        throw new InvalidOperationException($"{message}：expected={expected}, actual={actual}");
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
