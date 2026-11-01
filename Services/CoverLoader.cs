using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OsuRoam.Services;

/// <summary>
/// 封面加载。不用 XAML 的 Image.Source="http://..."（那条通道不走程序的代理配置，
/// 失败时静默空白），改为用 HttpClient 拉字节再解码，和 API 走同一套网络路径。
///
/// 线程注意：解码必须离开 UI 线程。之前 await 之后会回到 UI 上下文再 EndInit，
/// 一张封面解码十几毫秒，预取/翻页时会直接吃掉动画的帧预算（渲染诊断里的 p95 尖峰）。
/// </summary>
/// <summary>
/// 一张曲绘的几种用途：
/// <paramref name="Cover"/> 是 256px 的清晰版（缩略图 / 需要细节时用），
/// <paramref name="Backdrop"/> 是 96px 且下边缘已经渐隐的版本（卡片背景），
/// <paramref name="Fill"/> 是重糊的 96px 版（铺满整卡/整块，只用来给下半部分带颜色），
/// <paramref name="Bar"/> 是整幅再横向羽化的版本（迷你播放器里居中铺一条，边界融进模糊底）。
/// </summary>
public sealed record CoverArt(ImageSource? Cover, ImageSource? Backdrop, ImageSource? Fill, ImageSource? Bar);

public static class CoverLoader
{
    /// <summary>缩略图解码宽度：最坏的用途是信息窗 56px 头像，256 足够，还省解码时间。</summary>
    private const int ThumbnailWidth = 256;

    /// <summary>
    /// 整幅曲绘底图宽度。96px 靠放大出模糊会变马赛克，256px + 真高斯后放到卡片宽度只放大
    /// ~1.4 倍，观感是"糊"而不是"块"。
    /// </summary>
    private const int BackdropWidth = 256;

    /// <summary>整幅曲绘的模糊 σ（256px 底图）。3 ≈ 卡片上的"柔但还认得出画面"。</summary>
    private const double BandSigma = 3.0;

    /// <summary>铺满卡片的填充层：只留 96px 再重糊，避免下半部分出现细节、抢文字。</summary>
    private const int FillWidth = 96;

    /// <summary>填充层的模糊 σ（96px 底图，放大到卡片约 3.8 倍，等效很重的糊）。</summary>
    private const double FillSigma = 4.0;

    /// <summary>背影从这一比例开始往下渐隐（0.3 = 上方 30% 完全保留）。</summary>
    private const double FadeStart = 0.30;

    /// <summary>迷你播放器用的左右羽化宽度（0.22 = 两侧各 22% 渐隐）。</summary>
    private const double EdgeFade = 0.22;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<int, CoverArt> Cache = [];
    private static readonly HashSet<int> Loading = [];

    /// <summary>下载 + 解码超过这个耗时就记一条日志，便于确认卡顿来源。</summary>
    private const long SlowLoadMilliseconds = 60;

    public static async void Load(int beatmapsetId, Action<CoverArt> apply)
    {
        if (beatmapsetId <= 0)
        {
            apply(new CoverArt(null, null, null, null));
            return;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(beatmapsetId, out CoverArt? cached))
            {
                apply(cached);
                return;
            }
            if (!Loading.Add(beatmapsetId))
                return;
        }

        try
        {
            Stopwatch clock = Stopwatch.StartNew();

            // ConfigureAwait(false)：后面的解码留在后台线程，不再回到 UI 上下文。
            byte[] bytes = await Http
                .GetByteArrayAsync($"https://assets.ppy.sh/beatmaps/{beatmapsetId}/covers/cover.jpg")
                .ConfigureAwait(false);

            CoverArt art = decode(bytes);

            long elapsed = clock.ElapsedMilliseconds;
            if (elapsed > SlowLoadMilliseconds)
                AppLog.Info($"曲绘加载较慢：set={beatmapsetId}，{elapsed}ms（解码已在后台线程）");

            lock (Cache) Cache[beatmapsetId] = art;
            post(() => apply(art));
        }
        catch (Exception exception)
        {
            AppLog.Error($"加载曲绘失败：set={beatmapsetId}", exception);
            lock (Cache) Cache[beatmapsetId] = new CoverArt(null, null, null, null);
            post(() => apply(new CoverArt(null, null, null, null)));
        }
        finally
        {
            lock (Cache) Loading.Remove(beatmapsetId);
        }
    }

    private static CoverArt decode(byte[] bytes)
    {
        try
        {
            BitmapImage image = new();
            using (MemoryStream stream = new(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = ThumbnailWidth;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            BitmapSource? band = blurBitmap(scaleDown(image, BackdropWidth), BandSigma);
            BitmapSource? fill = blurBitmap(scaleDown(image, FillWidth), FillSigma);
            return new CoverArt(
                image,
                createBackdrop(band, fade: true),
                fill,
                createBarBackdrop(band));
        }
        catch (Exception exception)
        {
            AppLog.Error("曲绘解码失败", exception);
            return new CoverArt(null, null, null, null);
        }
    }

    /// <summary>把解码出来的 256px 图再缩到指定宽度（填充层只需要 96px）。</summary>
    private static BitmapSource? scaleDown(BitmapSource source, int width)
    {
        try
        {
            if (source.PixelWidth <= width)
                return source;

            double scale = (double)width / source.PixelWidth;
            TransformedBitmap scaled = new(source, new ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }
        catch (Exception exception)
        {
            AppLog.Error("曲绘背影缩放失败", exception);
            return null;
        }
    }

    /// <summary>
    /// 生成 96px 背影。<paramref name="fade"/> 为 true 时把下 70% 的 alpha 按 smoothstep 收到 0：
    /// 卡片里整幅曲绘只占顶部一条，这样下边缘不会有硬横线；
    /// 迷你播放器那种"按高度填满"的场景不需要（也不能）渐隐，用 false。
    /// </summary>
    private static ImageSource? createBackdrop(BitmapSource? scaled, bool fade)
    {
        if (scaled is null)
            return null;
        if (!fade)
            return scaled;

        try
        {
            BitmapSource pixels = scaled.Format == PixelFormats.Bgra32 || scaled.Format == PixelFormats.Pbgra32
                ? scaled
                : new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);

            int width = pixels.PixelWidth;
            int height = pixels.PixelHeight;
            if (width <= 0 || height <= 0)
                return scaled;

            int stride = width * 4;
            byte[] buffer = new byte[stride * height];
            pixels.CopyPixels(buffer, stride, 0);

            for (int y = 0; y < height; y++)
            {
                double progress = height <= 1 ? 1 : y / (double)(height - 1);
                double t = (progress - FadeStart) / (1 - FadeStart);
                if (t <= 0)
                    continue;

                // smoothstep：两端导数为 0，交界处不会出现"折线"感
                double keep = 1 - smoothstep(Math.Clamp(t, 0, 1));
                int rowStart = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int index = rowStart + x * 4;
                    // Pbgra32 是预乘 alpha：整体乘 keep 等价于"再多一层透明度"，颜色不会偏亮
                    buffer[index] = (byte)(buffer[index] * keep);
                    buffer[index + 1] = (byte)(buffer[index + 1] * keep);
                    buffer[index + 2] = (byte)(buffer[index + 2] * keep);
                    buffer[index + 3] = (byte)(buffer[index + 3] * keep);
                }
            }

            BitmapSource faded = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
            faded.Freeze();
            return faded;
        }
        catch (Exception exception)
        {
            AppLog.Error("曲绘背影渐隐失败", exception);
            return scaled;
        }
    }

    /// <summary>
    /// 迷你播放器用的版本：左右各 <see cref="EdgeFade"/> 横向羽化。
    /// 那条只有 64px 高，曲绘按高度铺满后左右会留出模糊底，"硬边"就出现在图的两侧，
    /// 所以这里做的是横向渐隐（纵向不渐隐）。
    /// </summary>
    private static ImageSource? createBarBackdrop(BitmapSource? scaled)
    {
        if (scaled is null)
            return null;

        try
        {
            BitmapSource pixels = scaled.Format == PixelFormats.Bgra32 || scaled.Format == PixelFormats.Pbgra32
                ? scaled
                : new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);

            int width = pixels.PixelWidth;
            int height = pixels.PixelHeight;
            if (width <= 1 || height <= 0)
                return scaled;

            int stride = width * 4;
            byte[] buffer = new byte[stride * height];
            pixels.CopyPixels(buffer, stride, 0);

            for (int x = 0; x < width; x++)
            {
                double progress = x / (double)(width - 1);
                double edge = Math.Min(progress, 1 - progress) / EdgeFade;
                if (edge >= 1)
                    continue;

                double keep = smoothstep(Math.Clamp(edge, 0, 1));
                for (int y = 0; y < height; y++)
                {
                    int index = y * stride + x * 4;
                    buffer[index] = (byte)(buffer[index] * keep);
                    buffer[index + 1] = (byte)(buffer[index + 1] * keep);
                    buffer[index + 2] = (byte)(buffer[index + 2] * keep);
                    buffer[index + 3] = (byte)(buffer[index + 3] * keep);
                }
            }

            BitmapSource feathered = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
            feathered.Freeze();
            return feathered;
        }
        catch (Exception exception)
        {
            AppLog.Error("曲绘横向羽化失败", exception);
            return scaled;
        }
    }

    /// <summary>
    /// 可分离高斯模糊：先横向一卷、再纵向一卷。
    /// 只在小图上做（256x71），核半径 ceil(3σ)=5，单张约 1~2ms，放在后台线程里可以忽略。
    /// 边缘用"复制边界像素"而不是采样透明，避免把图形边缘糊淡——边缘的渐隐/羽化是后面单独烘的。
    /// </summary>
    private static BitmapSource? blurBitmap(BitmapSource? source, double sigma)
    {
        if (source is null || sigma <= 0)
            return source;

        try
        {
            BitmapSource pixels = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

            int width = pixels.PixelWidth;
            int height = pixels.PixelHeight;
            if (width < 3 || height < 3)
                return pixels;

            int stride = width * 4;
            byte[] buffer = new byte[stride * height];
            pixels.CopyPixels(buffer, stride, 0);

            float[] kernel = gaussianKernel(sigma);
            int radius = kernel.Length / 2;
            byte[] scratch = new byte[buffer.Length];
            convolveHorizontal(buffer, scratch, width, height, kernel, radius);
            convolveVertical(scratch, buffer, width, height, kernel, radius);

            BitmapSource blurred = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
            blurred.Freeze();
            return blurred;
        }
        catch (Exception exception)
        {
            AppLog.Error("曲绘模糊失败", exception);
            return source;
        }
    }

    /// <summary>归一化的一维高斯核，半径 ceil(3σ)（覆盖 99.7% 的能量）。</summary>
    private static float[] gaussianKernel(double sigma)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        float[] kernel = new float[radius * 2 + 1];
        double sum = 0;
        for (int offset = -radius; offset <= radius; offset++)
        {
            double weight = Math.Exp(-(offset * offset) / (2 * sigma * sigma));
            kernel[offset + radius] = (float)weight;
            sum += weight;
        }

        for (int index = 0; index < kernel.Length; index++)
            kernel[index] = (float)(kernel[index] / sum);
        return kernel;
    }

    private static void convolveHorizontal(byte[] source, byte[] target, int width, int height, float[] kernel, int radius)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                for (int tap = -radius; tap <= radius; tap++)
                {
                    int index = row + Math.Clamp(x + tap, 0, width - 1) * 4;
                    float weight = kernel[tap + radius];
                    b += source[index] * weight;
                    g += source[index + 1] * weight;
                    r += source[index + 2] * weight;
                    a += source[index + 3] * weight;
                }

                int outIndex = row + x * 4;
                target[outIndex] = clampToByte(b);
                target[outIndex + 1] = clampToByte(g);
                target[outIndex + 2] = clampToByte(r);
                target[outIndex + 3] = clampToByte(a);
            }
        }
    }

    private static void convolveVertical(byte[] source, byte[] target, int width, int height, float[] kernel, int radius)
    {
        int stride = width * 4;
        for (int x = 0; x < width; x++)
        {
            int column = x * 4;
            for (int y = 0; y < height; y++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                for (int tap = -radius; tap <= radius; tap++)
                {
                    int index = Math.Clamp(y + tap, 0, height - 1) * stride + column;
                    float weight = kernel[tap + radius];
                    b += source[index] * weight;
                    g += source[index + 1] * weight;
                    r += source[index + 2] * weight;
                    a += source[index + 3] * weight;
                }

                int outIndex = y * stride + column;
                target[outIndex] = clampToByte(b);
                target[outIndex + 1] = clampToByte(g);
                target[outIndex + 2] = clampToByte(r);
                target[outIndex + 3] = clampToByte(a);
            }
        }
    }

    private static byte clampToByte(float value) =>
        value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)(value + 0.5f);

    private static double smoothstep(double t) => t * t * (3 - 2 * t);

    /// <summary>把回写 ImageSource 的动作送回 UI 线程；没有 Application 时直接执行。</summary>
    private static void post(Action action)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }
}
