using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OsuRoam.Services;

namespace OsuRoam.Controls;

/// <summary>
/// 极简雷达图，6 或 7 轴通用。
/// 关键点：
/// 1) 几何只用 ActualWidth/ActualHeight，不使用固定尺寸；
/// 2) 文字按当前显示器 DPI 测量（pixelsPerDip），否则缩放屏上标签会错位；
/// 3) 标签带宽度由实际文字尺寸推导，多边形半径随之收敛，顶点与标签都不会越界；
/// 4) 数值变化时从「当前显示向量」补间到新向量（移植自 MMA 的 star-pop/向量生长），
///    标签与同心环按尺寸/DPI 缓存，动画期间只重建多边形本身。
/// </summary>
public sealed class RadarChart : FrameworkElement
{
    private const double LabelFontSize = 9;
    private const double LabelGap = 4;
    private const double EdgePadding = 2;
    private const double PreferredSize = 120;
    private const int GrowthMilliseconds = 440;

    private static readonly Brush GridBrush = freeze(new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x3E)));
    private static readonly Brush AxisBrush = freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x34)));
    private static readonly Brush LabelBrush = freeze(new SolidColorBrush(Color.FromRgb(0xA5, 0xAD, 0xB3)));
    private static readonly Brush ValueBrush = freeze(new SolidColorBrush(Color.FromArgb(0x66, 0x44, 0xB7, 0xAE)));
    private static readonly Pen ValuePen = freeze(new Pen(freeze(new SolidColorBrush(Color.FromRgb(0x44, 0xB7, 0xAE))), 1.4));
    private static readonly Pen RingPen = freeze(new Pen(GridBrush, 0.6));
    private static readonly Pen AxisPen = freeze(new Pen(AxisBrush, 0.5));

    // 叠加层（LN 三轴）：粉色，与主多边形的青色区分开。
    private static readonly Brush OverlayLabelBrush = freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x8A)));
    private static readonly Brush OverlayFillBrush = freeze(new SolidColorBrush(Color.FromArgb(0x3C, 0xFF, 0x5C, 0x8A)));
    private static readonly Brush OverlayFillActiveBrush = freeze(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0x5C, 0x8A)));
    private static readonly Pen OverlayPen = freeze(new Pen(freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x8A))), 1.5));

    /// <summary>叠加层三条轴的固定朝向：正下、右上、左上。</summary>
    private static readonly double[] OverlayAngles = [90, 330, 210];

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(RadarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, onLabelsChanged));

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(RadarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, onValuesChanged));

    /// <summary>
    /// 叠加层数值：3 个 0–1 的半径比例，顺序是「正下、右上、左上」。
    /// null 表示这张谱没有叠加层（例如 LN 占比不到面谱门槛）。
    /// </summary>
    public static readonly DependencyProperty OverlayValuesProperty = DependencyProperty.Register(
        nameof(OverlayValues), typeof(IReadOnlyList<double>), typeof(RadarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, onOverlayChanged));

    /// <summary>叠加层标签；悬停或点击锁定后替换掉主标签。</summary>
    public static readonly DependencyProperty OverlayLabelsProperty = DependencyProperty.Register(
        nameof(OverlayLabels), typeof(IReadOnlyList<string>), typeof(RadarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, onOverlayChanged));

    /// <summary>补间进度，0→1；动画期间每帧只重画多边形。</summary>
    private static readonly DependencyProperty GrowthProperty = DependencyProperty.Register(
        "Growth", typeof(double), typeof(RadarChart),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// 标签字体。XAML 里用 <c>{DynamicResource UiFont}</c> 绑定，
    /// 这样设置面板切换 osu! 字体时这里的标签也会跟着换（改了要丢缓存重排）。
    /// </summary>
    public static readonly DependencyProperty LabelFontFamilyProperty = DependencyProperty.Register(
        nameof(LabelFontFamily), typeof(FontFamily), typeof(RadarChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, onLabelFontChanged));

    private double[] fromValues = [];
    private double[] targetValues = [];

    // 叠加层的交互状态：悬停临时显示，点击锁存（触屏/键鼠都能用）。
    private bool overlayHovered;
    private bool overlayLocked;
    private FormattedText[]? cachedOverlayTexts;
    private string? cachedOverlaySignature;

    // 标签布局缓存：只有标签文本、DPI 或控件尺寸变化时才重算 FormattedText。
    private FormattedText[]? cachedTexts;
    private int cachedTextCount;
    private double cachedPixelsPerDip;
    private string? cachedLabelSignature;
    private Geometry? cachedRings;
    private Geometry? cachedAxes;
    private double cachedGeometryRadius;
    private Size cachedGeometrySize;
    private int cachedGeometryCount;

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

    public IReadOnlyList<double>? OverlayValues
    {
        get => (IReadOnlyList<double>?)GetValue(OverlayValuesProperty);
        set => SetValue(OverlayValuesProperty, value);
    }

    public IReadOnlyList<string>? OverlayLabels
    {
        get => (IReadOnlyList<string>?)GetValue(OverlayLabelsProperty);
        set => SetValue(OverlayLabelsProperty, value);
    }

    private bool HasOverlay => OverlayValues is { Count: 3 };

    /// <summary>悬停或点击锁定任一成立时，标签切到叠加层的三轴。</summary>
    private bool ShowOverlayLabels => HasOverlay && (overlayHovered || overlayLocked);

    /// <summary>标签字体；为 null 时跟随 <see cref="Services.Typography.Current"/>。</summary>
    public FontFamily? LabelFontFamily
    {
        get => (FontFamily?)GetValue(LabelFontFamilyProperty);
        set => SetValue(LabelFontFamilyProperty, value);
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

    private static void onLabelsChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        RadarChart chart = (RadarChart)target;
        chart.cachedTexts = null;
        chart.cachedLabelSignature = null;
        chart.cachedRings = null;
        chart.cachedAxes = null;
        // 轴数变了要按新轴数重新评估向量（第一次绑定时 Labels 可能晚于 Values 到位）。
        chart.beginGrowth(chart.Values);
    }

    private static void onValuesChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        ((RadarChart)target).beginGrowth(e.NewValue as IReadOnlyList<double>);
    }

    private static void onOverlayChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        RadarChart chart = (RadarChart)target;
        chart.cachedOverlayTexts = null;
        chart.cachedOverlaySignature = null;

        if (!chart.HasOverlay)
        {
            chart.overlayHovered = false;
            chart.overlayLocked = false;
            chart.Cursor = null;
        }
        else
        {
            chart.Cursor = Cursors.Hand;
        }
    }

    /// <summary>
    /// 叠加层存在时，把整个雷达圆盘（而非只是粉色三角）当作可命中区域：
    /// 三角在 LN 刚过门槛时只有二十几像素，按多边形判悬停根本对不进去。
    /// </summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        if (!HasOverlay)
            return base.HitTestCore(hitTestParameters);

        Point center = new(ActualWidth / 2, ActualHeight / 2);
        double hitRadius = Math.Min(ActualWidth, ActualHeight) / 2 * 0.8;
        return (hitTestParameters.HitPoint - center).Length <= hitRadius
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (!HasOverlay || overlayHovered)
            return;
        overlayHovered = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!overlayHovered)
            return;
        overlayHovered = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!HasOverlay)
            return;
        overlayLocked = !overlayLocked;
        Cursor = Cursors.Hand;
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>字体换了：标签宽度会变，缓存的 FormattedText 全部作废。</summary>
    private static void onLabelFontChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        RadarChart chart = (RadarChart)target;
        chart.cachedTexts = null;
        chart.cachedLabelSignature = null;
    }

    /// <summary>
    /// 新向量到达：先把「当前显示向量」冻结成补间的起点，再从 0 走到 1。
    /// 中断时起点取的是插值后的当前值，所以连续切换不会跳。
    /// </summary>
    private void beginGrowth(IReadOnlyList<double>? incoming)
    {
        IReadOnlyList<string>? labels = Labels;
        int count = labels?.Count ?? 0;

        if (incoming is null || count < 3 || incoming.Count < count)
        {
            fromValues = [];
            targetValues = [];
            BeginAnimation(GrowthProperty, null);
            SetCurrentValue(GrowthProperty, 1.0);
            return;
        }

        double[] next = new double[count];
        for (int index = 0; index < count; index++)
            next[index] = double.IsFinite(incoming[index]) ? Math.Clamp(incoming[index], 0, 1) : 0;

        if (valuesEqual(targetValues, next))
            return;

        double[] current = displayedValues();
        fromValues = current.Length == count ? current : new double[count];
        targetValues = next;

        try
        {
            BeginAnimation(GrowthProperty, null);
            SetCurrentValue(GrowthProperty, 1.0);
            DoubleAnimation animation = new(0.0, 1.0, TimeSpan.FromMilliseconds(GrowthMilliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            BeginAnimation(GrowthProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        catch (Exception exception)
        {
            // 生长动画失败就退化成直接显示最终形状，别让绘制层抛出去。
            Services.AppLog.Error("雷达图生长动画失败", exception);
            BeginAnimation(GrowthProperty, null);
            SetCurrentValue(GrowthProperty, 1.0);
        }
    }

    private double[] displayedValues()
    {
        if (targetValues.Length == 0)
            return [];
        double progress = (double)GetValue(GrowthProperty);
        double[] result = new double[targetValues.Length];
        for (int index = 0; index < targetValues.Length; index++)
        {
            double start = index < fromValues.Length ? fromValues[index] : 0;
            result[index] = start + (targetValues[index] - start) * progress;
        }
        return result;
    }

    private static bool valuesEqual(double[] left, double[] right)
    {
        if (left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (Math.Abs(left[index] - right[index]) > 0.0005)
                return false;
        }
        return true;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 4 || height <= 4)
            return;

        IReadOnlyList<string>? labels = Labels;
        int count = labels?.Count ?? 0;
        bool hasValues = Values is not null && targetValues.Length == count && count >= 3;
        if (count < 3 || (Values is not null && !hasValues))
        {
            drawCentered(context, Loc.T("radar.empty"), width, height);
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText[] texts = resolveTexts(labels!, pixelsPerDip);
        double maxTextWidth = 0;
        double maxTextHeight = 0;
        for (int index = 0; index < texts.Length; index++)
        {
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
            drawCentered(context, Loc.T("radar.tight"), width, height);
            return;
        }

        (Geometry rings, Geometry axes) = resolveGrid(center, radius, count);
        context.DrawGeometry(null, RingPen, rings);
        context.DrawGeometry(null, AxisPen, axes);

        if (hasValues)
        {
            double[] values = displayedValues();
            StreamGeometry shape = new();
            using (StreamGeometryContext geometry = shape.Open())
            {
                for (int index = 0; index < count; index++)
                {
                    Point vertex = point(center, radius * values[index], index, count);
                    if (index == 0)
                        geometry.BeginFigure(vertex, true, true);
                    else
                        geometry.LineTo(vertex, true, false);
                }
            }
            shape.Freeze();
            context.DrawGeometry(ValueBrush, ValuePen, shape);
        }

        if (HasOverlay)
        {
            // 叠加层的三条轴是「正下 / 右上 / 左上」，不是七边形的顶点方向。
            // 直接按外接圆半径画，正下那个顶点会戳到七边形的边外面（视觉上像画崩了），
            // 所以乘上七边形的内切圆系数 cos(π/n)：满格时正好贴住格线。
            double overlayRadius = radius * Math.Cos(Math.PI / count);
            StreamGeometry overlayShape = new();
            using (StreamGeometryContext geometry = overlayShape.Open())
            {
                for (int index = 0; index < 3; index++)
                {
                    double value = OverlayValues![index];
                    double scaled = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
                    Point vertex = pointAt(center, overlayRadius * scaled, OverlayAngles[index] * Math.PI / 180);
                    if (index == 0)
                        geometry.BeginFigure(vertex, true, true);
                    else
                        geometry.LineTo(vertex, true, false);
                }
            }
            overlayShape.Freeze();
            context.DrawGeometry(ShowOverlayLabels ? OverlayFillActiveBrush : OverlayFillBrush, OverlayPen, overlayShape);
        }

        if (ShowOverlayLabels)
        {
            FormattedText[] overlayTexts = resolveOverlayTexts(pixelsPerDip);
            double overlayLabelRadius = radius * Math.Cos(Math.PI / count) + LabelGap;
            for (int index = 0; index < overlayTexts.Length; index++)
                placeLabel(context, overlayTexts[index], center, overlayLabelRadius, OverlayAngles[index] * Math.PI / 180);
        }
        else
        {
            for (int index = 0; index < count; index++)
                placeLabel(context, texts[index], center, radius + LabelGap, angleOf(index, count));
        }
    }

    /// <summary>把一行标签按方向摆到半径外沿：右侧左对齐、左侧右对齐、正上下居中。</summary>
    private static void placeLabel(DrawingContext context, FormattedText text, Point center, double radius, double angle)
    {
        double directionX = Math.Cos(angle);
        double directionY = Math.Sin(angle);
        Point anchor = pointAt(center, radius, angle);

        double x = directionX > 0.2 ? anchor.X
            : directionX < -0.2 ? anchor.X - text.WidthIncludingTrailingWhitespace
            : anchor.X - text.WidthIncludingTrailingWhitespace / 2;
        double y = directionY > 0.2 ? anchor.Y
            : directionY < -0.2 ? anchor.Y - text.Height
            : anchor.Y - text.Height / 2;

        context.DrawText(text, new Point(x, y));
    }

    private FormattedText[] resolveOverlayTexts(double pixelsPerDip)
    {
        IReadOnlyList<string>? overlayLabels = OverlayLabels;
        int count = overlayLabels?.Count ?? 0;
        string signature = overlayLabels is null ? string.Empty : string.Join('\u001f', overlayLabels);

        if (cachedOverlayTexts is not null &&
            cachedOverlayTexts.Length == count &&
            string.Equals(cachedOverlaySignature, signature, StringComparison.Ordinal))
        {
            return cachedOverlayTexts;
        }

        FormattedText[] texts = new FormattedText[count];
        for (int index = 0; index < count; index++)
            texts[index] = new FormattedText(overlayLabels![index] ?? string.Empty,
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(LabelFontFamily ?? Typography.Current, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                LabelFontSize, OverlayLabelBrush, pixelsPerDip);

        cachedOverlayTexts = texts;
        cachedOverlaySignature = signature;
        return texts;
    }

    private FormattedText[] resolveTexts(IReadOnlyList<string> labels, double pixelsPerDip)
    {
        string signature = string.Join('\u001f', labels);
        if (cachedTexts is not null &&
            cachedTextCount == labels.Count &&
            Math.Abs(cachedPixelsPerDip - pixelsPerDip) < 0.0001 &&
            string.Equals(cachedLabelSignature, signature, StringComparison.Ordinal))
        {
            return cachedTexts;
        }

        FormattedText[] texts = new FormattedText[labels.Count];
        for (int index = 0; index < labels.Count; index++)
            texts[index] = format(labels[index] ?? string.Empty, pixelsPerDip);

        cachedTexts = texts;
        cachedTextCount = labels.Count;
        cachedPixelsPerDip = pixelsPerDip;
        cachedLabelSignature = signature;
        return texts;
    }

    private (Geometry rings, Geometry axes) resolveGrid(Point center, double radius, int count)
    {
        if (cachedRings is not null && cachedAxes is not null &&
            cachedGeometryCount == count &&
            Math.Abs(cachedGeometryRadius - radius) < 0.05 &&
            Math.Abs(cachedGeometrySize.Width - ActualWidth) < 0.05 &&
            Math.Abs(cachedGeometrySize.Height - ActualHeight) < 0.05)
        {
            return (cachedRings, cachedAxes);
        }

        GeometryGroup rings = new();
        for (int ring = 1; ring <= 4; ring++)
            rings.Children.Add(polygon(center, radius * ring / 4.0, count));

        GeometryGroup axes = new();
        for (int index = 0; index < count; index++)
        {
            LineGeometry axis = new(center, point(center, radius, index, count));
            axis.Freeze();
            axes.Children.Add(axis);
        }

        rings.Freeze();
        axes.Freeze();
        cachedRings = rings;
        cachedAxes = axes;
        cachedGeometryRadius = radius;
        cachedGeometrySize = new Size(ActualWidth, ActualHeight);
        cachedGeometryCount = count;
        return (rings, axes);
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

    private static Point pointAt(Point center, double radius, double angleRadians) =>
        new(center.X + radius * Math.Cos(angleRadians), center.Y + radius * Math.Sin(angleRadians));

    private FormattedText format(string text, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(LabelFontFamily ?? Typography.Current, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            LabelFontSize, LabelBrush, pixelsPerDip);

    private static T freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
