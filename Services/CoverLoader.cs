using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OsuRoam.Services;

/// <summary>
/// 封面加载。不用 XAML 的 Image.Source="http://..."（那条通道不走程序的代理配置，
/// 失败时静默空白），改为用 HttpClient 拉字节再解码，和 API 走同一套网络路径。
/// </summary>
public static class CoverLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<int, ImageSource?> Cache = [];
    private static readonly HashSet<int> Loading = [];

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
            byte[] bytes = await Http.GetByteArrayAsync($"https://assets.ppy.sh/beatmaps/{beatmapsetId}/covers/cover.jpg");
            BitmapImage image = new();
            using (MemoryStream stream = new(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            lock (Cache) Cache[beatmapsetId] = image;
            apply(image);
        }
        catch (Exception exception)
        {
            AppLog.Error($"加载曲绘失败：set={beatmapsetId}", exception);
            lock (Cache) Cache[beatmapsetId] = null;
            apply(null);
        }
        finally
        {
            lock (Cache) Loading.Remove(beatmapsetId);
        }
    }
}
