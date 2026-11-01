using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OsuRoam.Controls;

/// <summary>
/// 键型占比直方图：一行一个类别（标签 + 百分比 + 条）。
///
/// 为什么用直方图而不是雷达图：6 个核心键型的占比**加起来是 1**，
/// 雷达图适合"各自独立、可独立变大"的维度（比如 7 轴技能强度），
/// 而占比这种"此消彼长"的数据在雷达图里会让人误读成两个方向互相独立。
/// 横向条能直接比长短，也是 MMA 主卡片里 cluster 条的做法。
///
/// 只画 DrawingContext，不建子元素：卡片详情和历史里各有 6 条，
/// 用控件树的话每次翻面都要重新布局一遍。
/// </summary>
public sealed class PatternBars : FrameworkElement
{
    private const double LabelFontSize = 9;
    private const double ValueFontSize = 9;
    private const double BarHeight = 4;
    private const double RowGap = 4;
    private const double TextGap = 2;

    private static readonly Brush LabelBrush = freeze(new SolidColorBrush(Color.FromRgb(0xA5, 0xAD, 0xB3)));
    private static readonly Brush ValueBrush = freeze(new SolidColorBrush(Color.FromRgb(0xF3, 0xF5, 0xF6)));
    private static readonly Brush TrackBrush = freeze(new SolidColorBrush(Color.FromRgb(0x27, 0x2C, 0x31)));

    private static readonly Brush FillBrush = freeze(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromRgb(0x4E, 0xC7, 0xFF), 0),
            new(Color.FromRgb(0x58, 0xF0, 0xD9), 0.6),
            new(Color.FromRgb(0xF6, 0xEF, 0x6B), 1)
        },
        new Point(0, 0.5),
        new Point(1, 0.5)));

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(PatternBars),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, onDataChanged));

    public static readonly DependencyProperty SharesProperty = DependencyProperty.Register(
        nameof(Shares), typeof(IReadOnlyList<double>), typeof(PatternBars),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, onDataChanged));

    /// <summary>占比条的绘制进度 0→1，换数据时从 0 长到 1。</summary>
    private static readonly DependencyProperty GrowthProperty = DependencyProperty.Register(
        "Growth", typeof(double), typeof(PatternBars),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private FormattedText[]? cachedLabels;
    private string? cachedLabelSignature;
    private double cachedPixelsPerDip;

    /// <summary>真正要画的行（跳过显示为 0% 的键型），按 Labels/Shares 变化重算。</summary>
    private int[]? visibleRows;

    public IReadOnlyList<string>? Labels
    {
        get => (IReadOnlyList<string>?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public IReadOnlyList<double>? Shares
    {
        get => (IReadOnlyList<double>?)GetValue(SharesProperty);
        set => SetValue(SharesProperty, value);
    }

    public PatternBars()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    private int Count => resolveRows().Length;

    /// <summary>
    /// 只保留四舍五入后大于 0% 的行：谱面里常见的 "Jacks 0% / Wildcard 0%"
    /// 画出来既没信息量，又白占卡片高度。
    /// </summary>
    private int[] resolveRows() => visibleRows ??= VisibleRowIndices(Labels, Shares);

    /// <summary>
    /// 需要画出来的行号：跳过显示为 0% 的键型。
    /// 谱面里 "Jacks 0% / Wildcard 0%" 很常见，画出来既没信息量又白占高度。
    /// 判据按四舍五入后的百分比算，所以 0.4% 也不画（界面上它同样是 "0%"）。
    /// </summary>
    public static int[] VisibleRowIndices(IReadOnlyList<string>? labels, IReadOnlyList<double>? shares)
    {
        int count = Math.Min(labels?.Count ?? 0, shares?.Count ?? 0);
        List<int> rows = new(count);
        for (int index = 0; index < count; index++)
        {
            double share = shares![index];
            if (!double.IsFinite(share) || share <= 0)
                continue;
            if (Math.Round(share * 100, MidpointRounding.AwayFromZero) <= 0)
                continue;
            rows.Add(index);
        }

        return rows.ToArray();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Count;
        double height = count == 0 ? 0 : count * rowHeight() - RowGap;
        double width = double.IsInfinity(availableSize.Width) ? 180 : availableSize.Width;
        return new Size(width, Math.Max(0, height));
    }

    protected override void OnRender(DrawingContext context)
    {
        int[] rows = resolveRows();
        if (rows.Length == 0)
            return;

        double growth = (double)GetValue(GrowthProperty);
        if (!double.IsFinite(growth))
            growth = 1;
        growth = Math.Clamp(growth, 0, 1);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText[] labels = resolveLabels(rows.Length, pixelsPerDip);
        double width = Math.Max(24, ActualWidth);

        for (int row = 0; row < rows.Length; row++)
        {
            int index = rows[row];
            double y = row * rowHeight();
            FormattedText label = labels[row];
            FormattedText value = format(Shares, index, pixelsPerDip);

            context.DrawText(label, new Point(0, y));
            context.DrawText(value, new Point(Math.Max(label.Width + TextGap, width - value.Width), y));

            double barTop = y + label.Height + TextGap;
            double radius = BarHeight / 2;
            context.DrawRoundedRectangle(TrackBrush, null, new Rect(0, barTop, width, BarHeight), radius, radius);

            double fillWidth = width * Math.Clamp(sharesAt(index), 0, 1) * growth;
            if (fillWidth > 0.5)
                context.DrawRoundedRectangle(FillBrush, null, new Rect(0, barTop, fillWidth, BarHeight), radius, radius);
        }
    }

    private FormattedText format(IReadOnlyList<double>? shares, int index, double pixelsPerDip) =>
        // 不用 "P0"：InvariantCulture 会格式化成 "42 %"（带空格），卡片里看着别扭。
        new($"{(sharesAt(index) * 100).ToString("0", CultureInfo.InvariantCulture)}%", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(Services.Typography.Current, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            ValueFontSize, ValueBrush, pixelsPerDip);

    private double sharesAt(int index)
    {
        IReadOnlyList<double>? shares = Shares;
        if (shares is null || index < 0 || index >= shares.Count)
            return 0;
        double value = shares[index];
        return double.IsFinite(value) ? value : 0;
    }

    private FormattedText[] resolveLabels(int count, double pixelsPerDip)
    {
        IReadOnlyList<string>? labels = Labels;
        string signature = string.Join('\u001f', labels ?? []);
        if (cachedLabels is not null && cachedLabels.Length == Count &&
            Math.Abs(cachedPixelsPerDip - pixelsPerDip) < 0.0001 &&
            string.Equals(cachedLabelSignature, signature, StringComparison.Ordinal))
        {
            return cachedLabels;
        }

        int[] rows = resolveRows();
        FormattedText[] texts = new FormattedText[rows.Length];
        for (int row = 0; row < rows.Length; row++)
        {
            int index = rows[row];
            texts[row] = new FormattedText(
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

    private static double rowHeight() => LabelFontSize + TextGap + BarHeight + RowGap;

    private static void onDataChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        PatternBars bars = (PatternBars)target;
        bars.cachedLabels = null;
        bars.cachedLabelSignature = null;
        bars.visibleRows = null;
        bars.beginGrowth();
    }

    /// <summary>
    /// 换数据时让条从 0 长到 1：基值保持 1，动画从 0 走到 1（Stop 结束后回到基值），
    /// 这样中断重放也不会把 1 写死成 0。
    /// </summary>
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
            DoubleAnimation animation = new(0.0, 1.0, TimeSpan.FromMilliseconds(420))
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
