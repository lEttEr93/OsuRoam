using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OsuRoam.Controls;

/// <summary>
/// 极简雷达图，6 或 7 轴通用。
/// 关键点：
/// 1) 几何只用 ActualWidth/ActualHeight，不使用固定尺寸；
/// 2) 文字按当前显示器 DPI 测量（pixelsPerDip），否则缩放屏上标签会错位；
/// 3) 标签带宽度由实际文字尺寸推导，多边形半径随之收敛，顶点与标签都不会越界。
/// </summary>
public sealed class RadarChart : FrameworkElement
{
    private const double LabelFontSize = 9;
    private const double LabelGap = 4;
    private const double EdgePadding = 2;
    private const double PreferredSize = 120;

    private static readonly Brush GridBrush = freeze(new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x3E)));
    private static readonly Brush AxisBrush = freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x34)));
    private static readonly Brush LabelBrush = freeze(new SolidColorBrush(Color.FromRgb(0xA5, 0xAD, 0xB3)));
    private static readonly Brush ValueBrush = freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x44, 0xB7, 0xAE)));
    private static readonly Pen ValuePen = freeze(new Pen(freeze(new SolidColorBrush(Color.FromRgb(0x44, 0xB7, 0xAE))), 1.4));
    private static readonly Pen RingPen = freeze(new Pen(GridBrush, 0.6));
    private static readonly Pen AxisPen = freeze(new Pen(AxisBrush, 0.5));

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(RadarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(RadarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<string>? Labels
    {
        get => (IReadOnlyList<string>?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public RadarChart()
    {
        ClipToBounds = true;
        Focusable = false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? PreferredSize : Math.Min(PreferredSize, availableSize.Width);
        double height = double.IsInfinity(availableSize.Height) ? PreferredSize : Math.Min(PreferredSize, availableSize.Height);
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 4 || height <= 4)
            return;

        IReadOnlyList<string>? labels = Labels;
        IReadOnlyList<double>? values = Values;
        int count = labels?.Count ?? 0;
        if (count < 3 || values is null || values.Count < count)
        {
            drawCentered(context, "暂无键型数据", width, height);
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText[] texts = new FormattedText[count];
        double maxTextWidth = 0;
        double maxTextHeight = 0;
        for (int index = 0; index < count; index++)
        {
            texts[index] = format(labels![index] ?? string.Empty, pixelsPerDip);
            maxTextWidth = Math.Max(maxTextWidth, texts[index].WidthIncludingTrailingWhitespace);
            maxTextHeight = Math.Max(maxTextHeight, texts[index].Height);
        }

        // 预留带按真实文字尺寸推导，而不是写死的常数。
        // 多字标签按整段文字宽度收敛半径：水平方向最宽的那个标签也必须放得下
        double labelRoom = Math.Max(maxTextWidth, maxTextHeight) + LabelGap + EdgePadding;
        Point center = new(width / 2, height / 2);
        double radius = Math.Min(width, height) / 2 - labelRoom;
        if (radius <= 2)
        {
            drawCentered(context, "空间不足", width, height);
            return;
        }

        for (int ring = 1; ring <= 4; ring++)
            context.DrawGeometry(null, RingPen, polygon(center, radius * ring / 4.0, count));

        for (int index = 0; index < count; index++)
            context.DrawLine(AxisPen, center, point(center, radius, index, count));

        StreamGeometry shape = new();
        using (StreamGeometryContext geometry = shape.Open())
        {
            for (int index = 0; index < count; index++)
            {
                double fraction = double.IsFinite(values[index]) ? Math.Clamp(values[index], 0, 1) : 0;
                Point vertex = point(center, radius * fraction, index, count);
                if (index == 0)
                    geometry.BeginFigure(vertex, true, true);
                else
                    geometry.LineTo(vertex, true, false);
            }
        }
        shape.Freeze();
        context.DrawGeometry(ValueBrush, ValuePen, shape);

        for (int index = 0; index < count; index++)
        {
            FormattedText text = texts[index];
            double directionX = Math.Cos(angleOf(index, count));
            double directionY = Math.Sin(angleOf(index, count));

            // 锚点贴着多边形的外沿，文字再按方向往外排：
            // 右侧左对齐、左侧右对齐、正上下居中，这样多字标签也不会出界。
            Point anchor = point(center, radius + LabelGap, index, count);
            double x = directionX > 0.2 ? anchor.X
                : directionX < -0.2 ? anchor.X - text.WidthIncludingTrailingWhitespace
                : anchor.X - text.WidthIncludingTrailingWhitespace / 2;
            double y = directionY > 0.2 ? anchor.Y
                : directionY < -0.2 ? anchor.Y - text.Height
                : anchor.Y - text.Height / 2;

            context.DrawText(text, new Point(x, y));
        }
    }

    private void drawCentered(DrawingContext context, string message, double width, double height)
    {
        FormattedText text = format(message, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(text, new Point((width - text.Width) / 2, (height - text.Height) / 2));
    }

    private static StreamGeometry polygon(Point center, double radius, int count)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            for (int index = 0; index < count; index++)
            {
                Point vertex = point(center, radius, index, count);
                if (index == 0)
                    context.BeginFigure(vertex, false, true);
                else
                    context.LineTo(vertex, true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static double angleOf(int index, int count) => -Math.PI / 2 + 2 * Math.PI * index / count;

    private static Point point(Point center, double radius, int index, int count)
    {
        double angle = angleOf(index, count);
        return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }

    private static FormattedText format(string text, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), LabelFontSize, LabelBrush, pixelsPerDip);

    private static T freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
