namespace OsuRoam.Models;

public sealed class AppSettings
{
    public const int MaximumRecommendationHistory = 500;
    public const double MinimumSelectableStars = 0.0;
    public const double MaximumFiniteSelectableStars = 11.0;
    public const double UnlimitedMaximumStars = 999.0;
    public const string DefaultDownloadMirrors = "https://dl.sayobot.cn/beatmaps/download/full/{setId}\nhttps://txy1.sayobot.cn/beatmaps/download/full/{setId}";
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
    private static readonly string[] DefaultTagPool = [
        "anime", "classical", "electronic", "hip hop", "jazz", "jpop", "kpop", "metal", "pop", "rock", "vocaloid",
        "ambient", "drum and bass", "dubstep", "hardcore", "house", "instrumental", "orchestral", "trance",
        "1k", "2k", "3k", "4k", "5k", "6k", "7k", "8k", "9k", "10k",
        "rice", "ln", "jack", "chordjack", "handstream", "stream", "speed", "stamina", "tech", "sv", "scroll",
        "米", "面", "叠", "技", "乱", "切"
    ];
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
    public bool ExcludeRecentlyPlayed { get; set; } = true;
    public double Exploration { get; set; } = 0.45;
    public int PoolSize { get; set; } = 5;
    public int ResultDelaySeconds { get; set; } = 5;
    public bool AutoDownload { get; set; } = true;
    public bool AutoImport { get; set; } = true;
    public string DownloadUrlTemplate { get; set; } = "https://dl.sayobot.cn/beatmaps/download/full/{setId}";
    public string DownloadMirrorTemplates { get; set; } = DefaultDownloadMirrors;
    public string OsuExecutablePath { get; set; } = string.Empty;
    public string OsuClient { get; set; } = "lazer";
    public string TosuWebSocketUrl { get; set; } = "ws://127.0.0.1:24050/websocket/v2";
    public string TosuExecutablePath { get; set; } = string.Empty;
    public bool AutoStartTosu { get; set; } = true;
    public bool StopManagedTosuOnExit { get; set; } = true;
    public List<int> RecommendationHistory { get; set; } = [];
    public List<string> TagPool { get; set; } = [];
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
        TagPool = normalizeTags(DefaultTagPool.Concat(TagPool));
        IncludedTags = normalizeTags(IncludedTags);
        ExcludedTags = normalizeTags(ExcludedTags);
        ExcludedTags.RemoveAll(tag => IncludedTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
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
        TagPool = normalizeTags(TagPool.Concat(tags));
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
        .Select(tag => tag.Trim().ToLowerInvariant())
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
