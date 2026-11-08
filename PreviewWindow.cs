using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using OsuRoam.Controls;
using OsuRoam.Services;

namespace OsuRoam;

/// <summary>
/// 谱面预览的对外门面：它只是信息窗的一个子页面，所以对外只暴露「挂到哪个窗口下面」。
///
/// 真正的画面在 <see cref="PreviewOverlay"/> 里 —— 自己的窗口 + D3D11 合成交换链，
/// 除音符外全部透明，完全不经过 WPF 的渲染线程。
/// </summary>
public sealed class PreviewWindow : IDisposable
{
    /// <summary>赛道宽度（DIP）。瘦身过一次：原来是和信息窗等宽的 430。</summary>
    private const double FieldWidthDip = 301;

    /// <summary>和锚点窗口之间的间距（DIP）。</summary>
    private const double GapDip = 10;

    private readonly PreviewOverlay overlay = new();
    private Window? anchor;

    public PreviewWindow(bool layered = false)
    {
        if (layered)
            AppLog.Info("预览：分层窗口模式已废弃（子窗口 + 合成交换链不支持分层窗口）");
    }

    public event EventHandler? Closed;

    /// <summary>预览永远跟着信息窗：这个为空就不显示。</summary>
    public Window? Anchor
    {
        get => anchor;
        set
        {
            anchor = value;
            if (value is null)
                dismiss();
            else
                applyBounds();
        }
    }

    public bool Topmost { get; set; } = true;

    public double Width { get; set; } = FieldWidthDip;

    public double Height { get; set; }

    public double ActualHeight => measureAnchor();

    /// <summary>一屏覆盖多少毫秒（越小越快，对应游戏里的流速）。</summary>
    public double VisibleSeconds
    {
        get => overlay.VisibleSeconds;
        set => overlay.VisibleSeconds = value;
    }

    /// <summary>延迟补偿（毫秒）。</summary>
    public double LatencyMilliseconds
    {
        get => overlay.LatencyMilliseconds;
        set => overlay.LatencyMilliseconds = value;
    }

    public void DockTo(Window main)
    {
        // 覆盖层是独立窗口，不需要 WPF 宿主；只记下主窗，免得它被当成孤儿窗口。
        _ = main;
    }

    /// <summary>信息窗高度变了就跟着改（预览和信息窗等高是设计要求）。</summary>
    public void SetCardHeight(double height)
    {
        Height = height;
        applyBounds();
    }

    public void ShowChart(ManiaPreviewChart chart, string title, string meta)
    {
        // 标题/作者/时间戳都不要了：画面上只有音符本身。
        _ = title;
        _ = meta;
        if (anchor is null)
        {
            AppLog.Info("预览：没有信息窗，不显示（它是信息窗的子页面）");
            return;
        }

        applyBounds();
        overlay.SetOwner(new WindowInteropHelper(anchor).Handle);
        overlay.Show(chart);
    }

    public void SetTime(double milliseconds) => overlay.Sync(milliseconds);

    public void CaptureSurface(string path) => overlay.CaptureFrame(path);

    public void BeginFpsProbe() => overlay.ResetFrameStats();

    public string Describe() =>
        $"时间 {overlay.FrameIntervals.Count} 帧样本｜本帧画出 {overlay.DrawnNotes} 个音符｜" +
        $"流速 {overlay.VisibleSeconds:0.00}s/屏｜补偿 {overlay.LatencyMilliseconds:0}ms｜" +
        $"窗口 {Width:0}x{Height:0} DIP";

    public string DescribeFps()
    {
        List<double> frames = [.. overlay.FrameIntervals];
        if (frames.Count < 10)
            return $"样本太少（{frames.Count} 帧），渲染线程可能还没起来";

        frames.Sort();
        double p50 = frames[frames.Count / 2];
        double p90 = frames[Math.Min(frames.Count - 1, (int)(frames.Count * 0.90))];
        double p95 = frames[Math.Min(frames.Count - 1, (int)(frames.Count * 0.95))];
        double p99 = frames[Math.Min(frames.Count - 1, (int)(frames.Count * 0.99))];
        int slow = frames.Count(value => value > 20);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{frames.Count} 帧｜p50={p50:0.0} p90={p90:0.0} p95={p95:0.0} p99={p99:0.0} max={frames[^1]:0.0}ms " +
            $"≈{1000 / p50:0}fps｜>20ms {slow} 帧｜绘制 {overlay.LastDrawMilliseconds:0.00}ms｜" +
            $"窗口 {Width:0}x{Height:0} DIP｜流速 {overlay.VisibleSeconds:0.00}s/屏");
    }

    public void Close()
    {
        dismiss();
        overlay.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Close();

    /// <summary>收起画面（保留窗口，等着下一次换歌）。</summary>
    public void Dismiss() => dismiss();

    private void dismiss()
    {
        overlay.Hide();
    }

    /// <summary>
    /// 摆位：贴在信息窗右侧、与它等高；右边放不下就翻到左侧。
    /// 全程用**物理像素**（信息窗的 GetWindowRect 就是物理像素），不掺 WPF 的 DIP。
    /// </summary>
    private void applyBounds()
    {
        if (anchor is null || !anchor.IsVisible)
            return;

        IntPtr hwnd = new WindowInteropHelper(anchor).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out Rect anchorRect))
            return;

        double dpi = dpiOf(hwnd);
        int width = (int)Math.Round((Width > 1 ? Width : FieldWidthDip) * dpi);
        int height = (int)Math.Round((Height > 1 ? Height : anchorRect.Bottom - anchorRect.Top) * dpi);
        if (Height <= 1)
            height = anchorRect.Bottom - anchorRect.Top;

        int gap = (int)Math.Round(GapDip * dpi);
        int left = anchorRect.Right + gap;
        int screenWidth = GetSystemMetrics(0);
        if (left + width > screenWidth)
            left = anchorRect.Left - gap - width;

        overlay.SetBounds(left, anchorRect.Top, width, height);
    }

    private static double dpiOf(IntPtr hwnd)
    {
        try
        {
            uint dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private double measureAnchor()
    {
        if (anchor is null)
            return 0;
        IntPtr hwnd = new WindowInteropHelper(anchor).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out Rect rect))
            return anchor.ActualHeight;
        double dpi = dpiOf(hwnd);
        return (rect.Bottom - rect.Top) / dpi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
