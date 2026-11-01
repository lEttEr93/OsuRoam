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
public static class CoverLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<int, ImageSource?> Cache = [];
    private static readonly HashSet<int> Loading = [];

    /// <summary>下载 + 解码超过这个耗时就记一条日志，便于确认卡顿来源。</summary>
    private const long SlowLoadMilliseconds = 60;

    public static async void Load(int beatmapsetId, Action<ImageSource?> apply)
    {
        if (beatmapsetId <= 0)
        {
            apply(null);
            return;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(beatmapsetId, out ImageSource? cached))
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

            ImageSource? image = decode(bytes);

            long elapsed = clock.ElapsedMilliseconds;
            if (elapsed > SlowLoadMilliseconds)
                AppLog.Info($"曲绘加载较慢：set={beatmapsetId}，{elapsed}ms（解码已在后台线程）");

            lock (Cache) Cache[beatmapsetId] = image;
            post(() => apply(image));
        }
        catch (Exception exception)
        {
            AppLog.Error($"加载曲绘失败：set={beatmapsetId}", exception);
            lock (Cache) Cache[beatmapsetId] = null;
            post(() => apply(null));
        }
        finally
        {
            lock (Cache) Loading.Remove(beatmapsetId);
        }
    }

    private static ImageSource? decode(byte[] bytes)
    {
        try
        {
            BitmapImage image = new();
            using (MemoryStream stream = new(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }
        catch (Exception exception)
        {
            AppLog.Error("曲绘解码失败", exception);
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
