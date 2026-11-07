using System.Windows;
using System.Windows.Media;
using OsuRoam.Services;

namespace OsuRoam.Controls;

/// <summary>
/// 极简 mania 谱面预览：几条轨道 + 白色方砖 + 粉色长条，按「当前音频位置」往上滚。
///
/// 不做判定、不播声音、不读皮肤——它只是选歌界面里「这首歌打起来长什么样」的一张动图。
/// 时间由外部每帧灌进来（<see cref="Time"/>），控件本身不持有时钟：外面的窗口需要
/// 和 tosu 的预览音频位置对齐，偶尔还要停更/跳变，时钟只能放在那一层。
///
/// 每帧只画「还在画面里」的物件：物件按起点排序，先用二分找到最前面可能露头的一个
/// （往前让出最长长条的时间），再往后画到超出画面上沿为止。
/// </summary>
public sealed class ManiaPreview : FrameworkElement
{
    private static readonly Brush LaneBrush = freeze(new SolidColorBrush(Color.FromRgb(0x0F, 0x14, 0x18)));
    private static readonly Pen LanePen = freeze(new Pen(freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x22, 0x28))), 1));
    private static readonly Brush TapBrush = freeze(gradient(Color.FromRgb(0xF5, 0xF9, 0xFB), Color.FromRgb(0xD4, 0xE0, 0xE8)));
    private static readonly Brush HoldBrush = freeze(new SolidColorBrush(Color.FromRgb(0xE1, 0x51, 0x7B)));
    private static readonly Brush HoldHeadBrush = freeze(gradient(Color.FromRgb(0xFB, 0xF2, 0xF5), Color.FromRgb(0xE9, 0xC9, 0xD3)));
    private static readonly Pen HoldEdgePen = freeze(new Pen(freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0x34, 0x58))), 1));
    private static readonly Brush JudgementBrush = freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x6A, 0x76)));
    private static readonly Brush KeyBrush = freeze(new SolidColorBrush(Color.FromRgb(0x18, 0x1F, 0x25)));
    private static readonly Pen KeyPen = freeze(new Pen(freeze(new SolidColorBrush(Color.FromRgb(0x28, 0x33, 0x3B))), 1));
    private static readonly Brush HitZoneBrush = freeze(new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x12)));

    private ManiaPreviewChart? chart;
    private double time;

    /// <summary>判定线离底边留多高：下面这条带子画键位，不做判定。</summary>
    private const double KeyBandHeight = 26;

    /// <summary>判定线以上的可见时长（秒）：越大越「慢」，也和卡片高度无关地保持信息量。</summary>
    public double VisibleSeconds { get; set; } = 1.25;

    public ManiaPreviewChart? Chart
    {
        get => chart;
        set
        {
            chart = value;
            InvalidateVisual();
        }
    }

    /// <summary>当前音频位置（毫秒，和 <c>.osu</c> 的时间轴同一把尺）。</summary>
    public double Time
    {
        get => time;
        set => time = value;
    }

    /// <summary>画面里现在画了多少个物件（开发用：确认二分查找没漏东西）。</summary>
    public int DrawnNotes { get; private set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        DrawnNotes = 0;
        if (width <= 0 || height <= 0 || chart is null)
            return;

        int keys = chart.KeyCount;
        double laneWidth = width / keys;
        double judgementY = Math.Max(KeyBandHeight + 40, height - KeyBandHeight);
        double pixelsPerMs = judgementY / Math.Max(0.25, VisibleSeconds) / 1000.0;

        drawLanes(drawingContext, width, height, keys, laneWidth, judgementY);

        // 判定线以上才是滚动区，长条越过线就裁掉，别压到底部的键位带子。
        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, width, judgementY)));
        drawNotes(drawingContext, laneWidth, judgementY, pixelsPerMs, keys);
        drawingContext.Pop();

        // 判定线 + 键位带子
        drawingContext.DrawRectangle(JudgementBrush, null, new Rect(0, judgementY - 2, width, 2));
        for (int key = 0; key < keys; key++)
        {
            Rect keyRect = new(key * laneWidth + 2, judgementY + 4, Math.Max(1, laneWidth - 4), KeyBandHeight - 8);
            drawingContext.DrawRoundedRectangle(KeyBrush, KeyPen, keyRect, 3, 3);
        }
    }

    private void drawLanes(DrawingContext drawingContext, double width, double height, int keys,
        double laneWidth, double judgementY)
    {
        drawingContext.DrawRectangle(HitZoneBrush, null, new Rect(0, 0, width, height));

        // 隔列压一层深色：读谱时能一眼看出第几列，也是 mania 皮肤里最常见的做法。
        for (int key = 0; key < keys; key++)
        {
            if ((key & 1) == 1)
                drawingContext.DrawRectangle(LaneBrush, null, new Rect(key * laneWidth, 0, laneWidth, judgementY));
        }

        for (int key = 1; key < keys; key++)
        {
            double x = Math.Round(key * laneWidth) + 0.5;
            drawingContext.DrawLine(LanePen, new Point(x, 0), new Point(x, judgementY));
        }
    }

    private void drawNotes(DrawingContext drawingContext, double laneWidth, double judgementY,
        double pixelsPerMs, int keys)
    {
        ManiaPreviewChart source = chart!;
        IReadOnlyList<PreviewNote> notes = source.Notes;
        double visibleMs = VisibleSeconds * 1000;
        double noteHeight = Math.Clamp(laneWidth * 0.17, 7, 20);
        double noteWidth = Math.Max(2, laneWidth - 6);

        int drawn = 0;
        for (int index = source.FirstIndexAt(time - source.LongestHold - 1); index < notes.Count; index++)
        {
            PreviewNote note = notes[index];
            if (note.Start - time > visibleMs)
                break;
            if (note.End < time)
                continue;

            double headY = judgementY - (note.Start - time) * pixelsPerMs;
            double tailY = judgementY - (note.End - time) * pixelsPerMs;
            double x = note.Column * laneWidth + 3;

            if (note.IsHold && headY - tailY > 0.5)
            {
                double bodyTop = Math.Max(tailY, -noteHeight);
                double bodyBottom = Math.Min(headY, judgementY + noteHeight);
                if (bodyBottom - bodyTop > 0.5)
                {
                    Rect body = new(x, bodyTop, noteWidth, bodyBottom - bodyTop);
                    drawingContext.DrawRectangle(HoldBrush, HoldEdgePen, body);
                }

                // 头尾各一条浅色横条：这是 mania 长条的标志性画法，也让人看清尾巴什么时候到。
                drawingContext.DrawRectangle(HoldHeadBrush, null, new Rect(x, headY, noteWidth, noteHeight));
                drawingContext.DrawRectangle(HoldHeadBrush, null, new Rect(x, tailY, noteWidth, Math.Min(noteHeight, 4)));
            }
            else
            {
                double y = Math.Max(headY, -noteHeight);
                if (y >= judgementY)
                    continue;
                double visibleHeight = Math.Min(noteHeight, judgementY - y);
                drawingContext.DrawRectangle(TapBrush, null, new Rect(x, y, noteWidth, visibleHeight));
            }

            drawn++;
        }

        DrawnNotes = drawn;
    }

    private static Brush freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Pen freeze(Pen pen)
    {
        pen.Freeze();
        return pen;
    }

    private static Brush gradient(Color top, Color bottom)
    {
        LinearGradientBrush brush = new(top, bottom, new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }
}
