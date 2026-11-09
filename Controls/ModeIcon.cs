using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OsuRoam.Controls;

/// <summary>四个玩法。值和 <see cref="Models.AppSettings.Mode"/> 一致（catch 存的是 fruits）。</summary>
public enum ModeIconKind
{
    Osu,
    Taiko,
    Catch,
    Mania,
}

/// <summary>
/// 四个玩法的图标。官方那套是品牌图形，不在仓库里转存，这里按同样的构成画矢量版：
/// osu! = 靶环 + 靶心，taiko = 鼓面双环，catch = 带叶的果子，mania = 4 根琴键。
///
/// 用 <see cref="Control.Foreground"/> 上色，所以「选中/未选中」只要换按钮的前景色，
/// 既不用准备四套位图，也不会在缩放时糊掉。
/// </summary>
public sealed class ModeIcon : Control
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(ModeIconKind), typeof(ModeIcon),
        new FrameworkPropertyMetadata(ModeIconKind.Osu, FrameworkPropertyMetadataOptions.AffectsRender));

    public ModeIconKind Mode
    {
        get => (ModeIconKind)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Foreground 是继承属性，只改它不会让 OnRender 重跑，得自己叫一次重画。
        if (e.Property == ForegroundProperty)
            InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 6)
            return;

        Brush brush = Foreground ?? Brushes.Gray;
        double left = (ActualWidth - size) / 2;
        double top = (ActualHeight - size) / 2;
        // 全部按 0..1 的格子画，随控件尺寸缩放。
        Point P(double x, double y) => new(left + x * size, top + y * size);
        double U(double value) => value * size;
        Pen Ring(double thickness) => new(brush, U(thickness))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        switch (Mode)
        {
            case ModeIconKind.Osu:
                dc.DrawEllipse(null, Ring(0.10), P(0.5, 0.5), U(0.40), U(0.40));
                dc.DrawEllipse(brush, null, P(0.5, 0.5), U(0.185), U(0.185));
                break;

            case ModeIconKind.Taiko:
                // 鼓面：外圈鼓边 + 内圈鼓面。和 osu! 的靶环区分在于没有实心靶心。
                dc.DrawEllipse(null, Ring(0.10), P(0.5, 0.5), U(0.40), U(0.40));
                dc.DrawEllipse(null, Ring(0.075), P(0.5, 0.5), U(0.205), U(0.205));
                break;

            case ModeIconKind.Catch:
                dc.DrawEllipse(brush, null, P(0.45, 0.63), U(0.29), U(0.29));
                // 果柄
                Pen stem = new(brush, U(0.085))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                dc.DrawLine(stem, P(0.57, 0.40), P(0.65, 0.24));
                // 叶子：绕叶心转一点再画椭圆
                dc.PushTransform(new RotateTransform(-28, left + U(0.74), top + U(0.23)));
                dc.DrawEllipse(brush, null, P(0.74, 0.23), U(0.17), U(0.075));
                dc.Pop();
                break;

            case ModeIconKind.Mania:
                // 四根琴键，中间两根高一点，跟官方那个「拱形」轮廓一致。
                DrawBar(dc, brush, P(0.07, 0.42), U(0.16), U(0.52), U(0.05));
                DrawBar(dc, brush, P(0.30, 0.12), U(0.16), U(0.82), U(0.05));
                DrawBar(dc, brush, P(0.53, 0.12), U(0.16), U(0.82), U(0.05));
                DrawBar(dc, brush, P(0.76, 0.42), U(0.16), U(0.52), U(0.05));
                break;
        }
    }

    private static void DrawBar(DrawingContext dc, Brush brush, Point corner, double width, double height, double radius) =>
        dc.DrawRoundedRectangle(brush, null, new Rect(corner.X, corner.Y, width, height), radius, radius);
}
