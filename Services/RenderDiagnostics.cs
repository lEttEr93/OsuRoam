using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OsuRoam.Services;

/// <summary>
/// 渲染诊断：只在需要时采样一小段帧间隔，用来判断"掉帧"是动画写慢了，
/// 还是整个窗口走了软件渲染路径。
///
/// 注意：CompositionTarget.Rendering 一旦常驻订阅会让渲染循环一直空转，
/// 所以这里只在请求时订阅 1.5 秒，采完立刻退订。
/// </summary>
public static class RenderDiagnostics
{
    private static readonly HashSet<string> measured = [];
    private static bool sampling;
    private static bool environmentLogged;

    /// <summary>启动时打一行环境信息：渲染层级决定后面所有优化方向。</summary>
    public static void LogEnvironment()
    {
        if (environmentLogged)
            return;
        environmentLogged = true;

        try
        {
            int tier = RenderCapability.Tier >> 16;
            string tierText = tier switch
            {
                0 => "0（软件渲染）",
                1 => "1（部分硬件加速）",
                2 => "2（完整硬件加速）",
                _ => tier.ToString()
            };
            AppLog.Info($"渲染环境：Tier={tierText}, DWM合成={DwmCompositionEnabled()}, " +
                        $"像素着色器={RenderCapability.IsPixelShaderVersionSupported(2, 0)}, " +
                        $"系统动画={SystemParameters.ClientAreaAnimation}");
        }
        catch (Exception exception)
        {
            AppLog.Error("无法读取渲染环境", exception);
        }
    }

    /// <summary>对某个动画采样一次帧间隔，同一标签只采一次。</summary>
    public static void MeasureOnce(string tag)
    {
        if (sampling || !measured.Add(tag))
            return;

        sampling = true;
        List<double> deltas = [];
        Stopwatch clock = Stopwatch.StartNew();
        double last = 0;
        EventHandler? handler = null;

        handler = (_, _) =>
        {
            double now = clock.Elapsed.TotalMilliseconds;
            double delta = now - last;
            last = now;
            if (delta > 0 && delta < 500)
                deltas.Add(delta);

            if (now < 1500)
                return;

            CompositionTarget.Rendering -= handler;
            sampling = false;
            log(tag, deltas);
        };

        CompositionTarget.Rendering += handler;
    }

    private static void log(string tag, List<double> deltas)
    {
        try
        {
            if (deltas.Count < 5)
            {
                AppLog.Info($"帧间隔[{tag}]：样本太少（{deltas.Count} 帧），期间可能没有持续渲染");
                return;
            }

            deltas.Sort();
            double p50 = deltas[(int)(deltas.Count * 0.50)];
            double p95 = deltas[Math.Min(deltas.Count - 1, (int)(deltas.Count * 0.95))];
            double max = deltas[^1];
            double fps = 1000.0 / p50;

            string verdict = p95 <= 20 ? "流畅"
                : p95 <= 34 ? "轻微掉帧"
                : "明显掉帧";

            AppLog.Info($"帧间隔[{tag}]：p50={p50:0.0}ms p95={p95:0.0}ms max={max:0.0}ms " +
                        $"≈{fps:0}fps（{verdict}），样本 {deltas.Count} 帧");
        }
        catch (Exception exception)
        {
            AppLog.Error("帧间隔统计失败", exception);
        }
    }

    private static bool DwmCompositionEnabled()
    {
        try
        {
            return DwmIsCompositionEnabled(out bool enabled) == 0 && enabled;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 开发用：把窗口当前画面渲染成 PNG。用来在改字体/配色后肉眼对比，
    /// 免得靠"启动看一眼"。透明窗口会带 alpha 通道，直接看没问题。
    /// </summary>
    public static void CaptureWindow(Window window, string path, double scale = 2.0)
    {
        try
        {
            window.UpdateLayout();
            int width = (int)Math.Ceiling(Math.Max(1, window.ActualWidth) * scale);
            int height = (int)Math.Ceiling(Math.Max(1, window.ActualHeight) * scale);
            RenderTargetBitmap bitmap = new(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(window);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
            AppLog.Info($"截图已保存：{path}（{width}x{height}）");
        }
        catch (Exception exception)
        {
            AppLog.Error("截图失败", exception);
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(out bool enabled);
}
