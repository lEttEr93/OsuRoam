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
/// <summary>一张曲绘的两种用途：<paramref name="Cover"/> 给缩略图，<paramref name="Backdrop"/> 给卡片背景。</summary>
public sealed record CoverArt(ImageSource? Cover, ImageSource? Backdrop);

public static class CoverLoader
{
    /// <summary>缩略图解码宽度：最坏的用途是信息窗 56px 头像，256 足够，还省解码时间。</summary>
    private const int ThumbnailWidth = 256;

    /// <summary>
    /// 背影宽度：卡片背景要铺到 ~360px，这里只留 96px，剩下交给 WPF 放大。
    /// 放大本身就是天然的模糊，比挂 BlurEffect 便宜得多，也不会把圆角裁切糊成直角。
    /// </summary>
    private const int BackdropWidth = 96;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<int, CoverArt> Cache = [];
    private static readonly HashSet<int> Loading = [];

    /// <summary>下载 + 解码超过这个耗时就记一条日志，便于确认卡顿来源。</summary>
    private const long SlowLoadMilliseconds = 60;

    public static async void Load(int beatmapsetId, Action<CoverArt> apply)
    {
        if (beatmapsetId <= 0)
        {
            apply(new CoverArt(null, null));
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
            lock (Cache) Cache[beatmapsetId] = new CoverArt(null, null);
            post(() => apply(new CoverArt(null, null)));
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
            return new CoverArt(image, createBackdrop(image));
        }
        catch (Exception exception)
        {
            AppLog.Error("曲绘解码失败", exception);
            return new CoverArt(null, null);
        }
    }

    /// <summary>把缩略图缩到 96px 宽当背影用；放大到卡片宽度时自然是模糊的。</summary>
    private static ImageSource? createBackdrop(BitmapSource source)
    {
        try
        {
            if (source.PixelWidth <= BackdropWidth)
            {
                return source;
            }

            double scale = (double)BackdropWidth / source.PixelWidth;
            TransformedBitmap backdrop = new(source, new ScaleTransform(scale, scale));
            backdrop.Freeze();
            return backdrop;
        }
        catch (Exception exception)
        {
            AppLog.Error("曲绘背影生成失败", exception);
            return null;
        }
    }

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
