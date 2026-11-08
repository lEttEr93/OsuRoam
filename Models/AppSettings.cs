using OsuRoam.Services;

namespace OsuRoam.Models;

public static class MapInfoSources
{
    public const string Imported = "import";
    public const string InGame = "ingame";
}

public sealed class AppSettings
{
    public const int MaximumRecommendationHistory = 500;
    public const double MinimumSelectableStars = 0.0;
    public const double MaximumFiniteSelectableStars = 11.0;
    public const double UnlimitedMaximumStars = 999.0;
    public const string DefaultDownloadMirrors = Flavor.DefaultDownloadMirrors;
    public static readonly IReadOnlyList<string> StatusFilterValues = [
        "ranked", "approved", "qualified", "loved", "pending", "wip", "graveyard"
    ];
    public static readonly IReadOnlyList<string> GenreFilterValues = [
        "unspecified", "video game", "anime", "rock", "pop", "other", "novelty", "hip hop", "electronic", "metal", "classical", "folk", "jazz"
    ];
    public static readonly IReadOnlyList<string> LanguageFilterValues = [
        "unspecified", "english", "japanese", "chinese", "instrumental", "korean", "french", "german", "swedish", "spanish", "italian", "polish", "russian", "other"
    ];
    private static readonly IReadOnlyDictionary<int, string> GenreNames = new Dictionary<int, string>
    {
        [1] = "unspecified", [2] = "video game", [3] = "anime", [4] = "rock", [5] = "pop", [6] = "other",
        [7] = "novelty", [9] = "hip hop", [10] = "electronic", [11] = "metal", [12] = "classical",
        [13] = "folk", [14] = "jazz"
    };
    private static readonly IReadOnlyDictionary<int, string> LanguageNames = new Dictionary<int, string>
    {
        [1] = "unspecified", [2] = "english", [3] = "japanese", [4] = "chinese", [5] = "instrumental",
        [6] = "korean", [7] = "french", [8] = "german", [9] = "swedish", [10] = "spanish",
        [11] = "italian", [12] = "polish", [13] = "russian", [14] = "other"
    };
    private static readonly string[] DefaultStatusFilters = ["ranked", "approved", "qualified", "loved"];
    /// <summary>爬到的、不在白名单里的 tag 最多留这么多，避免设置文件无限膨胀。</summary>
    private const int MaxSeenTags = 2000;
    // osu! exposes beatmapset tags as one space-separated string, so the API
    // does not preserve the boundary in common multi-word tags. Keep this
    // list deliberately curated and match longer phrases before shorter ones.
    private static readonly string[] CommonCompoundTags = [
        "alternative rock", "art rock", "black metal", "classical music", "dance music",
        "death metal", "deep house", "drum and bass", "electronic music", "female vocal",
        "film score", "future bass", "future funk", "game music", "gothic rock",
        "happy hardcore", "hard rock", "hardcore techno", "heavy metal", "hip hop",
        "indie rock", "japanese folk", "japanese pop", "japanese vocal", "jazz fusion",
        "jazz funk", "liquid drum and bass", "liquid funk", "male vocal", "melodic dubstep",
        "nu jazz", "nu metal", "original soundtrack", "pop rock", "post metal", "post rock",
        "power metal", "progressive house", "progressive rock", "progressive trance",
        "rhythm game", "speed practice", "stream practice", "tech house", "tech map",
        "trance music", "video game", "video game music", "vocaloid original"
    ];
    private static readonly string[] OrderedCompoundTags = CommonCompoundTags
        .OrderByDescending(getWordCount)
        .ThenByDescending(tag => tag.Length)
        .ToArray();

    public string Username { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ProtectedClientSecret { get; set; } = string.Empty;
    public string Mode { get; set; } = "osu";
    public int ManiaKeys { get; set; } = 4;
    public bool AutomaticDifficulty { get; set; } = true;
    public double MinimumStars { get; set; } = 4.0;
    public double MaximumStars { get; set; } = 5.5;
    public Dictionary<string, DifficultyRangeSettings> DifficultyRanges { get; set; } = CreateDefaultDifficultyRanges();
    public int MinimumYear { get; set; } = 2007;
    public int MaximumYear { get; set; } = DateTime.UtcNow.Year;
    public int MinimumLengthSeconds { get; set; } = 45;
    public int MaximumLengthSeconds { get; set; } = 360;
    public bool IncludeLoved { get; set; } = true;
    /// <summary>界面用内置的 Outfit 字体（见 Fonts\ATTRIBUTION.md）。关掉则回退系统字体。</summary>
    public bool UseEmbeddedFont { get; set; } = true;
    /// <summary>界面语言：auto（跟随系统）/ zh / en，见 Services\Loc.cs。</summary>
    public string Language { get; set; } = Flavor.DefaultLanguage;
    /// <summary>导入谱面后自动弹出谱面信息窗。</summary>
    public bool ShowMapInfo { get; set; } = true;
    /// <summary>信息栏显示哪张谱面：import = 本次导入的（默认），ingame = osu! 里当前选中的。</summary>
    public string MapInfoSource { get; set; } = MapInfoSources.Imported;
    /// <summary>选歌界面里再开一个谱面预览窗，跟着游戏里的预览音频滚 mania 谱面。</summary>
    public bool ShowPreview { get; set; } = true;
    /// <summary>预览流速：一屏覆盖多少秒谱面（越小滚得越快）。目标是与玩家游戏内流速一致。</summary>
    public double PreviewScrollSeconds { get; set; } = 1.25;
    /// <summary>预览延迟补偿（毫秒）：预览要追上游戏画面，往未来多看一点。</summary>
    public double PreviewLatencyMilliseconds { get; set; } = 35;
    public bool ExcludeRecentlyPlayed { get; set; } = true;
    /// <summary>扫描 osu! 谱面目录，已经装在机器上的难度不再推荐（同曲其他难度仍可推荐）。</summary>
    public bool ExcludeInstalledBeatmaps { get; set; } = true;
    public double Exploration { get; set; } = 0.45;
    /// <summary>键型偏好在排序里的权重（0 = 不参与）。</summary>
    public double PatternWeight { get; set; } = 0.12;
    public int PoolSize { get; set; } = 5;
    public int ResultDelaySeconds { get; set; } = 5;
    public bool AutoDownload { get; set; } = true;
    public bool AutoImport { get; set; } = true;
    public string DownloadUrlTemplate { get; set; } = Flavor.DefaultDownloadTemplate;
    public string DownloadMirrorTemplates { get; set; } = DefaultDownloadMirrors;
    public string OsuExecutablePath { get; set; } = string.Empty;
    /// <summary>谱面缓存上限（MB），只裁剪 Beatmaps 里的 .osz，其他数据不动。</summary>
    public int CacheLimitMegabytes { get; set; } = 1024;
    /// <summary>
    /// 客户端：<c>auto</c> / <c>lazer</c> / <c>stable</c>。默认自动 —— 旧版本默认写死 lazer，
    /// 配置被清过（或只装了另一种客户端）就找不到可执行文件，导入直接没反应。
    /// </summary>
    public string OsuClient { get; set; } = "auto";
    public string TosuWebSocketUrl { get; set; } = "ws://127.0.0.1:24050/websocket/v2";
    public string TosuExecutablePath { get; set; } = string.Empty;
    public bool AutoStartTosu { get; set; } = true;
    public bool StopManagedTosuOnExit { get; set; } = true;
    public List<int> RecommendationHistory { get; set; } = [];
    /// <summary>
    /// 每次漫游递增，用于轮换候选谱面的搜索排序，避免每次都只看到
    /// 同样那 400 条结果（池子耗干后就会大量回收历史谱面）。
    /// </summary>
    public int SearchRotation { get; set; }
    public List<string> TagPool { get; set; } = [];
    /// <summary>
    /// 候选谱面里出现过、但不在白名单里的 tag（有上限、旧的先丢）。
    /// 它们仍然可以选，只是排在白名单之后 —— 这样池子不会被几万条歌名撑爆。
    /// </summary>
    public List<string> SeenTags { get; set; } = [];
    /// <summary>池子格式版本，2 = 白名单 + SeenTags。旧版本升级时用来做一次迁移。</summary>
    public int TagPoolVersion { get; set; }

    /// <summary>攒够数量后自动把待审 tag 提交到仓库（只发 tag 文本和次数）。</summary>
    public bool TagSubmitEnabled { get; set; } = true;

    /// <summary>上次提交时间，用来做冷却。</summary>
    public DateTimeOffset? LastTagSubmitUtc { get; set; }

    /// <summary>
    /// 用户显式勾选的键型（<see cref="ManiaPattern.CorePatterns"/> 的子集）。
    /// 非空时用它算 patternFit 并给强权重，取代「从成绩反推」的旧路径。
    /// </summary>
    public List<string> SelectedPatterns { get; set; } = [];

    /// <summary>只要面谱：LnRatio ≥ <see cref="ManiaPattern.LnChartThreshold"/> 的谱面。</summary>
    public bool PreferLn { get; set; }
    public List<string> IncludedTags { get; set; } = [];
    public List<string> ExcludedTags { get; set; } = [];
    public List<string> IncludedStatuses { get; set; } = [.. DefaultStatusFilters];
    public List<string> IncludedGenres { get; set; } = [];
    public List<string> IncludedLanguages { get; set; } = [];
    public List<string> StrictStatuses { get; set; } = [];
    public List<string> StrictGenres { get; set; } = [];
    public List<string> StrictLanguages { get; set; } = [];
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;

    public string DifficultyKey => GetDifficultyKey(Mode, ManiaKeys);

    public DifficultyRangeSettings GetDifficultyRange()
    {
        if (!DifficultyRanges.TryGetValue(DifficultyKey, out DifficultyRangeSettings? range))
        {
            range = new DifficultyRangeSettings();
            DifficultyRanges[DifficultyKey] = range;
        }
        return range;
    }

    public static bool IsUnlimitedMaximumStars(double maximumStars) => maximumStars >= UnlimitedMaximumStars;

    public static void NormalizeDifficultyRange(DifficultyRangeSettings range)
    {
        range.MinimumStars = Math.Clamp(range.MinimumStars, MinimumSelectableStars, MaximumFiniteSelectableStars);
        range.MaximumStars = range.MaximumStars > MaximumFiniteSelectableStars
            ? UnlimitedMaximumStars
            : Math.Clamp(range.MaximumStars, range.MinimumStars, MaximumFiniteSelectableStars);
    }

    public void NormalizeDifficultyRanges()
    {
        DifficultyRanges ??= CreateDefaultDifficultyRanges();
        foreach (DifficultyRangeSettings range in DifficultyRanges.Values)
            NormalizeDifficultyRange(range);

        MinimumStars = Math.Clamp(MinimumStars, MinimumSelectableStars, MaximumFiniteSelectableStars);
        MaximumStars = MaximumStars > MaximumFiniteSelectableStars
            ? UnlimitedMaximumStars
            : Math.Clamp(MaximumStars, MinimumStars, MaximumFiniteSelectableStars);
    }

    public void NormalizeTags()
    {
        NormalizePatternSelection();
        if (TagPoolVersion < TagPoolFormat)
        {
            migrateLegacyTagPool();
            TagPoolVersion = TagPoolFormat;
        }

        SeenTags ??= [];
        if (SeenTags.Count > MaxSeenTags)
            SeenTags = SeenTags.Skip(SeenTags.Count - MaxSeenTags).ToList();

        IncludedTags = normalizeTags(IncludedTags);
        ExcludedTags = normalizeTags(ExcludedTags);
        ExcludedTags.RemoveAll(tag => IncludedTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        // 池子 = 白名单（按审计顺序）+ 见过的 + 已选的，同一条只出现一次。
        TagPool = normalizeTags(TagCatalog.Pool.Concat(SeenTags).Concat(IncludedTags).Concat(ExcludedTags))
            .OrderBy(TagCatalog.Rank)
            .ThenBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>池子格式版本：2 起 = 白名单 + SeenTags。</summary>
    public const int TagPoolFormat = 2;

    /// <summary>把勾选的键型收敛成核心型的规范写法，丢掉不认识的条目。</summary>
    public void NormalizePatternSelection()
    {
        SelectedPatterns ??= [];
        List<string> selected = [];
        foreach (string name in SelectedPatterns)
        {
            string? canonical = ManiaPattern.CorePatterns
                .FirstOrDefault(core => string.Equals(core, name?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (canonical is not null && !selected.Contains(canonical, StringComparer.Ordinal))
                selected.Add(canonical);
        }
        SelectedPatterns = selected;
    }

    /// <summary>
    /// 旧版本的池子是从每个候选谱面里无条件攒出来的（实测能到 1.6 万条，绝大多数是歌名和符号）。
    /// 升级时把白名单以外、看起来还像 tag 的挪进 SeenTags，池子本身重置。
    /// </summary>
    private void migrateLegacyTagPool()
    {
        if (TagPool is { Count: > 0 })
        {
            foreach (string tag in TagPool)
            {
                string canonical = TagCatalog.Canonical(tag);
                if (!IsMeaningfulTag(canonical) || TagCatalog.Contains(canonical))
                    continue;
                if (!SeenTags.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                    SeenTags.Add(canonical);
            }
        }

        TagPool = [];
    }

    public void NormalizeSimpleFilters()
    {
        IncludedStatuses = normalizeFilterValues(IncludedStatuses, StatusFilterValues, DefaultStatusFilters);
        IncludedGenres = normalizeFilterValues(IncludedGenres, GenreFilterValues, []);
        IncludedLanguages = normalizeFilterValues(IncludedLanguages, LanguageFilterValues, []);
        StrictStatuses = normalizeFilterValues(StrictStatuses, StatusFilterValues, []);
        StrictGenres = normalizeFilterValues(StrictGenres, GenreFilterValues, []);
        StrictLanguages = normalizeFilterValues(StrictLanguages, LanguageFilterValues, []);
        StrictStatuses = StrictStatuses.Intersect(IncludedStatuses, StringComparer.OrdinalIgnoreCase).ToList();
        StrictGenres = StrictGenres.Intersect(IncludedGenres, StringComparer.OrdinalIgnoreCase).ToList();
        StrictLanguages = StrictLanguages.Intersect(IncludedLanguages, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool MatchesStrictSimpleFilters(OsuBeatmapset set)
    {
        string status = normalizeFilterValue(set.Status);
        string genre = genreValue(set);
        string language = languageValue(set);
        return matchesFilter(StrictStatuses, status) && matchesFilter(StrictGenres, genre) && matchesFilter(StrictLanguages, language);
    }

    public int StrictSimpleFilterMatchCount(OsuBeatmapset set)
    {
        string status = normalizeFilterValue(set.Status);
        string genre = genreValue(set);
        string language = languageValue(set);
        int matches = 0;
        if (StrictStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) matches++;
        if (StrictGenres.Contains(genre, StringComparer.OrdinalIgnoreCase)) matches++;
        if (StrictLanguages.Contains(language, StringComparer.OrdinalIgnoreCase)) matches++;
        return matches;
    }

    public bool MatchesSimpleFilters(OsuBeatmapset set)
    {
        return MatchesStrictSimpleFilters(set);
    }

    public double SimpleFilterFit(OsuBeatmapset set)
    {
        string status = normalizeFilterValue(set.Status);
        string genre = genreValue(set);
        string language = languageValue(set);
        int activeFilters = 0;
        double matchedFilters = 0;

        IReadOnlyList<string> preferredStatuses = preferredValues(IncludedStatuses, StrictStatuses);
        IReadOnlyList<string> preferredGenres = preferredValues(IncludedGenres, StrictGenres);
        IReadOnlyList<string> preferredLanguages = preferredValues(IncludedLanguages, StrictLanguages);

        if (preferredStatuses.Count > 0)
        {
            activeFilters++;
            if (preferredStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
                matchedFilters++;
        }
        if (preferredGenres.Count > 0)
        {
            activeFilters++;
            if (preferredGenres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                matchedFilters++;
        }
        if (preferredLanguages.Count > 0)
        {
            activeFilters++;
            if (preferredLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
                matchedFilters++;
        }

        return activeFilters == 0 ? 0 : matchedFilters / activeFilters;
    }

    public double DefaultStatusPriority(OsuBeatmapset set)
    {
        if (preferredValues(IncludedStatuses, StrictStatuses).Count > 0)
            return 0;

        string status = normalizeFilterValue(set.Status);
        return status is "ranked" or "loved" ? 1 : 0;
    }

    private static bool matchesFilter(IReadOnlyCollection<string> selected, string value) =>
        selected.Count == 0 || selected.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> preferredValues(
        IReadOnlyCollection<string> included,
        IReadOnlyCollection<string> strict) =>
        included.Where(value => !strict.Contains(value, StringComparer.OrdinalIgnoreCase)).ToList();

    private static List<string> normalizeFilterValues(
        IEnumerable<string>? values,
        IReadOnlyCollection<string> allowed,
        IReadOnlyCollection<string> defaults)
    {
        if (values is null)
            return [.. defaults];

        return values
            .Select(value => normalizeFilterValue(value))
            .Where(value => allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string normalizeFilterValue(string? value, string fallback = "") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    private static string genreValue(OsuBeatmapset set) =>
        normalizeFilterValue(set.Genre?.Name ?? (set.GenreId is int id && GenreNames.TryGetValue(id, out string? name) ? name : null), "unspecified");

    private static string languageValue(OsuBeatmapset set) =>
        normalizeFilterValue(set.Language?.Name ?? (set.LanguageId is int id && LanguageNames.TryGetValue(id, out string? name) ? name : null), "unspecified");

    public void RecordTags(IEnumerable<string> tags)
    {
        SeenTags ??= [];
        List<string> normalized = normalizeTags(tags);
        foreach (string tag in normalized)
        {
            if (TagCatalog.Contains(tag))
                continue;
            if (!SeenTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                SeenTags.Add(tag);
        }

        // 白名单外的 tag 顺手进待审队列（本地文件，用户自己决定要不要提交）。
        TagIntake.Offer(normalized);
        NormalizeTags();
    }

    public static IReadOnlyList<string> ParseTags(string tags)
    {
        string normalized = (tags ?? string.Empty).Replace('_', ' ');
        string[] words = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        List<string> parsed = [];

        for (int index = 0; index < words.Length; index++)
        {
            string? compound = OrderedCompoundTags.FirstOrDefault(tag => matchesCompound(words, index, tag));
            if (compound is not null)
            {
                parsed.Add(compound);
                index += getWordCount(compound) - 1;
            }
            else
            {
                parsed.Add(words[index]);
            }
        }

        return normalizeTags(parsed);
    }

    private static bool matchesCompound(IReadOnlyList<string> words, int start, string compound)
    {
        string[] parts = compound.Split(' ');
        if (start + parts.Length > words.Count)
            return false;

        for (int offset = 0; offset < parts.Length; offset++)
        {
            if (!string.Equals(words[start + offset], parts[offset], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static int getWordCount(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static List<string> normalizeTags(IEnumerable<string>? tags) => (tags ?? [])
        .Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Select(tag => TagCatalog.Canonical(tag.Trim().ToLowerInvariant()))
        .Where(IsMeaningfulTag)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static bool IsMeaningfulTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        string value = tag.Trim();
        if (value.Length < 2 || value.Length > 40 || value.Contains('\uFFFD') || value.Contains('?'))
            return false;
        if (value.Any(char.IsControl))
            return false;
        // 谱面 tag 里混进来的排版垃圾：引号、括号、井号、路径、运算符。保留 - ! ' 三种（j-core、osu!、mappers' guild）。
        if (value.Any(character => character is '"' or '(' or ')' or '[' or ']' or '{' or '}' or '#'
                or '$' or '%' or '^' or '*' or '+' or '=' or '|' or '<' or '>' or '/' or '\\' or '~' or '@' or '`'))
            return false;
        if (value[0] is '-' or '.' or '_')
            return false;
        if (!value.Any(char.IsLetter))
            return false;

        int usefulCharacters = value.Count(char.IsLetterOrDigit);
        return usefulCharacters >= 2 && usefulCharacters * 2 >= value.Length;
    }
    public void NormalizeRecommendationHistory()
    {
        RecommendationHistory ??= [];
        HashSet<int> seen = [];
        RecommendationHistory = RecommendationHistory
            .AsEnumerable()
            .Reverse()
            .Where(beatmapsetId => beatmapsetId > 0 && seen.Add(beatmapsetId))
            .Take(MaximumRecommendationHistory)
            .Reverse()
            .ToList();
    }

    public void RecordRecommendation(int beatmapsetId)
    {
        if (beatmapsetId <= 0)
            return;

        RecommendationHistory ??= [];
        RecommendationHistory.RemoveAll(id => id == beatmapsetId);
        RecommendationHistory.Add(beatmapsetId);
        if (RecommendationHistory.Count > MaximumRecommendationHistory)
            RecommendationHistory.RemoveRange(0, RecommendationHistory.Count - MaximumRecommendationHistory);
    }

    public static string GetDifficultyKey(string mode, int maniaKeys) =>
        string.Equals(mode, "mania", StringComparison.OrdinalIgnoreCase) ? $"mania-{Math.Clamp(maniaKeys, 1, 10)}k" : mode;

    public static Dictionary<string, DifficultyRangeSettings> CreateDefaultDifficultyRanges()
    {
        Dictionary<string, DifficultyRangeSettings> result = new(StringComparer.OrdinalIgnoreCase)
        {
            ["osu"] = new(),
            ["taiko"] = new(),
            ["fruits"] = new()
        };
        for (int keys = 1; keys <= 10; keys++)
            result[$"mania-{keys}k"] = new DifficultyRangeSettings { MinimumStars = 2.0, MaximumStars = 4.0 };
        return result;
    }
}

public sealed class DifficultyRangeSettings
{
    public bool Automatic { get; set; } = true;
    public double MinimumStars { get; set; } = 4.0;
    public double MaximumStars { get; set; } = 5.5;
}
