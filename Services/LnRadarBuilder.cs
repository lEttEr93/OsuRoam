using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 信息窗里叠在七轴雷达上的 LN 三轴三角。
///
/// 口径（1.5.3 定稿）：
/// 1. 三角的**形状**来自三个 LN 核心型：协调（正下）/ 密度（右上）/ 杂项（左上），
///    各自按「除以三者中最大的那个」归一，所以最大那根永远等于整体缩放系数；
/// 2. 三角的**大小**来自长条的难度贡献：ΔSR/SR —— 原谱星数减去「长条压成单点」后的星数，
///    再除以原谱星数。原谱星数直接用 osu! API 给的值，删长条后的星数由
///    <see cref="ManiaStarRating"/> 在本地算；
/// 3. 锚点 <see cref="Anchor"/> = 0.30：实测本机纯面谱的 ΔSR/SR 最大 30.6%、中位 14.5%，
///    取 0.30 意味着「最难的面谱刚好填满」，中位面谱落在半格左右。超过锚点按满格截断；
/// 4. 缩放取**开方**：ΔSR/SR 的分布极度偏科（LN 20–40% 那批中位只有 0.8%），线性映射会让
///    绝大多数面谱缩成一个点。开方不改排序，只把低处抬起来 —— 3.3% 从 11% 半径变成 33%。
///
/// 顺序固定为「正下 / 右上 / 左上」，与 <see cref="Controls.RadarChart"/> 的叠加层朝向一致。
/// </summary>
public static class LnRadarBuilder
{
    public const int AxisCount = 3;

    /// <summary>ΔSR/SR 到多少算填满外接圆。</summary>
    public const double Anchor = 0.30;

    /// <summary>
    /// 三个顶点长度（0–1，占雷达半径）：正下 = 协调，右上 = 密度，左上 = 杂项。
    /// 缺少数据（没有长条、没算过米线星数、官方星数缺失）时返回 null —— 不画三角。
    /// </summary>
    public static double[]? Build(ManiaPattern? pattern, double officialStarRating)
    {
        if (pattern is null || officialStarRating <= 0 || pattern.RiceStarRating <= 0)
            return null;

        double coordination = shareOf(pattern, "Coordination");
        double density = shareOf(pattern, "Density");
        double wildcard = shareOf(pattern, "Wildcard");

        double max = Math.Max(coordination, Math.Max(density, wildcard));
        if (max <= 0.0001)
            return null;

        double delta = officialStarRating - pattern.RiceStarRating;
        if (delta <= 0)
            return null;

        double ratio = delta / officialStarRating;
        double scale = Math.Sqrt(Math.Min(1, ratio / Anchor));

        return
        [
            Math.Clamp(coordination / max * scale, 0, 1),
            Math.Clamp(density / max * scale, 0, 1),
            Math.Clamp(wildcard / max * scale, 0, 1)
        ];
    }

    private static double shareOf(ManiaPattern pattern, string name) =>
        pattern.CoreShares.TryGetValue(name, out double value) ? Math.Max(0, value) : 0;
}
