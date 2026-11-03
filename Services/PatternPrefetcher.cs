using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>一次预取的结果。</summary>
public sealed record PrefetchResult(int Considered, int Attempted, int Parsed, int Hits, bool BudgetExhausted);

/// <summary>
/// 键型预取：把候选池里「还没有真实键型」的谱面按值得抓的顺序排一遍，
/// 抓 <c>.osu</c>、解析、入库，直到**命中够用**或预算烧完。
///
/// 按设计笔记第 5 条：目标是「池子填满」，不是「抓够 N 张」。
/// 所以先按星级接近程度排序（离目标星级越近越值得抓），命中够了立刻停，
/// 剩下的候选留给下一轮（第 6 条：未露面的候选是缓冲）。
/// </summary>
public static class PatternPrefetcher
{
    /// <summary>
    /// 一轮最多抓这么多张。实测单张 2 秒左右（87 KB / 39 KB 两张分别是 2.4 s / 1.9 s），
    /// 4 路并发下 20 张约 10 秒；命中够了会提前停，抓过的下次直接从库里读。
    /// </summary>
    public const int DefaultBudget = 20;

    /// <summary>命中判定：勾选的键型占这张谱 ≥ 这个比例。</summary>
    private const double HitShareThreshold = 0.5;

    public static async Task<PrefetchResult> RunAsync(
        IEnumerable<OsuBeatmapset> candidates,
        AppSettings settings,
        PatternLibrary library,
        Func<int, CancellationToken, Task<string?>> fetch,
        int budget,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        List<(int BeatmapId, int BeatmapsetId, double Stars, bool TagHint)> pending =
            collect(candidates, settings, library);
        int considered = pending.Count;
        if (pending.Count == 0)
            return new PrefetchResult(0, 0, 0, 0, false);

        // 排两档：tag 里已经点名的先抓（关键字命中率高得离谱），再按离目标星级的距离。
        double center = (settings.MinimumStars + settings.MaximumStars) / 2;
        pending.Sort((left, right) =>
        {
            int byHint = right.TagHint.CompareTo(left.TagHint);
            return byHint != 0
                ? byHint
                : Math.Abs(left.Stars - center).CompareTo(Math.Abs(right.Stars - center));
        });

        int target = Math.Max(4, settings.PoolSize * 2);
        List<(int BeatmapId, int BeatmapsetId, double Stars, bool TagHint)> batch =
            pending.Take(Math.Max(1, budget)).ToList();

        int attempted = 0;
        int parsed = 0;
        int hits = 0;
        int next = -1;
        object gate = new();

        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        async Task worker()
        {
            while (true)
            {
                int index;
                lock (gate)
                {
                    if (stop.IsCancellationRequested || hits >= target)
                        return;
                    index = Interlocked.Increment(ref next);
                    if (index >= batch.Count)
                        return;
                }

                (int beatmapId, int beatmapsetId, _, _) = batch[index];
                string? text = await fetch(beatmapId, cancellationToken).ConfigureAwait(false);
                bool ingested = false;
                if (text is not null)
                {
                    lock (gate)
                    {
                        attempted += 1;
                    }
                    ingested = library.Ingest(text, beatmapId, beatmapsetId);
                    if (ingested)
                    {
                        lock (gate)
                        {
                            parsed += 1;
                            if (isHit(library.TryGet(beatmapId), settings))
                                hits += 1;
                        }
                    }
                }

                int done;
                lock (gate)
                {
                    done = attempted;
                }
                progress?.Report((done, Math.Min(batch.Count, target * 2)));
            }
        }

        int workers = Math.Min(4, batch.Count);
        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => worker())).ConfigureAwait(false);
        bool exhausted = attempted >= batch.Count && hits < target;
        return new PrefetchResult(considered, attempted, parsed, hits, exhausted);
    }

    private static bool isHit(ManiaPattern? pattern, AppSettings settings)
    {
        if (pattern is null)
            return false;
        if (settings.PreferLn && !pattern.IsLnChart)
            return false;
        if (settings.SelectedPatterns.Count > 0 && pattern.SelectedShare(settings.SelectedPatterns) < HitShareThreshold)
            return false;
        return true;
    }

    private static List<(int BeatmapId, int BeatmapsetId, double Stars, bool TagHint)> collect(
        IEnumerable<OsuBeatmapset> candidates,
        AppSettings settings,
        PatternLibrary library)
    {
        IReadOnlyList<string> hints = PatternSearchHints.For(settings);
        List<(int, int, double, bool)> pending = [];
        HashSet<int> seen = [];
        foreach (OsuBeatmapset set in candidates)
        {
            // tag 里已经点名了所选键型 → 值得先抓，命中率远高于盲抽。
            bool tagHint = hints.Count > 0 && hints.Any(hint =>
                (set.Tags ?? string.Empty).Contains(hint, StringComparison.OrdinalIgnoreCase));
            foreach (OsuBeatmap beatmap in set.Beatmaps)
            {
                if (!string.Equals(beatmap.Mode, settings.Mode, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (settings.Mode == "mania" && (int)Math.Round(beatmap.CircleSize) != settings.ManiaKeys)
                    continue;
                if (beatmap.Id <= 0 || !seen.Add(beatmap.Id))
                    continue;
                // tag 推断出来的不算「有真实键型」——那是预取要替换掉的东西。
                if (library.TryGet(beatmap.Id) is { IsInferred: false })
                    continue;
                pending.Add((beatmap.Id, set.Id, beatmap.DifficultyRating, tagHint));
            }
        }
        return pending;
    }
}
