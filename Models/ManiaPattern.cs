using System.Text.Json.Serialization;

namespace OsuRoam.Models;

/// <summary>
/// 一张 mania 谱面的键型指纹。分类体系沿用 osumania_map_analyser（MIT）：
/// 6 个核心型 + 各键数的子型 + RC/LN/Mix/HB 模式标签。
/// </summary>
public sealed class ManiaPattern
{
    public int BeatmapId { get; set; }
    public int BeatmapsetId { get; set; }
    public int KeyCount { get; set; }
    public int TotalNotes { get; set; }
    public int TotalHolds { get; set; }
    public double NotesPerSecond { get; set; }
    public double ChartsDurationMs { get; set; }

    /// <summary>长条占比：LN 音符 / 全部音符，决定 RC/LN 模式标签。</summary>
    public double LnRatio { get; set; }

    /// <summary>同一行同时有长条头和普通音符的比例，用于区分 HB（混合）谱。</summary>
    public double HbRowRatio { get; set; }

    /// <summary>RC / LN / Mix / HB。</summary>
    public string ModeTag { get; set; } = "Mix";

    /// <summary>主导分类，如 "Jumpstream Tech"。</summary>
    public string Category { get; set; } = "Uncategorised";

    /// <summary>变速强度（非 1 的 SV 点数量），>0 即为有 SV。</summary>
    public double SvAmount { get; set; }

    /// <summary>
    /// 「长条压成单点」之后的星数（本地算，见 <see cref="Services.ManiaStarRating"/>）。
    /// 原谱星数用 osu! API 给的值，两者之差除以原谱星数就是长条贡献的难度占比 —— 信息窗三角的大小。
    /// 0 表示还没算过（旧缓存），此时不画三角。
    /// </summary>
    public double RiceStarRating { get; set; }

    /// <summary>
    /// 变速（SV）评估：等时性 / 舒适度 / 背谱 / 强度 四个量的原始值。
    /// 口径见 <see cref="Services.SvEvaluator"/>；null 表示没算（旧缓存或非 mania）。
    /// </summary>
    public SvMetrics? Sv { get; set; }

    /// <summary>核心型时长占比（0–1，按匹配时长归一）。</summary>
    public Dictionary<string, double> CoreShares { get; set; } = [];

    /// <summary>子型时长占比，键名与 MMA 一致：Jumpstream / Handstream / Minijacks …</summary>
    public Dictionary<string, double> SpecificShares { get; set; } = [];

    /// <summary>匹配区间里节奏不稳定的比例，越高越"tech"。</summary>
    public double MixedShare { get; set; }

    public PatternSource Source { get; set; } = PatternSource.Osu;

    /// <summary>键型分析器的版本号；升级分析算法时递增，旧缓存按「过期」处理。</summary>
    public int AnalyzerVersion { get; set; }

    public DateTimeOffset AnalyzedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool IsInferred => Source == PatternSource.Tags;

    /// <summary>核心型固定顺序，向量与权重都按这个顺序对齐。</summary>
    /// <summary>
    /// 「面谱」门槛：长条占比到这个数就算 LN 向的谱面。
    /// 1.5.3 起从 0.25 降到 0.20 —— 同一个标准同时管筛选（勾「只要面谱」/ 准入 / 预取）
    /// 和显示（信息窗是否叠 LN 三轴雷达）。
    /// </summary>
    public const double LnChartThreshold = 0.20;

    public static readonly string[] CorePatterns =
    [
        "Stream", "Chordstream", "Jacks", "Coordination", "Density", "Wildcard"
    ];

    public static readonly string[] CorePatternLabels =
    [
        "流", "和弦流", "叠键", "协调", "密度", "变速/杂项"
    ];

    /// <summary>雷达图标签：控制在 2–3 个字，既看得懂又能在小图里放下。</summary>
    public static readonly string[] CorePatternShortLabels =
    [
        "流", "和弦流", "叠键", "协调", "密度", "杂项"
    ];

    public static readonly string[] SkillPatternShortLabels =
    [
        "流", "跳流", "交互", "耐力", "叠速", "叠和弦", "技巧"
    ];

    /// <summary>
    /// 七轴技能画像，轴名沿用 Etterna 的七个 skillset，数值由本地谱面数据推导，
    /// **不是 MinaCalc 结果**，只用于看谱面的难度倾向形状。
    /// </summary>
    public double[] SkillAxes { get; set; } = [];

    /// <summary>技能轴来源：MinaCalc 真值，或本地估算。</summary>
    public string SkillSource { get; set; } = "估算";

    public static readonly string[] SkillPatterns =
    [
        "Stream", "Jumpstream", "Handstream", "Stamina", "JackSpeed", "Chordjack", "Technical"
    ];

    public static readonly string[] SkillPatternLabels =
    [
        "流", "跳流", "交互", "耐力", "叠速", "叠和弦", "技巧"
    ];

    /// <summary>6 维核心型占比向量，缺失的按 0 处理。</summary>
    public double[] ToVector() => CorePatterns
        .Select(name => CoreShares.TryGetValue(name, out double share) ? share : 0)
        .ToArray();

    /// <summary>是不是面谱（长条占比过 <see cref="LnChartThreshold"/>）。</summary>
    public bool IsLnChart => LnRatio >= LnChartThreshold;

    /// <summary>勾选的键型在这张谱里占的份额（0–1）；按实际出现的核心型归一。</summary>
    public double SelectedShare(IReadOnlyList<string> selected)
    {
        if (selected.Count == 0)
            return 0;

        double total = 0;
        double hit = 0;
        foreach ((string name, double share) in CoreShares)
        {
            total += share;
            if (selected.Contains(name, StringComparer.OrdinalIgnoreCase))
                hit += share;
        }
        return total <= 0 ? 0 : Math.Max(0, Math.Min(1, hit / total));
    }

    /// <summary>两个键型指纹的相似度，0–1；模式标签不同会先打折。</summary>
    public double SimilarityTo(ManiaPattern other)
    {
        double[] left = ToVector();
        double[] right = other.ToVector();
        double diff = 0;
        for (int index = 0; index < left.Length; index++)
            diff += Math.Abs(left[index] - right[index]);

        double similarity = Math.Max(0, 1 - diff / left.Length);
        similarity *= 1 - Math.Min(0.4, Math.Abs(LnRatio - other.LnRatio) * 0.4);
        return similarity;
    }
}

public enum PatternSource
{
    Osu,
    Tags
}
