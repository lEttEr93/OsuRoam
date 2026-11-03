using OsuRoam.Models;

namespace OsuRoam.Services;

public sealed class RecommendationEngine
{
    public IReadOnlyList<RecommendationItem> RankAvoidingHistory(
        IEnumerable<OsuBeatmapset> beatmapsets,
        PlayerProfile profile,
        AppSettings settings,
        double minimumStars,
        double maximumStars,
        ISet<int> excludedBeatmapsetIds,
        IReadOnlyList<int> recommendationHistory,
        int count,
        Func<int, ManiaPattern?>? patternLookup = null,
        double[]? patternPreference = null,
        double patternWeight = 0,
        IReadOnlySet<int>? excludedBeatmapIds = null)
    {
        if (count <= 0) return [];
        List<OsuBeatmapset> source = beatmapsets.ToList();
        HashSet<int> historyIds = recommendationHistory.Where(id => id > 0).ToHashSet();
        HashSet<int> excludedFresh = [.. excludedBeatmapsetIds, .. historyIds];
        List<RecommendationItem> result = Rank(source, profile, settings, minimumStars, maximumStars, excludedFresh, count, false, patternLookup, patternPreference, patternWeight, excludedBeatmapIds).ToList();
        HashSet<int> selectedIds = [.. excludedBeatmapsetIds, .. result.Select(item => item.BeatmapsetId)];
        if (result.Count >= count) return result;

        HashSet<int> checkedHistoryIds = [];
        foreach (int beatmapsetId in recommendationHistory)
        {
            if (beatmapsetId <= 0 || !checkedHistoryIds.Add(beatmapsetId) || selectedIds.Contains(beatmapsetId)) continue;
            RecommendationItem? reusable = Rank(source.Where(set => set.Id == beatmapsetId), profile, settings, minimumStars, maximumStars, selectedIds, 1, isRecycled: true, patternLookup, patternPreference, patternWeight, excludedBeatmapIds).FirstOrDefault();
            if (reusable is null) continue;
            result.Add(reusable);
            selectedIds.Add(reusable.BeatmapsetId);
            if (result.Count >= count) break;
        }
        return result;
    }

    public IReadOnlyList<RecommendationItem> Rank(
        IEnumerable<OsuBeatmapset> beatmapsets,
        PlayerProfile profile,
        AppSettings settings,
        double minimumStars,
        double maximumStars,
        ISet<int> excludedBeatmapsetIds,
        int count,
        bool isRecycled = false,
        Func<int, ManiaPattern?>? patternLookup = null,
        double[]? patternPreference = null,
        double patternWeight = 0,
        IReadOnlySet<int>? excludedBeatmapIds = null)
    {
        double starCenter = (minimumStars + maximumStars) / 2;
        double starRadius = Math.Max(0.2, (maximumStars - minimumStars) / 2);
        int maxMapperCount = Math.Max(1, profile.MapperFrequency.Values.DefaultIfEmpty(0).Max());
        List<OsuBeatmapset> source = beatmapsets.ToList();
        List<RecommendationItem> candidates = [];
        HashSet<int> rankedBeatmapsetIds = [];

        foreach (OsuBeatmapset set in source)
        {
            if (excludedBeatmapsetIds.Contains(set.Id)) continue;

            if (!settings.MatchesSimpleFilters(set)) continue;

            IReadOnlyList<string> setTags = AppSettings.ParseTags(set.Tags);
            if (settings.ExcludedTags.Any(tag => setTags.Contains(tag, StringComparer.OrdinalIgnoreCase))) continue;
            if (settings.IncludedTags.Count > 0 && !settings.IncludedTags.Any(tag => setTags.Contains(tag, StringComparer.OrdinalIgnoreCase))) continue;

            int year = (set.RankedDate ?? set.SubmittedDate)?.Year ?? 0;
            if (year < settings.MinimumYear || year > settings.MaximumYear) continue;

            RecommendationItem? bestDifficulty = null;
            foreach (OsuBeatmap beatmap in set.Beatmaps)
            {
                if (!string.Equals(beatmap.Mode, settings.Mode, StringComparison.OrdinalIgnoreCase)) continue;
                if (settings.Mode == "mania" && (int)Math.Round(beatmap.CircleSize) != settings.ManiaKeys) continue;
                // 按「难度」去重：同一首曲子的其他难度仍然可以推荐。
                if (excludedBeatmapIds is not null && excludedBeatmapIds.Contains(beatmap.Id)) continue;
                if (beatmap.DifficultyRating < minimumStars || beatmap.DifficultyRating > maximumStars) continue;
                if (beatmap.TotalLength < settings.MinimumLengthSeconds || beatmap.TotalLength > settings.MaximumLengthSeconds) continue;

                double starFit = clamp01(1 - Math.Abs(beatmap.DifficultyRating - starCenter) / starRadius);
                double lengthFit = proximity(beatmap.TotalLength, profile.PreferredLength, 180);
                double mapperFit = profile.MapperFrequency.TryGetValue(set.Creator, out int mapperCount) ? (double)mapperCount / maxMapperCount : 0;
                double discovery = Random.Shared.NextDouble();
                double familiarityWeight = 1 - settings.Exploration * 0.55;
                double tagFit = settings.IncludedTags.Count == 0 ? 0 : (double)settings.IncludedTags.Count(tag => setTags.Contains(tag, StringComparer.OrdinalIgnoreCase)) / settings.IncludedTags.Count;
                double simpleFilterFit = settings.SimpleFilterFit(set);
                double simpleFilterBonus = simpleFilterFit * 0.18 + settings.DefaultStatusPriority(set) * 0.08;
                double patternFit = 0;
                double effectivePatternWeight = 0;
                // 勾选了键型就用勾选结果当目标向量（强权重）；没勾才退回「从成绩反推」的旧路径。
                bool usesSelection = settings.SelectedPatterns.Count > 0;
                ManiaPattern? pattern = patternLookup is not null &&
                                        (usesSelection || settings.PreferLn || (patternPreference is not null && patternWeight > 0))
                    ? patternLookup(beatmap.Id)
                    : null;

                // 「只要面谱」：拿得到键型才判，没数据的候选先放过（预取落地后会变严格）。
                if (settings.PreferLn && pattern is not null && pattern.LnRatio < ManiaPattern.LnChartThreshold)
                    continue;

                if (usesSelection)
                {
                    if (pattern is not null)
                        patternFit = pattern.SelectedShare(settings.SelectedPatterns);
                    effectivePatternWeight = SelectedPatternWeight;
                }
                else if (pattern is not null && patternPreference is not null && patternWeight > 0)
                {
                    patternFit = PatternPreference.Fit(pattern, patternPreference!);
                    effectivePatternWeight = patternWeight;
                }
                // BPM 不参与排序：实测它和键型分布几乎无关（vs Stream −0.06、vs Chordstream +0.13、
                // vs Jacks −0.09），而「喜欢的 BPM」只是已打谱面的中位数，噪声比信号大。
                // 原来的 0.15 权重按比例分给了其余三项（0.62/0.10/0.13 → 0.73/0.12/0.15）。
                double score = familiarityWeight * (starFit * 0.73 + lengthFit * 0.12 + mapperFit * 0.15) + settings.Exploration * 0.55 * discovery + tagFit * 0.12 + simpleFilterBonus + patternFit * effectivePatternWeight;
                // 理由只记「哪种匹配 + 两个数值」，具体文字按当前语言现拼，
                // 这样切语言时卡片上的理由会跟着变。
                ReasonKind reasonKind = tagFit > 0
                    ? ReasonKind.Tags
                    : mapperFit > 0.3
                        ? ReasonKind.Mapper
                        : ReasonKind.Explore;
                string reasonCategory = pattern is not null && pattern.Source == PatternSource.Osu
                    ? pattern.Category
                    : string.Empty;

                RecommendationItem item = new()
                {
                    BeatmapId = beatmap.Id, BeatmapsetId = set.Id, Artist = set.Artist, Title = set.Title,
                    DifficultyName = beatmap.Version, Mapper = set.Creator, Stars = beatmap.DifficultyRating,
                    Bpm = beatmap.Bpm, LengthSeconds = beatmap.TotalLength, RankedYear = year, Score = score,
                    ReasonKind = reasonKind, ReasonTagFit = tagFit, ReasonStarFit = starFit, ReasonCategory = reasonCategory,
                    Tags = setTags, TagItems = setTags.Select(tag => new RecommendationTag(tag, settings.IncludedTags.Contains(tag, StringComparer.OrdinalIgnoreCase))).ToList(), KeyCount = settings.Mode == "mania" ? (int)Math.Round(beatmap.CircleSize) : 0,
                    IsRecycled = isRecycled
                };
                if (bestDifficulty is null || item.Score > bestDifficulty.Score) bestDifficulty = item;
            }
            if (bestDifficulty is not null && rankedBeatmapsetIds.Add(set.Id)) candidates.Add(bestDifficulty);
        }
        return candidates
            .OrderByDescending(item => settings.StrictSimpleFilterMatchCount(source.FirstOrDefault(set => set.Id == item.BeatmapsetId) ?? new OsuBeatmapset()))
            .ThenByDescending(item => settings.DefaultStatusPriority(source.FirstOrDefault(set => set.Id == item.BeatmapsetId) ?? new OsuBeatmapset()))
            .ThenByDescending(item => item.Score)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// 勾选键型时给 patternFit 的权重。星级那一项折算后最高约 0.55（0.73 × 熟悉度权重），
    /// 这里取 0.6 让「选了什么」能和星级掰手腕，但不至于完全盖过它。
    /// </summary>
    private const double SelectedPatternWeight = 0.60;

    private static double proximity(double value, double target, double tolerance) => clamp01(1 - Math.Abs(value - target) / Math.Max(1, tolerance));
    private static double clamp01(double value) => Math.Max(0, Math.Min(1, value));
}
