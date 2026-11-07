using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OsuRoam.Controls;
using OsuRoam.Models;
using OsuRoam.Services;

namespace OsuRoam;

/// <summary>
/// 导入谱面后自动弹出的信息窗：贴在主窗口（通常是迷你播放器）下方，显示封面、星级、
/// CS/OD/AR/HP、物件数、键型雷达和键型占比。
///
/// 布局思路参考 MMA 的主卡片：星级大号数字按 osu! 难度色标上色，键型占比用横向生长条，
/// 雷达向量由 <see cref="RadarChart"/> 自己做生长动画。
///
/// 生命周期：
/// - Roam 发起导入时由主窗口调 <see cref="ShowFor"/>；
/// - 下一次开局（tosu 的 PlayStarted）由主窗口调 <see cref="Dismiss"/>；
/// - 用户点 × 也走 <see cref="Dismiss"/>，下次导入会再出现。
/// </summary>
public partial class MapInfoWindow : Window
{
    private static readonly Brush TextBrush = freeze(new SolidColorBrush(Color.FromRgb(0xF3, 0xF5, 0xF6)));
    private static readonly Brush MutedBrush = freeze(new SolidColorBrush(Color.FromRgb(0xA5, 0xAD, 0xB3)));
    private static readonly Brush CapsuleBackground = freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x26)));
    private static readonly Brush CapsuleBorder = freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x32, 0x38)));
    private static readonly Brush TrackBrush = freeze(new SolidColorBrush(Color.FromRgb(0x27, 0x2C, 0x31)));
    private static readonly Brush LnBrush = freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x8A)));
    private static readonly Brush LnDimBrush = freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x4A, 0x5E)));

    private Window? dockOwner;
    private RecommendationItem? currentItem;
    private bool exiting;

    /// <summary>非 null 时窗口处于「提示」状态（比如 tosu 没读到 osu!），不再显示谱面数据。</summary>
    private string? noticeKey;

    /// <summary>上一次画过的键型数据：同一个对象就不重画，避免动画重放。</summary>
    private ManiaPattern? renderedPattern;
    private double renderedStars;

    /// <summary>提示态和高低未定的卡片统一用这个高度：最后一次「完整卡片」的自然高度。</summary>
    private double cardHeight;
    private const double FallbackCardHeight = 420;

    /// <summary>键型就绪时详情行会长出「模式 / 主导键型 / 细分键型」几行，
    /// 于是卡片会变高。预留 4 行（10px 字号约 12.6px/行）把这段高度先占住。
    /// </summary>
    private const double ReservedDetailHeight = 51;

    /// <summary>
    /// 卡片现在（或收起时「应该」）有多高。预览窗拿它对齐高度：
    /// 信息窗没开的时候，谱面预览也得和它一样高，不能自己猜一个。
    /// </summary>
    public double PreferredHeight => ActualHeight > 120 ? ActualHeight : cardHeight > 0 ? cardHeight : FallbackCardHeight;

    public MapInfoWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => reposition();
        Loaded += (_, _) => reposition();
        // 换语言：胶囊、详情行、雷达标签全是代码拼的，得自己重算。
        Loc.Changed += onLanguageChanged;
        Closed += (_, _) => Loc.Changed -= onLanguageChanged;
    }

    /// <summary>换语言后标签要重画，先把「画过了」的标记清掉。</summary>
    private void onLanguageChanged()
    {
        renderedPattern = null;
        refresh();
    }

    /// <summary>挂在主窗口下面，跟随它移动/缩放，不单独占任务栏。</summary>
    public void DockTo(Window owner)
    {
        dockOwner = owner;
        Owner = owner;
        owner.LocationChanged += (_, _) => reposition();
        owner.SizeChanged += (_, _) => reposition();
        owner.Closed += (_, _) => Close();
        reposition();
    }

    /// <summary>显示（或更新）某个条目的信息；已经可见时只刷新内容。</summary>
    public void ShowFor(RecommendationItem item)
    {
        // 从提示态回到谱面态：把提示时藏起来的部件放回来，高度也交回 SizeToContent。
        noticeKey = null;
        SizeToContent = SizeToContent.Height;
        Height = double.NaN;
        StarPanel.Visibility = Visibility.Visible;
        CapsulePanel.Visibility = Visibility.Visible;
        NoticePanel.Visibility = Visibility.Collapsed;
        Separator.Visibility = Visibility.Visible;

        if (!ReferenceEquals(currentItem, item))
        {
            if (currentItem is not null)
                currentItem.PropertyChanged -= itemChanged;
            currentItem = item;
            currentItem.PropertyChanged += itemChanged;
        }

        DataContext = item;
        refresh();
        reposition();

        if (IsVisible)
            return;

        exiting = false;
        Show();
        PlayEntrance();
    }

    /// <summary>
    /// 提示模式：tosu 没读到 osu! 时代替谱面内容显示一句原因 + 一句怎么办。
    /// 不画星级/胶囊/键型 —— 那些数据本来就没有，画成空壳比直接说清楚更让人困惑。
    /// </summary>
    /// <param name="messageKey">原因文案的 Loc 键（<c>mapinfo.tosu.*</c>）。</param>
    public void ShowNotice(string messageKey)
    {
        noticeKey = messageKey;

        if (currentItem is not null)
        {
            currentItem.PropertyChanged -= itemChanged;
            currentItem = null;
        }

        DataContext = null;
        applyNotice();

        // 提示卡和普通卡片同高：一高一矮来回跳，看起来像窗口在抽搐。
        SizeToContent = SizeToContent.Manual;
        Height = cardHeight > 0 ? cardHeight : FallbackCardHeight;
        reposition();

        if (IsVisible)
            return;

        exiting = false;
        Show();
        PlayEntrance();
    }

    private void applyNotice()
    {
        TrackTitle.Text = Loc.T("mapinfo.tosu.title");
        TrackDifficulty.Text = string.Empty;
        TrackMeta.Text = string.Empty;

        StarPanel.Visibility = Visibility.Collapsed;
        CapsulePanel.Visibility = Visibility.Collapsed;
        RatioRow.Visibility = Visibility.Collapsed;
        PatternRow.Visibility = Visibility.Collapsed;
        Separator.Visibility = Visibility.Collapsed;
        Radar.Labels = null;
        Radar.Values = null;
        Radar.OverlayValues = null;
        Radar.OverlayLabels = null;
        Bars.Labels = null;
        Bars.Shares = null;
        SvRingRow.Labels = null;
        SvRingRow.Values = null;
        SvRingRow.Visibility = Visibility.Collapsed;

        DetailText.Text = string.Empty;
        DetailText.Visibility = Visibility.Collapsed;
        ReasonText.Text = string.Empty;
        ReasonText.Visibility = Visibility.Collapsed;
        // 提示态把三个控件都清空了，回到谱面态必须整块重画。
        renderedPattern = null;

        NoticeReason.Text = Loc.T(noticeKey ?? "mapinfo.tosu.nobeatmap");
        NoticeHint.Text = Loc.T("mapinfo.tosu.hint");
        NoticePanel.Visibility = Visibility.Visible;
    }

    /// <summary>收起（开局 / 手动关闭）。下次导入时会重新出现。</summary>
    public void Dismiss()
    {
        if (!IsVisible || exiting)
            return;

        exiting = true;
        if (!SystemParameters.ClientAreaAnimation)
        {
            finishExit();
            return;
        }

        try
        {
            // 退场用 EaseIn：EaseOut 是"一开始就冲到底"，之前的 120ms 里前 40ms 就掉到
            // 0.11 了，肉眼等于瞬间消失。EaseIn 前半段变化小，位移才看得出来。
            DoubleAnimation fade = new(1, 0, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.HoldEnd
            };
            fade.Completed += (_, _) => finishExit();
            RootCard.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);

            DoubleAnimation slide = new(0, 10, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.HoldEnd
            };
            CardSlide.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);

            DoubleAnimation shrink = new(1, 0.985, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.HoldEnd
            };
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink, HandoffBehavior.SnapshotAndReplace);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink, HandoffBehavior.SnapshotAndReplace);
        }
        catch (Exception exception)
        {
            AppLog.Error("谱面信息窗退场动画失败", exception);
            finishExit();
        }
    }

    private void finishExit()
    {
        RootCard.BeginAnimation(OpacityProperty, null);
        CardSlide.BeginAnimation(TranslateTransform.YProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CardSlide.Y = 0;
        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;
        Hide();
        exiting = false;
    }

    private void PlayEntrance()
    {
        RootCard.Opacity = 1;
        CardSlide.Y = 0;
        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;
        if (!SystemParameters.ClientAreaAnimation)
            return;

        try
        {
            DoubleAnimation fade = new(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            RootCard.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);

            DoubleAnimation slide = new(9, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            CardSlide.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);

            DoubleAnimation grow = new(0.985, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow, HandoffBehavior.SnapshotAndReplace);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow, HandoffBehavior.SnapshotAndReplace);
        }
        catch (Exception exception)
        {
            AppLog.Error("谱面信息窗入场动画失败", exception);
            RootCard.Opacity = 1;
        }
    }

    private void reposition()
    {
        if (dockOwner is null)
            return;

        Width = Math.Max(360, dockOwner.ActualWidth);
        Left = dockOwner.Left;

        // 主窗口外圈有 8px 透明边距，卡片底边在 ActualHeight - 8；这里再让出一点缝隙。
        double top = dockOwner.Top + Math.Max(64, dockOwner.ActualHeight) - 12;
        double height = ActualHeight > 0 ? ActualHeight : 420;
        Rect work = SystemParameters.WorkArea;
        if (top + height > work.Bottom - 4)
            top = Math.Max(work.Top + 4, work.Bottom - height - 4);
        Top = top;
    }

    private void itemChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 下载进度每帧都在变，跟信息窗无关，别浪费布局。
        if (e.PropertyName is nameof(RecommendationItem.DownloadProgress)
            or nameof(RecommendationItem.DownloadLabel)
            or nameof(RecommendationItem.PreviewProgress)
            or nameof(RecommendationItem.IsPreviewLoading)
            or nameof(RecommendationItem.IsPreviewPlaying))
        {
            return;
        }

        refresh();
    }

    private void refresh()
    {
        // 提示态没有条目可刷，换语言时重画一次文案就够了。
        if (noticeKey is not null)
        {
            applyNotice();
            return;
        }

        RecommendationItem? item = currentItem;
        if (item is null)
            return;

        TrackTitle.Text = item.DisplayTitle;
        TrackDifficulty.Text = item.DifficultyName;
        TrackMeta.Text = buildMetaLine(item);

        Color starColour = DifficultyColour.Sample(item.Stars);
        StarValue.Text = item.Stars.ToString("0.00");
        StarValue.Foreground = new SolidColorBrush(starColour);
        StarGlyph.Foreground = new SolidColorBrush(starColour);

        buildCapsules(item);
        buildPattern(item);

        DetailText.Text = buildDetailLine(item);
        DetailText.Visibility = DetailText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ReasonText.Text = item.Reason;
        ReasonText.Visibility = string.IsNullOrWhiteSpace(item.Reason) ? Visibility.Collapsed : Visibility.Visible;

        rememberCardHeight();
    }

    /// <summary>
    /// 记下「完整卡片」的自然高度（含键型占位），提示态拿它对齐。
    /// 等布局稳定再读：此时 ActualHeight 才是 SizeToContent 算完的结果。
    /// </summary>
    /// <summary>开发用：把各行的高度打出来，便于核对「占位态」和「谱面态」是否等高。</summary>
    internal string DescribeHeights() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"ratio={RatioRow.ActualHeight:0.0} pattern={PatternRow.ActualHeight:0.0} " +
            $"radar={RadarPanel.ActualHeight:0.0} side={PatternSidePanel.ActualHeight:0.0} " +
            $"placeholder={PatternPlaceholder.ActualHeight:0.0} detail={DetailText.ActualHeight:0.0} " +
            $"reason={ReasonText.ActualHeight:0.0} sep={Separator.ActualHeight:0.0} notice={NoticePanel.ActualHeight:0.0}");

    private void rememberCardHeight()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (noticeKey is null && SizeToContent == SizeToContent.Height &&
                PatternRow.Visibility == Visibility.Visible && ActualHeight > 120)
            {
                cardHeight = ActualHeight;
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static string buildMetaLine(RecommendationItem item)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(item.Mapper))
            parts.Add(item.Mapper);
        if (item.RankedYear > 0)
            parts.Add(item.RankedYear.ToString());
        if (item.KeyCount > 0)
            parts.Add($"{item.KeyCount}K");
        if (item.IsRecycled)
            parts.Add(Loc.T("mapinfo.recycled"));
        return string.Join(" · ", parts);
    }

    private void buildCapsules(RecommendationItem item)
    {
        CapsulePanel.Children.Clear();
        BeatmapDetail? detail = item.Detail;

        addCapsule("BPM", item.Bpm > 0 ? item.Bpm.ToString("0") : "—");
        addCapsule(Loc.T("mapinfo.length"), TimeSpan.FromSeconds(item.LengthSeconds).ToString(@"m\:ss"));

        if (detail is not null)
        {
            addCapsule(Loc.T("mapinfo.mode"), detail.ModeDisplay);
            addCapsule("OD", detail.OverallDifficulty.ToString("0.0"));
            addCapsule("AR", detail.ApproachRate.ToString("0.0"));
            addCapsule("CS", detail.CircleSize.ToString("0.0"));
            addCapsule("HP", detail.HPDrainRate.ToString("0.0"));
            if (detail.HasObjects)
                addCapsule(Loc.T("mapinfo.objects"), detail.ObjectCount.ToString());
        }
        else
        {
            addCapsule(Loc.T("mapinfo.detail"), Loc.T("mapinfo.detailpending"), MutedBrush);
        }
    }

    private void addCapsule(string label, string value, Brush? valueBrush = null)
    {
        Border capsule = new()
        {
            Background = CapsuleBackground,
            BorderBrush = CapsuleBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 5, 5)
        };

        StackPanel row = new() { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 8,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = valueBrush ?? TextBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        capsule.Child = row;
        CapsulePanel.Children.Add(capsule);
    }

    private void buildPattern(RecommendationItem item)
    {
        ManiaPattern? pattern = item.Pattern;
        if (pattern is null)
        {
            renderedPattern = null;

            // 键型还在后台算：先按「算完的样子」占位，卡片高度不会等数据到了再跳一次。
            if (item.IsPatternPending)
            {
                buildPendingPattern(item);
                return;
            }

            RatioRow.Visibility = Visibility.Collapsed;
            PatternRow.Visibility = Visibility.Collapsed;
            PatternPlaceholder.Visibility = Visibility.Collapsed;
            DetailText.MinHeight = 0;
            Radar.SetData(null, null, null, null);
            Bars.SetData(null, null);
            SvRingRow.SetData(null, null);
            SvRingRow.Visibility = Visibility.Collapsed;
            return;
        }

        // 曲绘/封面加载完会再触发一次 refresh，键型对象却还是同一个 ——
        // 这时候重建等于把三个动画各重放一遍，看起来就是「每次出现渲染两遍」。
        if (ReferenceEquals(pattern, renderedPattern) && Math.Abs(renderedStars - item.Stars) < 0.0001)
            return;

        renderedPattern = pattern;
        renderedStars = item.Stars;

        buildRatioBar(pattern.LnRatio, pattern.IsLnChart);
        buildSvRings(pattern);
        DetailText.MinHeight = ReservedDetailHeight;
        PatternRow.Visibility = Visibility.Visible;
        PatternPlaceholder.Visibility = Visibility.Collapsed;
        PatternSidePanel.Visibility = Visibility.Visible;

        // 7 轴技能强度：各轴独立，适合雷达图。面谱才叠 LN 三轴；
        // 悬停/点击时标签切换在雷达控件内部处理。
        if (pattern.SkillAxes.Length == ManiaPattern.SkillPatterns.Length)
        {
            RadarPanel.Visibility = Visibility.Visible;
            double[]? overlay = pattern.IsLnChart ? LnRadarBuilder.Build(pattern, item.Stars) : null;
            Radar.SetData(
                Loc.SkillPatternLabels(),
                pattern.SkillAxes,
                overlay,
                overlay is null
                    ? null
                    : [Loc.CorePatternLabel("Coordination"), Loc.CorePatternLabel("Density"), Loc.CorePatternLabel("Misc")]);
        }
        else
        {
            // 没有技能轴时宁可空着，也不要拿"占比和 = 1"的核心键型去凑雷达图。
            RadarPanel.Visibility = Visibility.Collapsed;
            Radar.SetData(null, null, null, null);
        }

        // 6 个核心键型占比：加起来是 1，用直方图看长短（雷达图会误导）。
        Bars.SetData(Loc.CorePatternLabels(), pattern.ToVector());
    }

    /// <summary>
    /// 键型未就绪时的占位：圆形区域留 152（和雷达一样高），右侧的环与直方图先不画。
    /// RC/LN 占比条倒是能立刻给出来 —— 长条数 / 音符数在 <see cref="BeatmapDetail"/> 里已经有了。
    /// </summary>
    private void buildPendingPattern(RecommendationItem item)
    {
        BeatmapDetail? detail = item.Detail;
        int notes = detail?.CircleCount ?? 0;
        int holds = detail?.HoldCount ?? 0;
        if (detail is { HasObjects: true } && notes + holds > 0)
        {
            double ln = holds / (double)(notes + holds);
            buildRatioBar(ln, ln >= ManiaPattern.LnChartThreshold);
        }
        else
        {
            RatioRow.Visibility = Visibility.Collapsed;
        }

        PatternRow.Visibility = Visibility.Visible;
        DetailText.MinHeight = ReservedDetailHeight;
        RadarPanel.Visibility = Visibility.Collapsed;
        PatternSidePanel.Visibility = Visibility.Collapsed;
        PatternPlaceholder.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// RC / LN 占比横条：两段按星号列宽分配，容器改宽度时自动跟着走。
    /// 入参是比例而不是 <see cref="ManiaPattern"/> —— 键型还没算完时可以先用
    /// <see cref="BeatmapDetail"/> 里的音符数/长条数给出同样的结果。
    /// </summary>
    private void buildRatioBar(double lnRatio, bool faceChart)
    {
        double ln = Math.Clamp(lnRatio, 0, 1);

        RcRatioColumn.Width = new GridLength(Math.Max(0.0001, 1 - ln), GridUnitType.Star);
        LnRatioColumn.Width = new GridLength(Math.Max(0.0001, ln), GridUnitType.Star);
        RcRatioLabel.Text = $"RC {(1 - ln):P0}";
        LnRatioLabel.Text = $"LN {ln:P0}";
        LnRatioLabel.Foreground = faceChart ? LnBrush : MutedBrush;
        LnRatioBar.Background = faceChart ? LnBrush : LnDimBrush;
        RatioRow.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// SV 四个小环：等时性 / 舒适度（越高越好）、背谱 / 强度（越高越差）。
    /// 口径见 <see cref="SvEvaluator"/>，确切数值走 tooltip —— 卡片里没地方写四位数。
    /// </summary>
    private void buildSvRings(ManiaPattern pattern)
    {
        SvMetrics? sv = pattern.Sv;
        if (sv is null || sv.Rows <= 0)
        {
            SvRingRow.SetData(null, null);
            SvRingRow.ToolTip = null;
            SvRingRow.Visibility = Visibility.Collapsed;
            return;
        }

        double[] values = sv.DisplayValues();
        SvRingRow.SetData(Loc.SvLabels(), values, SvMetrics.HigherIsWorse);
        SvRingRow.ToolTip = Loc.F("sv.tip",
            percent(values[0]), percent(values[1]), percent(values[2]), percent(values[3]));
        SvRingRow.Visibility = Visibility.Visible;

        static string percent(double value) =>
            (Math.Clamp(value, 0, 1) * 100).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "%";
    }

    private static string buildDetailLine(RecommendationItem item)
    {
        ManiaPattern? pattern = item.Pattern;
        BeatmapDetail? detail = item.Detail;
        List<string> parts = [];

        if (detail is { HasObjects: true })
        {
            List<string> objects = [Loc.F("detail.objects", detail.ObjectCount)];
            if (detail.CircleCount > 0)
                objects.Add(Loc.F("detail.circles", detail.CircleCount));
            if (detail.SliderCount > 0)
                objects.Add(Loc.F("detail.sliders", detail.SliderCount));
            if (detail.HoldCount > 0)
                objects.Add(Loc.F("detail.holds", detail.HoldCount));
            if (detail.SpinnerCount > 0)
                objects.Add(Loc.F("detail.spinners", detail.SpinnerCount));
            parts.Add(string.Join(" · ", objects));
        }

        if (pattern is not null)
        {
            parts.Add(Loc.F("detail.pattern", pattern.ModeTag, pattern.LnRatio.ToString("P0"), pattern.NotesPerSecond.ToString("0.0")));
            parts.Add(Loc.F("detail.dominantskill", pattern.Category, Loc.SkillSourceText(pattern.SkillSource)));
            KeyValuePair<string, double>[] top = pattern.SpecificShares
                .OrderByDescending(pair => pair.Value)
                .Take(4)
                .ToArray();
            if (top.Length > 0)
                parts.Add(string.Join(" · ", top.Select(pair => $"{pair.Key} {pair.Value:P0}")));
        }
        else if (item.KeyCount is 0 or 4 && !item.IsPatternPending)
        {
            parts.Add(Loc.T("detail.maniaonly"));
        }

        return string.Join("\n", parts);
    }

    /// <summary>开发用：Dismiss 之后采样不透明度，确认退场动画真的在跑而不是直接消失。</summary>
    public async Task ProbeExitAsync()
    {
        AppLog.Info($"信息窗退场探针：ClientAreaAnimation={SystemParameters.ClientAreaAnimation}, " +
                    $"IsVisible={IsVisible}, Opacity={RootCard.Opacity:0.00}, SlideY={CardSlide.Y:0.0}");
        Dismiss();
        for (int step = 0; step < 12; step++)
        {
            await Task.Delay(20);
            AppLog.Info($"退场 {step * 20 + 20}ms：IsVisible={IsVisible}, Opacity={RootCard.Opacity:0.00}, SlideY={CardSlide.Y:0.0}");
        }

        AppLog.Info($"退场结束：IsVisible={IsVisible}, Opacity={RootCard.Opacity:0.00}, SlideY={CardSlide.Y:0.0}");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Dismiss();

    private static Brush freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
