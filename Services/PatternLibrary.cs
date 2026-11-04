using System.IO;
using System.IO.Compression;
using System.Text.Json;
using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 键型库：按 beatmap id 缓存真实解析出来的键型，以及在没有 .osu 时用 tag 做的粗推断。
/// 数据落在 %LOCALAPPDATA%\OsuRoam\patterns.json，越玩越准。
/// </summary>
public sealed class PatternLibrary
{
    private readonly string directory = StoragePaths.Root;

    /// <summary>多个预取任务会并发 Ingest，字典本身不是线程安全的。</summary>
    private readonly object gate = new();
    private Dictionary<int, ManiaPattern> patterns = [];
    private bool dirty;
    private int revision;

    private string FilePath => Path.Combine(directory, "patterns.json");

    public int Count
    {
        get
        {
            lock (gate)
                return patterns.Count;
        }
    }

    public int ParsedCount
    {
        get
        {
            lock (gate)
                return patterns.Values.Count(pattern => !pattern.IsInferred);
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            await using FileStream stream = File.OpenRead(FilePath);
            Dictionary<int, ManiaPattern> loaded =
                await JsonSerializer.DeserializeAsync<Dictionary<int, ManiaPattern>>(stream) ?? [];
            lock (gate)
                patterns = loaded;
        }
        catch (Exception exception)
        {
            AppLog.Error("读取键型库失败", exception);
            lock (gate)
                patterns = [];
        }
    }

    public async Task SaveAsync()
    {
        Dictionary<int, ManiaPattern> snapshot;
        int snapshotRevision;
        lock (gate)
        {
            if (!dirty)
                return;
            snapshot = new Dictionary<int, ManiaPattern>(patterns);
            snapshotRevision = revision;
        }

        try
        {
            Directory.CreateDirectory(directory);
            // 先写临时文件再原子替换：保存期间新写入的结果不会写出半截文件，也不会被 dirty=false 吞掉。
            string temp = FilePath + ".tmp";
            await using (FileStream stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, snapshot, new JsonSerializerOptions { WriteIndented = false });
            File.Move(temp, FilePath, true);
            lock (gate)
            {
                if (revision == snapshotRevision)
                    dirty = false;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("保存键型库失败", exception);
        }
    }

    public ManiaPattern? TryGet(int beatmapId)
    {
        if (beatmapId <= 0)
            return null;
        lock (gate)
            return patterns.TryGetValue(beatmapId, out ManiaPattern? pattern) ? pattern : null;
    }

    /// <summary>
    /// 「已可信」的键型：真实解析、且分析器版本与当前一致。
    /// tag 推断和旧版本缓存都不算 —— 它们只配当调度线索，不能当推荐依据。
    /// </summary>
    public ManiaPattern? TryGetVerified(int beatmapId)
    {
        ManiaPattern? pattern = TryGet(beatmapId);
        if (pattern is null || pattern.IsInferred || pattern.AnalyzerVersion != ManiaPatternAnalyzer.Version)
            return null;
        return pattern;
    }

    /// <summary>同谱面若已有真实键型，则不被 tag 推断覆盖。</summary>
    public void Put(ManiaPattern pattern)
    {
        if (pattern.BeatmapId <= 0)
            return;
        lock (gate)
        {
            if (patterns.TryGetValue(pattern.BeatmapId, out ManiaPattern? existing) &&
                !existing.IsInferred && pattern.IsInferred)
                return;
            patterns[pattern.BeatmapId] = pattern;
            dirty = true;
            revision += 1;
        }
    }

    public void PutInferred(int beatmapId, int beatmapsetId, IReadOnlyList<string> tags, int keys)
    {
        if (TryGet(beatmapId) is { IsInferred: false })
            return;
        if (PatternTags.Infer(tags, keys) is not { } inferred)
            return;
        inferred.BeatmapId = beatmapId;
        inferred.BeatmapsetId = beatmapsetId;
        Put(inferred);
    }

    /// <summary>从下载好的 .osz 里直接解析目标谱面的 .osu 并入库。</summary>
    public bool TryIngestOsz(string oszPath, int beatmapId, int beatmapsetId) =>
        OszReader.TryReadBeatmapText(oszPath, beatmapId, out string text, out string entryName) &&
        Ingest(text, beatmapId, beatmapsetId, entryName);

    /// <summary>
    /// 已经拿到 .osu 文本时直接入库。导入流程会把同一份文本先交给
    /// <see cref="Models.BeatmapDetailReader"/>（CS/OD/物件数），再交到这里解析键型，
    /// 免得多解压一遍压缩包。
    /// </summary>
    public bool Ingest(string osuText, int beatmapId, int beatmapsetId, string entryName = "")
    {
        try
        {
            ManiaPattern? pattern = ManiaPatternAnalyzer.Analyze(osuText, beatmapId, beatmapsetId);
            if (pattern is null)
            {
                AppLog.Error($"键型解析返回空（键数或音符不足）：{entryName}");
                return false;
            }

            AppLog.Info($"键型入库：set={beatmapsetId} beatmap={beatmapId} {pattern.KeyCount}K {pattern.Category}" +
                        (entryName.Length == 0 ? string.Empty : $"（{entryName}）"));
            Put(pattern);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("解析谱面键型失败", exception);
            return false;
        }
    }

    /// <summary>候选谱面的键型：优先真实数据，退回 tag 推断（推断结果不入库，避免污染库）。</summary>
    /// <param name="verifiedOnly">
    /// 只用「真实解析 + 当前版本」的结果。勾选键型 / 只要面谱时走这条：
    /// tag 推断出来的占比会让「猜中的」和「真中的」拿同样的分，等于拿猜测当结论。
    /// </param>
    public ManiaPattern? Resolve(int beatmapId, int beatmapsetId, IReadOnlyList<string> tags, int keys, bool verifiedOnly = false)
    {
        if (verifiedOnly)
            return TryGetVerified(beatmapId);

        if (TryGet(beatmapId) is { } known)
            return known;

        ManiaPattern? inferred = PatternTags.Infer(tags, keys);
        if (inferred is null)
            return null;
        inferred.BeatmapId = beatmapId;
        inferred.BeatmapsetId = beatmapsetId;
        return inferred;
    }
}

/// <summary>
/// 没有 .osu 时，用谱面 tag 粗略估计 6 个核心型的占比。
/// 只为让「键型偏好」在刚装上、还没下载过谱面时也有信号；真实值由分析器覆盖。
/// </summary>
public static class PatternTags
{
    public static ManiaPattern? Infer(IReadOnlyList<string> tags, int keys)
    {
        if (tags.Count == 0)
            return null;

        Dictionary<string, double> scores = new(StringComparer.OrdinalIgnoreCase);
        double ln = 0;
        foreach (string tag in tags)
        {
            switch (tag)
            {
                case "ln" or "long note" or "长条" or "面条":
                    ln += 1;
                    add("Density", 1);
                    add("Coordination", 1);
                    break;
                case "rice" or "米" or "stream" or "流":
                    add("Stream", 1);
                    break;
                case "jack" or "叠" or "minijack":
                    add("Jacks", 1);
                    break;
                case "chordjack" or "叠和弦":
                    add("Jacks", 1);
                    add("Chordstream", 0.5);
                    break;
                case "handstream" or "交互" or "jumpstream" or "和弦流" or "技":
                    add("Chordstream", 1);
                    break;
                case "tech" or "技巧" or "乱" or "切":
                    add("Wildcard", 1);
                    break;
                case "sv" or "变速" or "scroll":
                    add("Wildcard", 0.5);
                    break;
            }
        }

        if (scores.Count == 0)
            return null;

        double total = scores.Values.Sum();
        return new ManiaPattern
        {
            KeyCount = keys,
            LnRatio = Math.Min(1, ln / Math.Max(1, tags.Count)),
            ModeTag = ln > 0 ? "Mix" : "RC",
            CoreShares = scores.ToDictionary(pair => pair.Key, pair => Math.Round(pair.Value / total, 4)),
            Source = PatternSource.Tags
        };

        void add(string pattern, double value)
        {
            scores[pattern] = scores.TryGetValue(pattern, out double current) ? current + value : value;
        }
    }
}

/// <summary>从玩家实际打过的谱面反推键型偏好向量。</summary>
public static class PatternPreference
{
    /// <summary>
    /// 取同键数的 recent + best 成绩，优先用键型库里的真实数据，
    /// 没有就用 tag 推断。返回 6 维平均向量；样本不足时返回 null。
    /// </summary>
    public static double[]? Build(PlayerProfile profile, PatternLibrary library, int keys)
    {
        List<double[]> vectors = [];
        foreach (OsuScore score in profile.RecentScores.Concat(profile.BestScores))
        {
            OsuBeatmap? beatmap = score.Beatmap;
            if (beatmap is null)
                continue;
            if (keys > 0 && (int)Math.Round(beatmap.CircleSize) != keys)
                continue;

            // 只按 beatmapId 查：密钥空间是 beatmapId，混用 beatmapsetId 会读到别的谱面。
            // 查不到时用该谱面集的 tag 推断，比直接读错一条更安全。
            ManiaPattern? pattern = library.TryGet(beatmap.Id);
            if (pattern is null && score.Beatmapset is { } set)
                pattern = PatternTags.Infer(AppSettings.ParseTags(set.Tags).ToList(), keys);
            if (pattern is null)
                continue;

            vectors.Add(pattern.ToVector());
        }

        if (vectors.Count < 3)
            return null;

        double[] average = new double[ManiaPattern.CorePatterns.Length];
        foreach (double[] vector in vectors)
        {
            for (int index = 0; index < average.Length; index++)
                average[index] += vector[index];
        }
        for (int index = 0; index < average.Length; index++)
            average[index] /= vectors.Count;
        return average;
    }

    /// <summary>候选键型与偏好向量的余弦相似度，0–1。</summary>
    public static double Fit(ManiaPattern pattern, double[] preference)
    {
        double[] vector = pattern.ToVector();
        double dot = 0;
        double vectorNorm = 0;
        double preferenceNorm = 0;
        for (int index = 0; index < vector.Length; index++)
        {
            dot += vector[index] * preference[index];
            vectorNorm += vector[index] * vector[index];
            preferenceNorm += preference[index] * preference[index];
        }

        double denominator = Math.Sqrt(vectorNorm) * Math.Sqrt(preferenceNorm);
        return denominator <= 0 ? 0 : Math.Clamp(dot / denominator, 0, 1);
    }
}
