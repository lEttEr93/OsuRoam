using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>一次预取的结果。</summary>
public sealed record PrefetchResult(
    int Considered,
    int Attempted,
    int Completed,
    int Parsed,
    int Hits,
    bool BudgetExhausted,
    bool DeadlineReached);

/// <summary>
/// 键型预取：把候选里「还没有可信键型」的难度按**期望收益/耗时**排队，
/// 抓 <c>.osu</c>、解析、入库，直到当前池子够用、预算烧完或到达截止时间。
///
/// 排序用「估计能进前 N 的分」（乐观分），不是单纯的星级距离或 tag 优先：
/// 星级项跨度 0.73 大于 tag 提示能带来的 0.595，所以靠近星级中心的谱会排在
/// 「tag 命中但星级偏得远」的谱前面 —— 后者压根赢不了，不该占用预算。
/// </summary>
public static class PatternPrefetcher
{
    /// <summary>整个操作最多发起这么多次 HTTP 尝试（不是每页）。</summary>
    public const int DefaultBudget = 16;

    /// <summary>从开始预取算起的总截止时间。</summary>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(10);

    /// <summary>单张预估耗时，用来算「每秒收益」；实测约 2 秒。</summary>
    private const double EstimatedSecondsPerFetch = 2.0;

    /// <summary>命中判定：勾选的键型占这张谱 ≥ 这个比例。</summary>
    public const double HitShareThreshold = 0.5;

    public static async Task<PrefetchResult> RunAsync(
        IEnumerable<OsuBeatmapset> candidates,
        AppSettings settings,
        PatternLibrary library,
        Func<int, CancellationToken, Task<string?>> fetch,
        int budget,
        TimeSpan deadline,
        IProgress<(int Verified, int Hits)>? progress,
        CancellationToken cancellationToken,
        IReadOnlySet<int>? keywordBeatmapsetIds = null,
        Action<int, int>? onIngested = null,
        int? targetOverride = null)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        List<Candidate> pending = collect(candidates, settings, library, keywordBeatmapsetIds);
        int considered = pending.Count;
        if (pending.Count == 0)
            return new PrefetchResult(0, 0, 0, 0, 0, false, false);

        IReadOnlyList<string> hints = PatternSearchHints.For(settings);
        foreach (Candidate candidate in pending)
            candidate.Score(hints, settings);
        pending.Sort((left, right) =>
        {
            int byPriority = right.Optimistic.CompareTo(left.Optimistic);
            return byPriority != 0 ? byPriority : left.BeatmapId.CompareTo(right.BeatmapId);
        });

        int target = Math.Max(1, targetOverride ?? settings.PoolSize);
        List<Candidate> queue = pending.Take(Math.Max(1, budget)).ToList();

        object gate = new();
        int attempted = 0;
        int completed = 0;
        int parsed = 0;
        int hits = 0;
        int explored = 0;
        bool deadlineReached = false;
        bool budgetExhausted = false;

        async Task worker()
        {
            while (true)
            {
                Candidate? candidate;
                lock (gate)
                {
                    if (hits >= target || cancellationToken.IsCancellationRequested)
                        return;
                    if (attempted >= budget)
                    {
                        budgetExhausted = true;
                        return;
                    }
                    if (DateTimeOffset.UtcNow - started >= deadline)
                    {
                        deadlineReached = true;
                        return;
                    }

                    candidate = takeNext(queue, ref explored, hints);
                    if (candidate is null)
                        return;
                    attempted += 1;
                }

                string? text = await fetch(candidate.BeatmapId, cancellationToken).ConfigureAwait(false);
                if (text is null)
                    continue;

                bool ingested = library.Ingest(text, candidate.BeatmapId, candidate.BeatmapsetId);
                lock (gate)
                {
                    completed += 1;
                    if (ingested)
                    {
                        parsed += 1;
                        if (isHit(library.TryGetVerified(candidate.BeatmapId), settings))
                            hits += 1;
                        // 调用方用这个回调做「判完键型再决定要不要进候选」的准入。
                        onIngested?.Invoke(candidate.BeatmapId, candidate.BeatmapsetId);
                    }
                }
                progress?.Report((parsed, hits));
            }
        }

        int workers = Math.Min(4, queue.Count);
        await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => worker())).ConfigureAwait(false);
        if (DateTimeOffset.UtcNow - started >= deadline)
            deadlineReached = true;
        return new PrefetchResult(considered, attempted, completed, parsed, hits, budgetExhausted, deadlineReached);
    }

    /// <summary>
    /// 每 4 个名额留 1 个给「基础分最高但没命中 tag」的候选：
    /// 否则预取永远只验证已经贴了标签的谱，没标签的那部分永远学不到。
    /// </summary>
    private static Candidate? takeNext(List<Candidate> queue, ref int explored, IReadOnlyList<string> hints)
    {
        if (hints.Count > 0 && (queue.Count + explored) % 4 == 3)
        {
            Candidate? best = queue
                .Where(candidate => candidate.HintStrength <= 0.15)
                .OrderByDescending(candidate => candidate.BaseScore)
                .FirstOrDefault();
            if (best is not null)
            {
                queue.Remove(best);
                explored += 1;
                return best;
            }
        }

        if (queue.Count == 0)
            return null;
        Candidate next = queue[0];
        queue.RemoveAt(0);
        return next;
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

    private static List<Candidate> collect(
        IEnumerable<OsuBeatmapset> candidates,
        AppSettings settings,
        PatternLibrary library,
        IReadOnlySet<int>? keywordBeatmapsetIds)
    {
        List<Candidate> pending = [];
        HashSet<int> seen = [];
        foreach (OsuBeatmapset set in candidates)
        {
            foreach (OsuBeatmap beatmap in set.Beatmaps)
            {
                // 关键字召回：tag 白名单不作门槛（可能只是曲名/作者撞词），交给解析结果判断。
                bool keyword = keywordBeatmapsetIds?.Contains(set.Id) == true;
                if (!CandidateFilter.Eligible(set, beatmap, settings, ignoreTagWhitelist: keyword))
                    continue;
                if (!seen.Add(beatmap.Id))
                    continue;
                // 只认「真实解析 + 当前分析器版本」；tag 推断和旧版本缓存都还要重抓。
                if (library.TryGetVerified(beatmap.Id) is not null)
                    continue;
                pending.Add(new Candidate(set, beatmap) { Keyword = keyword });
            }
        }
        return pending;
    }

    private sealed class Candidate
    {
        public Candidate(OsuBeatmapset set, OsuBeatmap beatmap)
        {
            Set = set;
            Beatmap = beatmap;
            BeatmapId = beatmap.Id;
            BeatmapsetId = set.Id;
        }

        public bool Keyword { get; init; }

        public OsuBeatmapset Set { get; }
        public OsuBeatmap Beatmap { get; }
        public int BeatmapId { get; }
        public int BeatmapsetId { get; }
        public double BaseScore { get; private set; }
        public double HintStrength { get; private set; }
        public double Optimistic { get; private set; }

        /// <summary>
        /// 基础分 = 星级项 + 时长项（和正式评分同构，但没有玩家画像，mapper 项置 0）。
        /// </summary>
        public void Score(IReadOnlyList<string> hints, AppSettings settings)
        {
            double center = (settings.MinimumStars + settings.MaximumStars) / 2;
            double radius = Math.Max(0.2, (settings.MaximumStars - settings.MinimumStars) / 2);
            double starFit = Math.Clamp(1 - Math.Abs(Beatmap.DifficultyRating - center) / radius, 0, 1);
            double lengthFit = Math.Clamp(1 - Math.Abs(Beatmap.TotalLength - 150) / 180.0, 0, 1);
            BaseScore = starFit * 0.73 + lengthFit * 0.12;

            HintStrength = hintStrength(hints);
            // mu 是 SelectedShare 的乐观估计，0.20 是探索余量（不是置信度）。
            Optimistic = BaseScore + 0.70 * Math.Min(1, HintStrength + 0.20);
            // 关键字召回的候选优先验证：它们是「对症」的那一小撮，默认排序翻多少页都捞不到。
            if (Keyword)
                Optimistic += 1.0;
        }

        private double hintStrength(IReadOnlyList<string> hints)
        {
            if (hints.Count == 0)
                return 0.15;

            IReadOnlyList<string> tags = AppSettings.ParseTags(Set.Tags);
            if (hints.Any(hint => tags.Any(tag => containsWord(tag, hint))))
                return 0.60;

            string name = $"{Set.Title} {Set.Artist} {Beatmap.Version}";
            return hints.Any(hint => containsWord(name, hint)) ? 0.35 : 0.15;
        }

        /// <summary>按整词判断，避免把作者名里的 jack（如 jackson）当成同等级证据。</summary>
        private static bool containsWord(string text, string word)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            int index = 0;
            while ((index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool leftOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                int end = index + word.Length;
                bool rightOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (leftOk && rightOk)
                    return true;
                index = end;
            }
            return false;
        }
    }
}
