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

        if (settings.ExcludeInstalledBeatmaps && LocalBeatmapLibrary.HasData &&
            LocalBeatmapLibrary.InstalledBeatmapIds.Contains(beatmap.Id))
            return false;

        return true;
    }
}
