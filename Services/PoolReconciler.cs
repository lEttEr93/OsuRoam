using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 流式发布时把推荐池对齐到「当前排序结果」。
///
/// 键型验证是异步的，池子会边验证边重排，所以不能简单地清空重填：
/// 正在玩 / 正在下载 / 已预约下一首的卡片必须留在池子里，
/// 而同 id 的卡片要复用（封面、详情展开状态跟着保留，不然每 400 毫秒闪一次）。
/// </summary>
public static class PoolReconciler
{
    /// <summary>按 <paramref name="desired"/> 的顺序排出新的池子。</summary>
    /// <param name="current">当前池子。</param>
    /// <param name="desired">本次排序结果（可能比池子大，按池子上限截断）。</param>
    /// <param name="pinnedBeatmapsetIds">不能被换掉的谱面集（正在玩 / 下载 / 预约中）。</param>
    /// <param name="limit">池子上限。</param>
    public static List<RecommendationItem> Build(
        IReadOnlyList<RecommendationItem> current,
        IReadOnlyList<RecommendationItem> desired,
        ISet<int> pinnedBeatmapsetIds,
        int limit)
    {
        int capacity = Math.Max(1, limit);
        List<RecommendationItem> next = [];
        foreach (RecommendationItem item in current)
        {
            if (!pinnedBeatmapsetIds.Contains(item.BeatmapsetId))
                continue;
            if (next.Any(other => other.BeatmapsetId == item.BeatmapsetId))
                continue;
            next.Add(item);
        }

        foreach (RecommendationItem item in desired)
        {
            if (next.Count >= capacity)
                break;
            if (next.Any(other => other.BeatmapsetId == item.BeatmapsetId))
                continue;
            next.Add(current.FirstOrDefault(existing => existing.BeatmapsetId == item.BeatmapsetId) ?? item);
        }
        return next;
    }

    /// <summary>两个池子的谱面集顺序是否一致（一致就不必动 UI 集合）。</summary>
    public static bool SameSequence(IReadOnlyList<RecommendationItem> left, IReadOnlyList<RecommendationItem> right)
    {
        if (left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index += 1)
        {
            if (left[index].BeatmapsetId != right[index].BeatmapsetId)
                return false;
        }
        return true;
    }
}
