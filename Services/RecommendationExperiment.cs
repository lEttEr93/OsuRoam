using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 开发用：把「推荐结果偏某类键型」这件事变成可测的数字。
///
/// 做法是跑几个场景（真实画像 + 几个虚拟画像），每个场景：
/// 1. 按场景的星级区间从 API 拉同一批候选（tag 全空、无键型偏好、无历史）；
/// 2. 从候选里**随机抽一批**当基准，测出这个星级区间的谱池本身长什么样；
/// 3. 让推荐引擎选出 N 首，再逐首下载 `.osu` 做键型分析；
/// 4. 对比「推荐」和「池样本」的键型分布 —— 如果两者接近，说明偏的是谱池，
///    不是排序；如果推荐明显比池子更偏某一类，才是引擎的锅。
/// </summary>
public static class RecommendationExperiment
{
    private sealed record Scenario(
        string Name, double MinStars, double MaxStars, double Exploration,
        double PreferredBpm, double PreferredLength, string Mapper);

    private static readonly HttpClient download = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<string> RunAsync(
        OsuApiClient api,
        AppSettings baseSettings,
        string username,
        CancellationToken cancellationToken)
    {
        download.DefaultRequestHeaders.UserAgent.ParseAdd("osu-roam-experiment");
        StringBuilder report = new();
        report.AppendLine($"# osu! Roam 推荐实验 {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine($"基准设置：mode={baseSettings.Mode} keys={baseSettings.ManiaKeys} " +
                          $"长度 {baseSettings.MinimumLengthSeconds}-{baseSettings.MaximumLengthSeconds}s，" +
                          "tag 全空、无键型偏好、无推荐历史");
        report.AppendLine();

        PlayerProfile? realProfile = null;
        try
        {
            realProfile = await api.BuildProfileAsync(username, baseSettings.Mode,
                baseSettings.Mode == "mania" ? baseSettings.ManiaKeys : null, cancellationToken);
            report.AppendLine($"真实画像：{realProfile.User.Username}｜BPM {realProfile.PreferredBpm:0}｜" +
                              $"时长 {realProfile.PreferredLength:0}s｜样本 {realProfile.DifficultySampleCount}");
        }
        catch (Exception exception)
        {
            report.AppendLine($"真实画像拉取失败：{exception.Message}");
        }
        report.AppendLine();

        List<Scenario> scenarios = [];
        if (realProfile is not null)
            scenarios.Add(new Scenario("A 真实画像", 4.1, 4.9, 0.45,
                realProfile.PreferredBpm, realProfile.PreferredLength, string.Empty));
        scenarios.Add(new Scenario("B 虚拟：stream 型玩家", 4.1, 4.9, 0.45, 180, 150, string.Empty));
        scenarios.Add(new Scenario("C 虚拟：叠键/高 BPM 玩家", 4.9, 5.5, 0.60, 205, 120, string.Empty));
        scenarios.Add(new Scenario("D 虚拟：长条/短曲玩家", 3.6, 4.2, 0.20, 150, 90, string.Empty));

        Dictionary<int, ManiaPattern?> patternCache = [];

        foreach (Scenario scenario in scenarios)
        {
            AppSettings settings = cloneForScenario(baseSettings, scenario);
            List<OsuBeatmapset> candidates = [];
            try
            {
                await api.SearchCandidatesInBatchesAsync(settings, scenario.MinStars, scenario.MaxStars,
                    (batch, _) => { candidates.AddRange(batch); return Task.CompletedTask; }, cancellationToken);
            }
            catch (Exception exception)
            {
                report.AppendLine($"## {scenario.Name}");
                report.AppendLine($"   搜索失败：{exception.Message}");
                report.AppendLine();
                continue;
            }

            PlayerProfile profile = scenario.Name.StartsWith('A') && realProfile is not null
                ? realProfile
                : new PlayerProfile(
                    new OsuUser { Username = "synthetic" },
                    [], [],
                    scenario.MinStars, scenario.MaxStars,
                    scenario.PreferredBpm, scenario.PreferredLength,
                    0, new Dictionary<string, int>(), new HashSet<int>());

            RecommendationEngine engine = new();
            IReadOnlyList<RecommendationItem> picks = engine.RankAvoidingHistory(
                candidates, profile, settings, scenario.MinStars, scenario.MaxStars,
                new HashSet<int>(), [], 8, null, null, 0);

            // 池样本：均匀抽 30 个，代表这个星级区间能搜到的谱面本身
            List<OsuBeatmap> poolSample = sampleBeatmaps(candidates, settings, scenario, 30);

            report.AppendLine($"## {scenario.Name}（{scenario.MinStars:0.0}-{scenario.MaxStars:0.0}★，" +
                              $"探索 {scenario.Exploration:0.00}，偏好 BPM {scenario.PreferredBpm:0} / " +
                              $"{scenario.PreferredLength:0}s）");
            report.AppendLine($"   候选 {candidates.Count} 个谱面集");

            List<(string Label, double Stars, ManiaPattern? Pattern, IReadOnlyList<string> Tags)> poolRows = [];
            foreach (OsuBeatmap beatmap in poolSample)
            {
                ManiaPattern? pattern = await resolvePatternAsync(beatmap.Id, patternCache, cancellationToken);
                poolRows.Add(($"pool {beatmap.Id}", beatmap.DifficultyRating, pattern, []));
            }
            report.AppendLine("   " + Distribution("池样本", poolRows.Select(row => row.Pattern).ToList()));

            List<(string Label, double Stars, ManiaPattern? Pattern, IReadOnlyList<string> Tags)> pickRows = [];
            foreach (RecommendationItem item in picks)
            {
                ManiaPattern? pattern = await resolvePatternAsync(item.BeatmapId, patternCache, cancellationToken);
                pickRows.Add((item.DisplayTitle, item.Stars, pattern, item.Tags));
                if (pattern is null)
                    continue;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "   · {0:0.00}★ {1,3:0}bpm {2}  {3,-18} 流 {4:0.00} 和弦流 {5:0.00} 叠键 {6:0.00}  " +
                    "最高子型 {7}",
                    item.Stars, item.Bpm, TimeSpan.FromSeconds(item.LengthSeconds).ToString(@"m\:ss"),
                    pattern.Category, share(pattern, "Stream"), share(pattern, "Chordstream"),
                    share(pattern, "Jacks"), topSpecific(pattern)));
            }
            report.AppendLine("   " + Distribution("推荐  ", pickRows.Select(row => row.Pattern).ToList()));
            report.AppendLine();
        }

        return report.ToString();
    }

    /// <summary>场景之间只改这几个值，其余照抄用户配置（模式、键数、长度范围、池大小）。</summary>
    private static AppSettings cloneForScenario(AppSettings source, Scenario scenario)
    {
        AppSettings settings = new()
        {
            Username = source.Username,
            ClientId = source.ClientId,
            Mode = source.Mode,
            ManiaKeys = source.ManiaKeys,
            MinimumStars = scenario.MinStars,
            MaximumStars = scenario.MaxStars,
            MinimumYear = source.MinimumYear,
            MaximumYear = source.MaximumYear,
            MinimumLengthSeconds = source.MinimumLengthSeconds,
            MaximumLengthSeconds = source.MaximumLengthSeconds,
            Exploration = scenario.Exploration,
            PoolSize = 8,
            ExcludeRecentlyPlayed = false,
            IncludedTags = [],
            ExcludedTags = [],
            RecommendationHistory = [],
            IncludedStatuses = [.. AppSettings.StatusFilterValues],
            IncludedGenres = [],
            IncludedLanguages = [],
            StrictStatuses = [],
            StrictGenres = [],
            StrictLanguages = []
        };
        return settings;
    }

    private static List<OsuBeatmap> sampleBeatmaps(
        IReadOnlyList<OsuBeatmapset> candidates, AppSettings settings, Scenario scenario, int count)
    {
        List<OsuBeatmap> pool = [];
        foreach (OsuBeatmapset set in candidates)
        {
            OsuBeatmap? best = null;
            foreach (OsuBeatmap beatmap in set.Beatmaps)
            {
                if (!string.Equals(beatmap.Mode, settings.Mode, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (settings.Mode == "mania" && (int)Math.Round(beatmap.CircleSize) != settings.ManiaKeys)
                    continue;
                if (beatmap.DifficultyRating < scenario.MinStars || beatmap.DifficultyRating > scenario.MaxStars)
                    continue;
                if (beatmap.TotalLength < settings.MinimumLengthSeconds || beatmap.TotalLength > settings.MaximumLengthSeconds)
                    continue;
                if (best is null || beatmap.DifficultyRating > best.DifficultyRating)
                    best = beatmap;
            }
            if (best is not null)
                pool.Add(best);
        }

        if (pool.Count <= count)
            return pool;

        // 等间隔抽样：均匀覆盖整个区间，不受排序影响
        double step = (double)pool.Count / count;
        return Enumerable.Range(0, count).Select(index => pool[(int)(index * step)]).ToList();
    }

    /// <summary>
    /// 按 BPM 分桶测键型：验证「不同键型有各自的 BPM 优势区间」这个说法。
    ///
    /// 不经过推荐引擎，直接把一个星级区间的候选按 BPM 切桶，逐张拉 `.osu` 算真值，
    /// 看低 BPM 桶里叠键占比是不是真的更高、高 BPM 桶里是不是流系更高。
    /// 真值缓存写进 `data/pattern-truth.json`，重跑就不用再拉一遍。
    /// </summary>
    public static async Task<string> RunBpmAsync(
        OsuApiClient api,
        AppSettings baseSettings,
        double minStars,
        double maxStars,
        int perBucket,
        CancellationToken cancellationToken)
    {
        download.DefaultRequestHeaders.UserAgent.ParseAdd("osu-roam-experiment");

        AppSettings settings = cloneForScenario(baseSettings,
            new Scenario("bpm", minStars, maxStars, 0.45, 180, 180, string.Empty));

        List<OsuBeatmapset> candidates = [];
        await api.SearchCandidatesInBatchesAsync(settings, minStars, maxStars,
            (batch, _) => { candidates.AddRange(batch); return Task.CompletedTask; }, cancellationToken);

        // 每个谱面集取最接近区间中位数的难度，避免「总是取最难」带来的偏差
        double center = (minStars + maxStars) / 2;
        List<OsuBeatmap> pool = [];
        foreach (OsuBeatmapset set in candidates)
        {
            OsuBeatmap? best = null;
            foreach (OsuBeatmap beatmap in set.Beatmaps)
            {
                if (!string.Equals(beatmap.Mode, settings.Mode, StringComparison.OrdinalIgnoreCase)) continue;
                if (settings.Mode == "mania" && (int)Math.Round(beatmap.CircleSize) != settings.ManiaKeys) continue;
                if (beatmap.DifficultyRating < minStars || beatmap.DifficultyRating > maxStars) continue;
                if (beatmap.TotalLength < settings.MinimumLengthSeconds || beatmap.TotalLength > settings.MaximumLengthSeconds) continue;
                if (best is null || Math.Abs(beatmap.DifficultyRating - center) < Math.Abs(best.DifficultyRating - center))
                    best = beatmap;
            }
            if (best is not null)
                pool.Add(best);
        }

        (string Label, double Low, double High)[] buckets =
        [
            ("<120", 0, 120), ("120-150", 120, 150), ("150-180", 150, 180),
            ("180-220", 180, 220), (">=220", 220, double.MaxValue)
        ];

        Dictionary<int, ManiaPattern?> cache = await loadTruthAsync(cancellationToken);
        int fetched = 0;

        StringBuilder report = new();
        report.AppendLine($"# BPM × 键型 实验 {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine($"区间 {minStars:0.0}-{maxStars:0.0}★ {settings.Mode} {settings.ManiaKeys}K，" +
                          $"候选 {candidates.Count} 个谱面集 / {pool.Count} 张可用，每桶最多 {perBucket} 张");
        report.AppendLine();

        List<(double Bpm, ManiaPattern Pattern)> measured = [];
        foreach ((string label, double low, double high) in buckets)
        {
            List<OsuBeatmap> slice = pool
                .Where(beatmap => beatmap.Bpm >= low && beatmap.Bpm < high)
                .OrderBy(beatmap => beatmap.Bpm)
                .ToList();
            int step = Math.Max(1, slice.Count / Math.Max(1, perBucket));
            List<OsuBeatmap> sample = slice.Where((_, index) => index % step == 0).Take(perBucket).ToList();

            List<ManiaPattern> patterns = [];
            foreach (OsuBeatmap beatmap in sample)
            {
                bool wasCached = cache.ContainsKey(beatmap.Id);
                ManiaPattern? pattern = await resolvePatternAsync(beatmap.Id, cache, cancellationToken);
                if (!wasCached)
                {
                    fetched++;
                    if (fetched % 25 == 0)
                        AppLog.Info($"实验：已分析 {fetched} 张…");
                }
                if (pattern is not null)
                {
                    patterns.Add(pattern);
                    measured.Add((beatmap.Bpm, pattern));
                }
            }

            if (patterns.Count == 0)
            {
                report.AppendLine($"{label,-8} 无样本");
                continue;
            }

            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-8} n={1,-3} BPM 均 {2,5:0}  Stream {3,5:P0}  Chordstream {4,5:P0}  " +
                "Jacks {5,5:P0}  Coordination {6,5:P0}  Wildcard {7,5:P0}  含长条 {8,4:P0}",
                label, patterns.Count, sample.Average(b => b.Bpm),
                patterns.Average(p => share(p, "Stream")),
                patterns.Average(p => share(p, "Chordstream")),
                patterns.Average(p => share(p, "Jacks")),
                patterns.Average(p => share(p, "Coordination")),
                patterns.Average(p => share(p, "Wildcard")),
                patterns.Average(p => p.LnRatio)));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "         主型：叠键 {0} 张（{1:P0}）／流系 {2} 张（{3:P0}）／其他 {4} 张，最高子型 Top3：{5}",
                patterns.Count(p => dominant(p) == "Jacks"),
                (double)patterns.Count(p => dominant(p) == "Jacks") / patterns.Count,
                patterns.Count(p => dominant(p) is "Stream" or "Chordstream"),
                (double)patterns.Count(p => dominant(p) is "Stream" or "Chordstream") / patterns.Count,
                patterns.Count(p => dominant(p) is not ("Jacks" or "Stream" or "Chordstream")),
                string.Join("、", patterns
                    .SelectMany(p => p.SpecificShares)
                    .GroupBy(pair => pair.Key)
                    .Select(group => (Key: group.Key, Value: group.Sum(pair => pair.Value)))
                    .OrderByDescending(pair => pair.Value)
                    .Take(3)
                    .Select(pair => pair.Key))));
        }

        if (measured.Count > 3)
        {
            report.AppendLine();
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "相关系数（n={0}）：BPM vs Stream {1:0.00}｜BPM vs Chordstream {2:0.00}｜" +
                "BPM vs Jacks {3:0.00}｜BPM vs 流系(Stream+Chordstream) {4:0.00}",
                measured.Count,
                pearson(measured, p => share(p, "Stream")),
                pearson(measured, p => share(p, "Chordstream")),
                pearson(measured, p => share(p, "Jacks")),
                pearson(measured, p => share(p, "Stream") + share(p, "Chordstream"))));
        }

        await saveTruthAsync(cache, cancellationToken);
        report.AppendLine();
        report.AppendLine($"真值缓存：{truthPath()}（{cache.Count} 条，本次新拉 {fetched} 张）");
        return report.ToString();
    }

    private static double pearson(
        IReadOnlyList<(double Bpm, ManiaPattern Pattern)> data,
        Func<ManiaPattern, double> selector)
    {
        double[] xs = data.Select(item => item.Bpm).ToArray();
        double[] ys = data.Select(item => selector(item.Pattern)).ToArray();
        double mx = xs.Average(), my = ys.Average();
        double covariance = 0, vx = 0, vy = 0;
        for (int index = 0; index < xs.Length; index++)
        {
            double dx = xs[index] - mx, dy = ys[index] - my;
            covariance += dx * dy;
            vx += dx * dx;
            vy += dy * dy;
        }
        double denominator = Math.Sqrt(vx * vy);
        return denominator <= 0 ? 0 : covariance / denominator;
    }

    private static string truthPath() => Path.Combine(StoragePaths.Root, "pattern-truth.json");

    private static async Task<Dictionary<int, ManiaPattern?>> loadTruthAsync(CancellationToken cancellationToken)
    {
        string path = truthPath();
        if (!File.Exists(path))
            return [];
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, ManiaPattern?>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task saveTruthAsync(Dictionary<int, ManiaPattern?> cache, CancellationToken cancellationToken)
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(cache,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(truthPath(), json, cancellationToken);
        }
        catch (Exception exception)
        {
            AppLog.Error("实验：键型真值缓存写入失败", exception);
        }
    }

    private static async Task<ManiaPattern?> resolvePatternAsync(
        int beatmapId, Dictionary<int, ManiaPattern?> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(beatmapId, out ManiaPattern? cached))
            return cached;

        ManiaPattern? pattern = null;
        try
        {
            string text = await download.GetStringAsync($"https://osu.ppy.sh/osu/{beatmapId}", cancellationToken);
            if (text.Contains("[HitObjects]", StringComparison.Ordinal))
                pattern = ManiaPatternAnalyzer.Analyze(text);
        }
        catch (Exception exception)
        {
            AppLog.Info($"实验：拉 .osu 失败 {beatmapId}：{exception.Message}");
        }

        cache[beatmapId] = pattern;
        return pattern;
    }

    private static double share(ManiaPattern pattern, string name) =>
        pattern.CoreShares.TryGetValue(name, out double value) ? value : 0;

    private static string topSpecific(ManiaPattern pattern)
    {
        KeyValuePair<string, double>? top = pattern.SpecificShares
            .OrderByDescending(pair => pair.Value)
            .Cast<KeyValuePair<string, double>?>()
            .FirstOrDefault();
        return top is null ? "—" : $"{top.Value.Key} {top.Value.Value:P0}";
    }

    /// <summary>按主标签（流 / 和弦流 / 叠键 / 协调 / 密度 / 杂项）统计次数与平均占比。</summary>
    private static string Distribution(string label, IReadOnlyList<ManiaPattern?> patterns)
    {
        List<ManiaPattern> known = patterns.Where(pattern => pattern is not null).Select(pattern => pattern!).ToList();
        if (known.Count == 0)
            return $"{label}：没有拿到键型数据";

        string[] names = ["Stream", "Chordstream", "Jacks", "Coordination", "Density", "Wildcard"];
        StringBuilder line = new();
        line.Append(CultureInfo.InvariantCulture, $"{label}（{known.Count} 张）");
        foreach (string name in names)
        {
            int primary = known.Count(pattern => dominant(pattern).Equals(name, StringComparison.OrdinalIgnoreCase));
            double average = known.Average(pattern => share(pattern, name));
            if (primary == 0 && average < 0.02)
                continue;
            line.Append(CultureInfo.InvariantCulture, $"  {name} 主型 {primary} 张/均 {average:P0}");
        }

        int streamFamily = known.Count(pattern =>
            dominant(pattern) is "Stream" or "Chordstream");
        line.Append(CultureInfo.InvariantCulture, $"  ｜流系（Stream+Chordstream）主型 {streamFamily}/{known.Count}");
        return line.ToString();
    }

    private static string dominant(ManiaPattern pattern) =>
        pattern.CoreShares.Count == 0
            ? "—"
            : pattern.CoreShares.OrderByDescending(pair => pair.Value).First().Key;
}
