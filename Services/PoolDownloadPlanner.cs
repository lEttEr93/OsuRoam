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
            // 失败过、或者为了给够格候选让位而被取消的，都不再自动重下：
            // 否则「让位 → 状态回到 Waiting → 又被排上 → 再让位」会来回抖。
            if (alreadyRetried.Contains(item.BeatmapsetId))
                continue;
            ordered.Add(item);
        }
        return
        [
            .. ordered.OrderBy(item => priority(item, playing, preparedNext))
        ];
    }

    /// <summary>
    /// 在途下载该不该取消。判据必须是「卡片是否还在池子里 / 是否已经不需要下载」，
    /// **不能**用「待下载清单」—— 那张卡一旦开始下载状态就不再是 Waiting，
    /// 用它当判据会把刚启动的下载立刻取消，取消又把状态打回 Waiting，于是无限重启
    /// （线上出现过每秒 2800 次取消的循环，所有下载都下不完）。
    /// </summary>
    public static bool ShouldCancel(int beatmapsetId, IEnumerable<RecommendationItem> pool)
    {
        foreach (RecommendationItem item in pool)
        {
            if (item.BeatmapsetId != beatmapsetId)
                continue;
            return item.DownloadState is DownloadState.Downloaded or DownloadState.Imported or DownloadState.Importing;
        }
        return true;
    }

    private static int priority(RecommendationItem item, RecommendationItem? playing, RecommendationItem? preparedNext)
    {
        if (ReferenceEquals(item, playing) || ReferenceEquals(item, preparedNext))
            return 0;
        return item.IsFallback ? 2 : 1;
    }
}
