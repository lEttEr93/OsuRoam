using System.Text.Json.Serialization;

namespace OsuRoam.Models;

/// <summary>
/// 一张谱面的变速（SV）评估结果，口径见 <see cref="Services.SvEvaluator"/> 与
/// <c>docs/1.5.3-SV评估算法.md</c>。
///
/// 拆成四个独立的量，不合成一个分数 —— 单一分数在 Star Stream 与 HOTTO DOGU 上会把顺序判反：
/// 前者每颗音符匀速但基线跳 10 次，后者结构稳但变速幅度大。
/// </summary>
public sealed class SvMetrics
{
    /// <summary>
    /// A：结构上的「不等时」（0 = 完全匀速滚动，1 = 一直在跳）。
    /// 由「基线切换频率」和「1 秒局部飞行时长带宽」合成，是**负向**量。
    /// </summary>
    public double Irregularity { get; set; }

    /// <summary>
    /// B：飞行时长偏离标称值有多远（0–1，±10% 容忍带内记 0）。也是**负向**量。
    /// </summary>
    public double Extremeness { get; set; }

    /// <summary>背谱度（0–1）：A² + (1−A)·B —— 结构差时 B 的边际贡献被压缩，正向量。</summary>
    public double Memorization { get; set; }

    /// <summary>不适度（0–1）：基线切换时按「幅度 × 当时在飞的音符数」累计，正向量。</summary>
    public double Discomfort { get; set; }

    /// <summary>夸张度：音符飞行途中相对自身平均速度的对数偏离积分（0 = 匀速飞行）。</summary>
    public double Exaggeration { get; set; }

    /// <summary>参与计算的时间行数（同一时刻的多个音符只算一行）。</summary>
    public int Rows { get; set; }

    /// <summary>标称飞行时长 C = 11485 / 流速（毫秒）。</summary>
    public double ScreenTimeMs { get; set; }

    /// <summary>飞行时长中位数（毫秒），诊断用。</summary>
    public double MedianFlightMs { get; set; }

    /// <summary>基线切换次数 / 分钟。</summary>
    public double SwitchesPerMinute { get; set; }

    /// <summary>1 秒局部带宽中位数（(p90−p10)/中位）。</summary>
    public double LocalWidth { get; set; }

    /// <summary>切换发生时屏幕上的待点音符数均值。</summary>
    public double SwitchNotesMean { get; set; }

    /// <summary>
    /// 界面用的「等时性」（0–1，越高越稳）：<see cref="Irregularity"/> 的反向。
    /// </summary>
    [JsonIgnore]
    public double Isochrony => 1 - Math.Clamp(Irregularity, 0, 1);

    /// <summary>界面用的「舒适度」（0–1，越高越舒服）：<see cref="Discomfort"/> 的反向。</summary>
    [JsonIgnore]
    public double Comfort => 1 - Math.Clamp(Discomfort, 0, 1);

    /// <summary>
    /// 界面用的「强度」（0–1，越高越剧烈）：<see cref="Exaggeration"/> 走 x/(x+1) 压到 0–1。
    /// 实测范围 0.02–4.6，双曲饱和把常见值（0.5–1.5）摊在中间，极端值也不会顶满。
    /// </summary>
    [JsonIgnore]
    public double Intensity
    {
        get
        {
            double value = Math.Max(0, Exaggeration);
            return value / (value + 1);
        }
    }

    /// <summary>四个界面量，顺序固定：等时性 / 舒适度 / 背谱 / 强度。</summary>
    public double[] DisplayValues() =>
        [Isochrony, Comfort, Math.Clamp(Memorization, 0, 1), Intensity];

    /// <summary>四个界面量里，「值越高越差」的是后两个（配色方向不同）。</summary>
    public static readonly bool[] HigherIsWorse = [false, false, true, true];
}
