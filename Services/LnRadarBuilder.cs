using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 信息窗里叠在七轴雷达上的 LN 三轴三角。
///
/// 口径（1.5.3 定稿）：
/// 1. 三个核心型份额在三角内部按三者之和归一，显示成百分比，和 = 100%；
/// 2. 顶点长度 = (份额 / 最大份额) × f(LN)，最大那根正好等于 f(LN)，所以形状不受绝对量影响；
/// 3. f(LN) = 2·LN/(1+LN) —— 由「长条尾按 200 计分」的 pp 损失 1.875·LN/(1+LN)
///    按 LN = 100% 归一化得到，于是 LN = 100% 且三项等分时三个顶点都贴到外接圆。
///
/// 顺序固定为「正下 / 右上 / 左上」，与 <see cref="Controls.RadarChart"/> 的叠加层朝向一致。
/// </summary>
public static class LnRadarBuilder
{
    public const int AxisCount = 3;

    /// <summary>LN 强度系数：LN = 0 → 0，LN = 100% → 1。</summary>
    public static double Peak(double lnRatio)
    {
        double clamped = Math.Clamp(lnRatio, 0, 1);
        return 2 * clamped / (1 + clamped);
    }

    /// <summary>
    /// 三个顶点长度（0–1，占雷达半径）：正下 = 协调，右上 = 密度，左上 = 杂项。
    /// 三项全为零、或者 LN 占比为 0 时返回 null —— 没有可画的东西。
    /// </summary>
    public static double[]? Build(ManiaPattern? pattern)
    {
        if (pattern is null)
            return null;

        double coordination = shareOf(pattern, "Coordination");
        double density = shareOf(pattern, "Density");
        double wildcard = shareOf(pattern, "Wildcard");

        double max = Math.Max(coordination, Math.Max(density, wildcard));
        if (max <= 0.0001)
            return null;

        double peak = Peak(pattern.LnRatio);
        if (peak <= 0)
            return null;

        return
        [
            Math.Clamp(coordination / max * peak, 0, 1),
            Math.Clamp(density / max * peak, 0, 1),
            Math.Clamp(wildcard / max * peak, 0, 1)
        ];
    }

    private static double shareOf(ManiaPattern pattern, string name) =>
        pattern.CoreShares.TryGetValue(name, out double value) ? Math.Max(0, value) : 0;
}
