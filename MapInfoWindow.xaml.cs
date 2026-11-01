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

    private Window? dockOwner;
    private RecommendationItem? currentItem;
    private bool exiting;

    public MapInfoWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => reposition();
        Loaded += (_, _) => reposition();
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
            parts.Add("历史回收");
        return string.Join(" · ", parts);
    }

    private void buildCapsules(RecommendationItem item)
    {
        CapsulePanel.Children.Clear();
        BeatmapDetail? detail = item.Detail;

        addCapsule("BPM", item.Bpm > 0 ? item.Bpm.ToString("0") : "—");
        addCapsule("时长", TimeSpan.FromSeconds(item.LengthSeconds).ToString(@"m\:ss"));

        if (detail is not null)
        {
            addCapsule("mode", detail.ModeDisplay);
            addCapsule("OD", detail.OverallDifficulty.ToString("0.0"));
            addCapsule("AR", detail.ApproachRate.ToString("0.0"));
            addCapsule("CS", detail.CircleSize.ToString("0.0"));
            addCapsule("HP", detail.HPDrainRate.ToString("0.0"));
            if (detail.HasObjects)
                addCapsule("物件", detail.ObjectCount.ToString());
        }
        else
        {
            addCapsule("详情", "导入后解析", MutedBrush);
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
            PatternRow.Visibility = Visibility.Collapsed;
            Radar.Labels = null;
            Radar.Values = null;
            Bars.Labels = null;
            Bars.Shares = null;
            return;
        }

        PatternRow.Visibility = Visibility.Visible;

        // 7 轴技能强度：各轴独立，适合雷达图。
        if (pattern.SkillAxes.Length == ManiaPattern.SkillPatterns.Length)
        {
            RadarPanel.Visibility = Visibility.Visible;
            Radar.Labels = ManiaPattern.SkillPatternShortLabels;
            Radar.Values = pattern.SkillAxes;
        }
        else
        {
            // 没有技能轴时宁可空着，也不要拿"占比和 = 1"的核心键型去凑雷达图。
            RadarPanel.Visibility = Visibility.Collapsed;
            Radar.Labels = null;
            Radar.Values = null;
        }

        // 6 个核心键型占比：加起来是 1，用直方图看长短（雷达图会误导）。
        Bars.Labels = ManiaPattern.CorePatternLabels;
        Bars.Shares = pattern.ToVector();
    }

    private static string buildDetailLine(RecommendationItem item)
    {
        ManiaPattern? pattern = item.Pattern;
        BeatmapDetail? detail = item.Detail;
        List<string> parts = [];

        if (detail is { HasObjects: true })
        {
            List<string> objects = [$"物件 {detail.ObjectCount}"];
            if (detail.CircleCount > 0)
                objects.Add($"单点 {detail.CircleCount}");
            if (detail.SliderCount > 0)
                objects.Add($"滑条 {detail.SliderCount}");
            if (detail.HoldCount > 0)
                objects.Add($"长条 {detail.HoldCount}");
            if (detail.SpinnerCount > 0)
                objects.Add($"转盘 {detail.SpinnerCount}");
            parts.Add(string.Join(" · ", objects));
        }

        if (pattern is not null)
        {
            parts.Add($"模式 {pattern.ModeTag} · LN {pattern.LnRatio:P0} · NPS {pattern.NotesPerSecond:0.0}");
            parts.Add($"主导键型 {pattern.Category}（{pattern.SkillSource}技能轴）");
            KeyValuePair<string, double>[] top = pattern.SpecificShares
                .OrderByDescending(pair => pair.Value)
                .Take(4)
                .ToArray();
            if (top.Length > 0)
                parts.Add(string.Join(" · ", top.Select(pair => $"{pair.Key} {pair.Value:P0}")));
        }
        else if (item.KeyCount is 0 or 4)
        {
            parts.Add("键型雷达只对 mania 谱面可用（切到 mania 模式并导入后才会出现）");
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
