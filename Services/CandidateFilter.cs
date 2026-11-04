using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 候选难度是否「有资格被推荐」的唯一判定。
/// 预取和排名必须用同一份条件，否则预算会花在永远进不了池子的难度上
/// （搜索命中的是谱面集，不保证其中每个难度都满足 keys / 星级 / 时长）。
/// </summary>
public static class CandidateFilter
{
    public static bool Eligible(OsuBeatmapset set, OsuBeatmap beatmap, AppSettings settings)
    {
        if (!EligibleIgnoringInstalled(set, beatmap, settings))
            return false;
        return !isInstalled(beatmap, settings);
    }

    /// <summary>「排除已安装谱面」是否把这张难度挡掉了。</summary>
    private static bool isInstalled(OsuBeatmap beatmap, AppSettings settings) =>
        settings.ExcludeInstalledBeatmaps && LocalBeatmapLibrary.HasData &&
        LocalBeatmapLibrary.InstalledBeatmapIds.Contains(beatmap.Id);

    /// <summary>除「排除已安装」之外的全部条件。</summary>
    public static bool EligibleIgnoringInstalled(OsuBeatmapset set, OsuBeatmap beatmap, AppSettings settings)
    {
        if (set is null || beatmap is null)
            return false;
        if (beatmap.Id <= 0 || set.Id <= 0)
            return false;
        if (!string.Equals(beatmap.Mode, settings.Mode, StringComparison.OrdinalIgnoreCase))
            return false;
        if (settings.Mode == "mania" && (int)Math.Round(beatmap.CircleSize) != settings.ManiaKeys)
            return false;
        if (beatmap.DifficultyRating < settings.MinimumStars || beatmap.DifficultyRating > settings.MaximumStars)
            return false;
        if (beatmap.TotalLength < settings.MinimumLengthSeconds || beatmap.TotalLength > settings.MaximumLengthSeconds)
            return false;

        int year = (set.RankedDate ?? set.SubmittedDate)?.Year ?? 0;
        if (year < settings.MinimumYear || year > settings.MaximumYear)
            return false;

        if (!settings.MatchesSimpleFilters(set))
            return false;

        IReadOnlyList<string> tags = AppSettings.ParseTags(set.Tags);
        if (settings.ExcludedTags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            return false;
        if (settings.IncludedTags.Count > 0 && !settings.IncludedTags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    /// <summary>
    /// 池子被筛空时用来说清原因：多少候选、多少还能推荐、其中多少是被「排除已安装谱面」挡掉的、
    /// 多少个谱面集连 tag / 状态都没过。没有这份统计，「没有候选谱面」就是个死胡同。
    /// </summary>
    public static CandidateDiagnosis Diagnose(IEnumerable<OsuBeatmapset> sets, AppSettings settings)
    {
        int setCount = 0;
        int eligibleDiffs = 0;
        int installedDiffs = 0;
        int statusMissed = 0;
        int tagMissed = 0;
        foreach (OsuBeatmapset set in sets)
        {
            setCount += 1;
            IReadOnlyList<string> tags = AppSettings.ParseTags(set.Tags);
            if (!settings.MatchesSimpleFilters(set))
            {
                statusMissed += 1;
                continue;
            }
            if (settings.ExcludedTags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) ||
                (settings.IncludedTags.Count > 0 && !settings.IncludedTags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase))))
            {
                tagMissed += 1;
                continue;
            }
            foreach (OsuBeatmap beatmap in set.Beatmaps)
            {
                if (!EligibleIgnoringInstalled(set, beatmap, settings))
                    continue;
                if (isInstalled(beatmap, settings))
                    installedDiffs += 1;
                else
                    eligibleDiffs += 1;
            }
        }
        return new CandidateDiagnosis(setCount, eligibleDiffs, installedDiffs, statusMissed, tagMissed);
    }
}

/// <summary>一次候选漏斗统计。</summary>
public sealed record CandidateDiagnosis(
    int Sets,
    int EligibleDiffs,
    int InstalledDiffs,
    int StatusMissedSets,
    int TagMissedSets);
