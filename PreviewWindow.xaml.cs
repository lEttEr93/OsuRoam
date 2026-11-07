using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OsuRoam.Services;

namespace OsuRoam;

/// <summary>
/// 选歌界面的谱面预览窗：贴在信息窗旁边、和信息窗等高，按 tosu 报的预览音频位置
/// 把当前选中谱面的 mania 物件往上滚一遍。
///
/// 为什么要单开一个窗口而不是塞进信息窗：信息窗是「这谱面是什么样」的静态卡片，
/// 预览是「打起来什么样」的动画，两者高度都要吃满，塞一起两边都憋屈。
///
/// 时钟为什么要本地外推：tosu 的 WebSocket 快照不是每帧都有（默认几十毫秒一条），
/// 直接拿快照里的毫秒数当帧时间，画面就会一格一格跳。所以这里记下「值 + 到达时刻」，
/// 每帧按墙上时间往前推，再用 15% 的力度往 tosu 的值上拉；差值超过 200ms（换歌、
/// 预览跳回去）就直接对齐，不做平滑；200ms 没有新值（预览暂停 / 断流）画面冻住。
/// </summary>
public partial class PreviewWindow : Window
{
    /// <summary>量不到信息窗高度时退回到这个值（和信息窗自己的退路一致）。</summary>
    private const double FallbackHeight = 420;

    private const double Gap = 8;

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly PreviewClock previewClock = new();

    private Window? dockOwner;
    private ManiaPreviewChart? chart;
    private string? messageKey;

    private double cardHeight;
    private bool exiting;
    private bool adjusting;
    private bool running;

    private double lastFrame;
    private double lastHousekeeping;
    private double zeroClockSince = -1;
    private double probeWarmup;
    private double dpiScale = 1;
    private bool freezeRendering;
    private readonly bool layeredWindow;

    private int shownSecond = -1;
    private double shownProgress = -1;
    private string? shownSpan;

    /// <summary>开发用：帧率探针的帧间隔采样，null = 没在采样。</summary>
    private List<double>? probeFrames;

    /// <param name="layered">
    /// 是否用分层窗口（<c>AllowsTransparency=True</c>）。默认不用 —— 分层窗口在 WPF 里
    /// 只能走软件合成（整窗每帧重新栅格化 + 一次 alpha 贴图），实测帧率只有普通窗口的
    /// 一半左右（645x630 物理像素：14.3ms vs 6.3ms 一帧，见 docs\1.5.5-谱面预览.md）。
    /// 控件自己的 OnRender 只有 0.06ms，瓶颈完全在这一层。
    /// </param>
    public PreviewWindow(bool layered = false)
    {
        InitializeComponent();
        layeredWindow = layered;
        if (!layered)
        {
            AllowsTransparency = false;
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x16));
            // 普通窗口没有 8px 透明边距可用：卡片直接铺满，四角交给 DWM 切（Win11 有效）。
            RootCard.Margin = new Thickness(0);
            RootCard.CornerRadius = new CornerRadius(0);
            SourceInitialized += (_, _) => roundWindowCorners();
        }

        SizeChanged += (_, _) => reposition();
        Loaded += (_, _) =>
        {
            dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            reposition();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                startFrames();
            else
                stopFrames();
        };
        Loc.Changed += onLanguageChanged;
        Closed += (_, _) => Loc.Changed -= onLanguageChanged;
    }

    /// <summary>并排参照的窗口：信息窗可见时贴着它放，高度也跟它走。</summary>
    public Window? Anchor { get; set; }

    /// <summary>换语言只影响这里拼的提示文案；曲名/谱师来自 tosu，不跟着变。</summary>
    private void onLanguageChanged()
    {
        if (messageKey is not null)
            MessageText.Text = Loc.T(messageKey);
    }

    /// <summary>显示（或换）一张谱面。已经可见时只换内容，不重放动画。</summary>
    public void ShowChart(ManiaPreviewChart preview, string title, string meta)
    {
        chart = preview;
        Field.Chart = preview;
        TrackTitle.Text = title;
        TrackMeta.Text = meta;

        previewClock.Reset();
        hideMessage();

        SizeToContent = SizeToContent.Manual;
        reposition();

        if (IsVisible)
        {
            startFrames();
            return;
        }

        exiting = false;
        Show();
        PlayEntrance();
    }

    /// <summary>灌 tosu 的预览音频位置（毫秒）。</summary>
    public void SetTime(double milliseconds) =>
        previewClock.Sync(milliseconds, clock.Elapsed.TotalMilliseconds);

    /// <summary>信息窗量完高度以后告诉这里一声（两者等高是设计要求）。</summary>
    public void SetCardHeight(double height)
    {
        if (height <= 1 || Math.Abs(cardHeight - height) < 0.5)
            return;

        cardHeight = height;
        reposition();
    }

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
            if (layeredWindow)
            {
                DoubleAnimation fade = new(1, 0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                    FillBehavior = FillBehavior.HoldEnd
                };
                fade.Completed += (_, _) => finishExit();
                RootCard.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            }
            else
            {
                // 普通窗口没有透明底：把卡片淡成半透明只会剩一块深色方块，所以只做位移+收缩。
                DoubleAnimation slideOut = new(0, 8, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                    FillBehavior = FillBehavior.HoldEnd
                };
                slideOut.Completed += (_, _) => finishExit();
                CardSlide.BeginAnimation(TranslateTransform.YProperty, slideOut, HandoffBehavior.SnapshotAndReplace);
                return;
            }

            DoubleAnimation slide = new(0, 8, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.HoldEnd
            };
            CardSlide.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
        }
        catch (Exception exception)
        {
            AppLog.Error("谱面预览窗退场动画失败", exception);
            finishExit();
        }
    }

    private void finishExit()
    {
        RootCard.BeginAnimation(OpacityProperty, null);
        CardSlide.BeginAnimation(TranslateTransform.YProperty, null);
        CardSlide.Y = 0;
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
            // 普通窗口的底色是不透明的：卡片从 0 淡到 1 只会变成「先出现一块深色方块」，
            // 所以那种情况只做位移 + 轻微放大。
            if (layeredWindow)
            {
                DoubleAnimation fade = new(0, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                };
                RootCard.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            }

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
            AppLog.Error("谱面预览窗入场动画失败", exception);
            RootCard.Opacity = 1;
        }
    }

    // ---- 时钟 ----

    private void startFrames()
    {
        if (running)
            return;

        running = true;
        lastFrame = clock.Elapsed.TotalMilliseconds;
        lastHousekeeping = lastFrame - 400;
        CompositionTarget.Rendering += onRendering;
    }

    private void stopFrames()
    {
        if (!running)
            return;

        running = false;
        CompositionTarget.Rendering -= onRendering;
    }

    private void onRendering(object? sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalMilliseconds;
        double delta = Math.Clamp(now - lastFrame, 0, 120);
        lastFrame = now;

        if (probeFrames is not null && now > probeWarmup)
            probeFrames.Add(delta);

        previewClock.Advance(now, delta);

        Field.Time = previewClock.Time;
        if (!freezeRendering)
            Field.Redraw();

        if (now - lastHousekeeping < 300)
            return;

        lastHousekeeping = now;
        updateReadouts(now);
        reposition();
    }

    /// <summary>推进一步：平时按墙上时间走，被 tosu 的值轻轻拉住；停更或跳变时单独处理。</summary>
    private void updateReadouts(double now)
    {
        int second = (int)Math.Floor(previewClock.Time / 1000);
        if (second != shownSecond)
        {
            shownSecond = second;
            ClockText.Text = formatTime(previewClock.Time);
        }

        if (chart is null)
        {
            SpanText.Text = string.Empty;
            return;
        }

        double span = chart.LastNoteTime;
        double progress = span > 1 ? Math.Clamp(previewClock.Time / span, 0, 1) : 0;
        string spanText = formatTime(span);
        if (Math.Abs(progress - shownProgress) > 0.005)
        {
            shownProgress = progress;
            // 只改 RenderTransform：改宽度/列宽会引发布局，正好在帧时间上留下尖峰。
            ProgressScale.ScaleX = progress;
        }

        if (!string.Equals(shownSpan, spanText, StringComparison.Ordinal))
        {
            shownSpan = spanText;
            SpanText.Text = spanText;
        }

        // tosu 一直报 0（它自己没读到预览音频位置）：与其让人盯着一动不动的空轨道，不如说清楚。
        if (previewClock.HasSource && previewClock.SourceTime > 1)
        {
            zeroClockSince = -1;
            hideMessage();
            return;
        }

        if (zeroClockSince < 0)
            zeroClockSince = now;
        else if (now - zeroClockSince > 2500)
            showMessage("preview.noclock");
    }

    private static string formatTime(double milliseconds)
    {
        double clamped = Math.Max(0, milliseconds);
        return TimeSpan.FromMilliseconds(clamped).ToString(@"m\:ss");
    }

    private void showMessage(string key)
    {
        if (string.Equals(messageKey, key, StringComparison.Ordinal))
            return;

        messageKey = key;
        MessageText.Text = Loc.T(key);
        MessagePanel.Visibility = Visibility.Visible;
    }

    private void hideMessage()
    {
        if (messageKey is null)
            return;

        messageKey = null;
        MessagePanel.Visibility = Visibility.Collapsed;
    }

    // ---- 摆位 ----

    /// <summary>挂在主窗口下面（跟随移动/缩放、不占任务栏），和信息窗一样。</summary>
    public void DockTo(Window owner)
    {
        dockOwner = owner;
        Owner = owner;
        owner.LocationChanged += (_, _) => reposition();
        owner.SizeChanged += (_, _) => reposition();
        owner.Closed += (_, _) => Close();
        reposition();
    }

    private void reposition()
    {
        if (dockOwner is null || adjusting)
            return;

        adjusting = true;
        try
        {
            Rect work = SystemParameters.WorkArea;
            Window? anchor = Anchor is { IsVisible: true } ? Anchor : null;

            double width = anchor?.ActualWidth > 1 ? anchor.ActualWidth : Math.Max(360, dockOwner.ActualWidth);
            double height = cardHeight > 0
                ? cardHeight
                : anchor?.ActualHeight > 1 ? anchor.ActualHeight : FallbackHeight;
            height = Math.Clamp(height, 240, Math.Max(240, work.Height - 8));

            if (Math.Abs(Width - width) > 0.5)
                Width = width;
            if (Math.Abs(Height - height) > 0.5)
                Height = height;

            double top;
            double left;
            if (anchor is not null)
            {
                top = anchor.Top;
                left = anchor.Left + anchor.ActualWidth + Gap;
                // 右边放不下就翻到信息窗左边，再不行才贴屏幕右缘。
                if (left + Width > work.Right - 4)
                    left = anchor.Left - Width - Gap;
            }
            else
            {
                // 没有信息窗可依：和信息窗默认的落点对齐（主窗口下方那一条）。
                top = dockOwner.Top + Math.Max(64, dockOwner.ActualHeight) - 12;
                left = dockOwner.Left + dockOwner.ActualWidth + Gap;
            }

            double maxLeft = Math.Max(work.Left + 4, work.Right - Width - 4);
            left = Math.Clamp(left, work.Left + 4, maxLeft);
            if (top + Height > work.Bottom - 4)
                top = work.Bottom - Height - 4;
            top = Math.Max(work.Top + 4, top);

            if (Math.Abs(Left - left) > 0.5)
                Left = left;
            if (Math.Abs(Top - top) > 0.5)
                Top = top;
        }
        finally
        {
            adjusting = false;
        }
    }

    /// <summary>开发用：把当前状态打一行，确认高度跟信息窗一致、时钟在走。</summary>
    internal string Describe() => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"h={ActualHeight:0.0} w={ActualWidth:0.0} top={Top:0.0} left={Left:0.0} " +
        $"chart={chart?.KeyCount ?? 0}K/{chart?.Notes.Count ?? 0} " +
        $"time={previewClock.Time:0}ms source={previewClock.SourceTime:0}ms notes={Field.DrawnNotes}");

    /// <summary>开发用：开始采样帧间隔（前 600ms 丢掉，避开窗口刚弹出的抖动）。</summary>
    internal void BeginFpsProbe(bool freeze = false)
    {
        freezeRendering = freeze;
        probeWarmup = clock.Elapsed.TotalMilliseconds + 600;
        probeFrames = [];
    }

    /// <summary>开发用：帧间隔 + 控件自身绘制耗时的统计。</summary>
    internal string DescribeFps()
    {
        List<double> frames = probeFrames ?? [];
        if (frames.Count < 10)
            return $"样本太少（{frames.Count} 帧），窗口可能没在渲染";

        frames.Sort();
        double p50 = frames[frames.Count / 2];
        double p90 = frames[Math.Min(frames.Count - 1, (int)(frames.Count * 0.90))];
        double p95 = frames[Math.Min(frames.Count - 1, (int)(frames.Count * 0.95))];
        double p99 = frames[Math.Min(frames.Count - 1, (int)(frames.Count * 0.99))];
        int slow = frames.Count(value => value > 20);
        double average = frames.Sum() / frames.Count;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{frames.Count} 帧｜p50={p50:0.0} p90={p90:0.0} p95={p95:0.0} p99={p99:0.0} max={frames[^1]:0.0}ms " +
            $"平均={average:0.0}ms ≈{1000 / p50:0}fps｜>20ms {slow} 帧｜控件 OnRender 平均={Field.AverageRenderMilliseconds:0.00}ms " +
            $"（{Field.RenderSamples} 次）｜逻辑 {ActualWidth:0}x{ActualHeight:0}｜" +
            $"物理 {(int)Math.Round(ActualWidth * dpiScale)}x{(int)Math.Round(ActualHeight * dpiScale)}@{dpiScale:0.##}x");
    }

    /// <summary>Win11 的 DWM 圆角；失败（旧系统）就保持方角，不影响任何功能。</summary>
    private void roundWindowCorners()
    {
        try
        {
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int preference = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
        }
        catch (Exception exception)
        {
            AppLog.Info($"预览窗圆角设置失败（不影响功能）：{exception.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Dismiss();
}
