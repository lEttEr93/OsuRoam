using System.Windows.Media;

namespace OsuRoam.Controls;

/// <summary>
/// 星级的颜色标尺，取自 osu! 的 difficulty colour spectrum：
/// 2★ 以下是蓝的，4★ 转绿，5★ 黄，6★ 橙红，8★ 紫，10★ 深蓝。
///
/// 用途是让「6.42★」这个数字一眼能看出难度档位，比单纯白字信息量大。
/// </summary>
public static class DifficultyColour
{
    private static readonly (double Stars, Color Colour)[] Stops =
    [
        (0.0, Color.FromRgb(0x42, 0x90, 0xFB)),
        (1.5, Color.FromRgb(0x4F, 0xC0, 0xFF)),
        (2.5, Color.FromRgb(0x4F, 0xFF, 0xD5)),
        (3.5, Color.FromRgb(0x7C, 0xFF, 0x4F)),
        (4.5, Color.FromRgb(0xF6, 0xF0, 0x5C)),
        (5.5, Color.FromRgb(0xFF, 0x80, 0x68)),
        (6.5, Color.FromRgb(0xFF, 0x4E, 0x6F)),
        (7.5, Color.FromRgb(0xC6, 0x45, 0xB8)),
        (8.5, Color.FromRgb(0x65, 0x63, 0xDE)),
        (10.0, Color.FromRgb(0x18, 0x15, 0x8E))
    ];

    /// <summary>NaN / 负数等异常输入统一给一个中性灰，避免出现透明文字。</summary>
    private static readonly Color Unknown = Color.FromRgb(0x6D, 0x78, 0x94);

    public static Color Sample(double stars)
    {
        if (!double.IsFinite(stars) || stars < 0)
            return Unknown;

        if (stars <= Stops[0].Stars)
            return Stops[0].Colour;
        if (stars >= Stops[^1].Stars)
            return Stops[^1].Colour;

        for (int index = 0; index < Stops.Length - 1; index++)
        {
            (double leftStars, Color leftColour) = Stops[index];
            (double rightStars, Color rightColour) = Stops[index + 1];
            if (stars < leftStars || stars > rightStars)
                continue;

            double t = (stars - leftStars) / Math.Max(0.0001, rightStars - leftStars);
            return Color.FromRgb(
                blend(leftColour.R, rightColour.R, t),
                blend(leftColour.G, rightColour.G, t),
                blend(leftColour.B, rightColour.B, t));
        }

        return Stops[^1].Colour;
    }

    /// <summary>给 WPF 用的冻结画刷（每次新建，调用方不必管线程安全）。</summary>
    public static SolidColorBrush Brush(double stars)
    {
        SolidColorBrush brush = new(Sample(stars));
        brush.Freeze();
        return brush;
    }

    private static byte blend(byte from, byte to, double t) =>
        (byte)Math.Clamp(Math.Round(from + (to - from) * t), 0, 255);
}
