using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 预下载的排队规则：谁先下。
///
/// 冷启动时池子里先出现的是「补位」卡片（验证结果还没回来），如果它们占了下载通道，
/// 后到的够格候选就只能干等 —— 这是「候选全部下载卡住」的直接原因。
/// 所以顺序固定为：正在玩 / 已预约下一首 → 够格（非补位）→ 补位垫底。
/// 已经下完的不排队；失败过一次的也不再自动重试，避免失败即重试的死循环。
/// </summary>
public static class PoolDownloadPlanner
{
    public static List<RecommendationItem> Order(
        IEnumerable<RecommendationItem> pool,
        RecommendationItem? playing,
        RecommendationItem? preparedNext,
        ISet<int> alreadyRetried)
    {
        List<RecommendationItem> ordered = [];
        foreach (RecommendationItem item in pool)
        {
            if (item.DownloadState is not (DownloadState.Waiting or DownloadState.Failed))
                continue;
            if (item.DownloadState == DownloadState.Failed && alreadyRetried.Contains(item.BeatmapsetId))
                continue;
            ordered.Add(item);
        }
        return
        [
            .. ordered.OrderBy(item => priority(item, playing, preparedNext))
        ];
    }

    private static int priority(RecommendationItem item, RecommendationItem? playing, RecommendationItem? preparedNext)
    {
        if (ReferenceEquals(item, playing) || ReferenceEquals(item, preparedNext))
            return 0;
        return item.IsFallback ? 2 : 1;
    }
}
