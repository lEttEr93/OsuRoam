using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OsuRoam.Controls;

/// <summary>
/// 一排 SV 小环：每个环 = 一个指标，环的填充长度是数值，颜色按「好/坏」方向从红渐变到绿。
///
/// 为什么用环而不是数字：这四个量（等时性 / 舒适度 / 背谱 / 强度）都是 0–1 的比率，
/// 卡片里没地方写四位小数，环一眼就能看出「这谱稳不稳」；确切数值走 tooltip。
///
/// 配色方向由 <see cref="HigherIsWorse"/> 决定：等时性、舒适度越高越好（低 = 红），
/// 背谱、强度越高越差（高 = 红）。环本身只画 DrawingContext，不建子元素。
/// </summary>
public sealed class SvRings : FrameworkElement
{
    private const double RingDiameter = 16;
    private const double RingThickness = 2.8;
    private const double LabelFontSize = 9;
    private const double RingLabelGap = 3;

    private static readonly Brush LabelBrush = freeze(new SolidColorBrush(Color.FromRgb(0xA5, 0xAD, 0xB3)));
    private static readonly Brush TrackBrush = freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x35)));

    // 红 → 琥珀 → 青绿：直接红绿插值会经过发灰的橄榄色，中段加个琥珀更清楚。
    private static readonly Color LowColour = Color.FromRgb(0xE0, 0x5A, 0x4A);
    private static readonly Color MidColour = Color.FromRgb(0xE6, 0xB9, 0x53);
    private static readonly Color HighColour = Color.FromRgb(0x52, 0xCE, 0x9C);

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(SvRings),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, onDataChanged));

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(SvRings),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, onDataChanged));

    /// <summary>每个指标「值越高越差」的标记；长度不足时按 false（越高越好）处理。</summary>
    public static readonly DependencyProperty HigherIsWorseProperty = DependencyProperty.Register(
        nameof(HigherIsWorse), typeof(IReadOnlyList<bool>), typeof(SvRings),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>环的绘制进度 0→1，换数据时从 0 长到 1。</summary>
    private static readonly DependencyProperty GrowthProperty = DependencyProperty.Register(
        "Growth", typeof(double), typeof(SvRings),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private FormattedText[]? cachedLabels;
    private string? cachedLabelSignature;
    private double cachedPixelsPerDip;

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

    public IReadOnlyList<bool>? HigherIsWorse
    {
        get => (IReadOnlyList<bool>?)GetValue(HigherIsWorseProperty);
        set => SetValue(HigherIsWorseProperty, value);
    }

    public SvRings()
    {
        // 要能接住鼠标，四个量的确切数值挂在 tooltip 上。
        IsHitTestVisible = true;
        SnapsToDevicePixels = true;
    }

    private int Count => Math.Min(Labels?.Count ?? 0, Values?.Count ?? 0);

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Count;
        double width = double.IsInfinity(availableSize.Width) ? 4 * (RingDiameter + 12) : availableSize.Width;
        return new Size(width, count == 0 ? 0 : preferredHeight);
    }

    private static double preferredHeight => RingDiameter + RingLabelGap + 12;

    protected override void OnRender(DrawingContext context)
    {
        int count = Count;
        if (count == 0)
            return;

        double growth = (double)GetValue(GrowthProperty);
        if (!double.IsFinite(growth))
            growth = 1;
        growth = Math.Clamp(growth, 0, 1);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText[] labels = resolveLabels(count, pixelsPerDip);
        double width = Math.Max(1, ActualWidth);
        double cellWidth = width / count;
        double radius = RingDiameter / 2;

        for (int index = 0; index < count; index++)
        {
            double value = Math.Clamp(valueAt(index), 0, 1);
            double centreX = cellWidth * index + cellWidth / 2;
            Point centre = new(centreX, radius + 1);

            context.DrawEllipse(null, new Pen(TrackBrush, RingThickness), centre, radius, radius);

            // 数值为 0 时也留一小段弧：空环看起来像「没数据」，而 0 是「测出来就是 0」。
            double sweep = Math.Min(Math.Max(value, 0.02) * growth, 0.999);

            if (sweep > 0.001)
            {
                Brush brush = freeze(new SolidColorBrush(colourOf(value, index)));
                context.DrawGeometry(null, new Pen(brush, RingThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                    arc(centre, radius, sweep));
            }

            FormattedText label = labels[index];
            context.DrawText(label, new Point(centreX - label.Width / 2, RingDiameter + RingLabelGap));
        }
    }

    /// <summary>从 12 点方向顺时针画一段圆弧（0–1 圈）。</summary>
    private static Geometry arc(Point centre, double radius, double sweep)
    {
        double start = -Math.PI / 2;
        double end = start + sweep * 2 * Math.PI;
        Point from = new(centre.X + radius * Math.Cos(start), centre.Y + radius * Math.Sin(start));
        Point to = new(centre.X + radius * Math.Cos(end), centre.Y + radius * Math.Sin(end));

        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(from, false, false);
            context.ArcTo(to, new Size(radius, radius), 0, sweep > 0.5, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>0 = 最差（红），0.5 = 琥珀，1 = 最好（绿）；背谱/强度会先反转。</summary>
    public static Color colourOf(double value, bool higherIsWorse)
    {
        double t = Math.Clamp(higherIsWorse ? 1 - value : value, 0, 1);
        return t < 0.5
            ? blend(LowColour, MidColour, t * 2)
            : blend(MidColour, HighColour, (t - 0.5) * 2);
    }

    private Color colourOf(double value, int index)
    {
        IReadOnlyList<bool>? worse = HigherIsWorse;
        bool higherIsWorse = worse is not null && index < worse.Count && worse[index];
        return colourOf(value, higherIsWorse);
    }

    private static Color blend(Color from, Color to, double weight) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * weight),
        (byte)Math.Round(from.G + (to.G - from.G) * weight),
        (byte)Math.Round(from.B + (to.B - from.B) * weight));

    private double valueAt(int index)
    {
        IReadOnlyList<double>? values = Values;
        if (values is null || index < 0 || index >= values.Count)
            return 0;
        double value = values[index];
        return double.IsFinite(value) ? value : 0;
    }

    private FormattedText[] resolveLabels(int count, double pixelsPerDip)
    {
        IReadOnlyList<string>? labels = Labels;
        string signature = string.Join('\u001f', labels ?? []);
        if (cachedLabels is not null && cachedLabels.Length == count &&
            Math.Abs(cachedPixelsPerDip - pixelsPerDip) < 0.0001 &&
            string.Equals(cachedLabelSignature, signature, StringComparison.Ordinal))
        {
            return cachedLabels;
        }

        FormattedText[] texts = new FormattedText[count];
        for (int index = 0; index < count; index++)
        {
            texts[index] = new FormattedText(
                labels is not null && index < labels.Count ? labels[index] ?? string.Empty : string.Empty,
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Services.Typography.Current, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                LabelFontSize, LabelBrush, pixelsPerDip);
        }

        cachedLabels = texts;
        cachedLabelSignature = signature;
        cachedPixelsPerDip = pixelsPerDip;
        return texts;
    }

    private static void onDataChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        SvRings rings = (SvRings)target;
        rings.cachedLabels = null;
        rings.cachedLabelSignature = null;
        rings.beginGrowth();
    }

    /// <summary>换数据时让环从 0 转出来，和键型条的 Growth 一致。</summary>
    private void beginGrowth()
    {
        if (!SystemParameters.ClientAreaAnimation || Count == 0)
        {
            BeginAnimation(GrowthProperty, null);
            SetCurrentValue(GrowthProperty, 1.0);
            return;
        }

        try
        {
            BeginAnimation(GrowthProperty, null);
            SetCurrentValue(GrowthProperty, 1.0);
            DoubleAnimation animation = new(0.0, 1.0, TimeSpan.FromMilliseconds(460))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            BeginAnimation(GrowthProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        catch
        {
            BeginAnimation(GrowthProperty, null);
            SetCurrentValue(GrowthProperty, 1.0);
        }
    }

    private static Brush freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
