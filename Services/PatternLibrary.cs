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
    private readonly string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuRoam");

    private Dictionary<int, ManiaPattern> patterns = [];
    private bool dirty;

    private string FilePath => Path.Combine(directory, "patterns.json");

    public int Count => patterns.Count;

    public int ParsedCount => patterns.Values.Count(pattern => !pattern.IsInferred);

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            await using FileStream stream = File.OpenRead(FilePath);
            patterns = await JsonSerializer.DeserializeAsync<Dictionary<int, ManiaPattern>>(stream) ?? [];
        }
        catch (Exception exception)
        {
            AppLog.Error("读取键型库失败", exception);
            patterns = [];
        }
    }

    public async Task SaveAsync()
    {
        if (!dirty)
            return;
        try
        {
            Directory.CreateDirectory(directory);
            await using FileStream stream = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(stream, patterns, new JsonSerializerOptions { WriteIndented = false });
            dirty = false;
        }
        catch (Exception exception)
        {
            AppLog.Error("保存键型库失败", exception);
        }
    }

    public ManiaPattern? TryGet(int beatmapId) =>
        beatmapId > 0 && patterns.TryGetValue(beatmapId, out ManiaPattern? pattern) ? pattern : null;

    /// <summary>同谱面若已有真实键型，则不被 tag 推断覆盖。</summary>
    public void Put(ManiaPattern pattern)
    {
        if (pattern.BeatmapId <= 0)
            return;
        if (patterns.TryGetValue(pattern.BeatmapId, out ManiaPattern? existing) && !existing.IsInferred && pattern.IsInferred)
            return;
        patterns[pattern.BeatmapId] = pattern;
        dirty = true;
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
    public bool TryIngestOsz(string oszPath, int beatmapId, int beatmapsetId)
    {
        if (!File.Exists(oszPath))
            return false;
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(oszPath);
            ZipArchiveEntry? entry = archive.Entries
                .Where(item => item.FullName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(item => beatmapId <= 0 || item.FullName.Contains($"[{beatmapId}]", StringComparison.Ordinal));
            if (entry is null)
                return false;

            using StreamReader reader = new(entry.Open());
            ManiaPattern? pattern = ManiaPatternAnalyzer.Analyze(reader.ReadToEnd(), beatmapId, beatmapsetId);
            if (pattern is null)
                return false;

            Put(pattern);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error($"解析谱面键型失败：{Path.GetFileName(oszPath)}", exception);
            return false;
        }
    }

    /// <summary>候选谱面的键型：优先真实数据，退回 tag 推断（推断结果不入库，避免污染库）。</summary>
    public ManiaPattern? Resolve(int beatmapId, int beatmapsetId, IReadOnlyList<string> tags, int keys)
    {
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

            ManiaPattern? pattern = library.TryGet(beatmap.Id) ?? library.TryGet(beatmap.BeatmapsetId);
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
