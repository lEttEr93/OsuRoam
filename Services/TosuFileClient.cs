using System.Collections.Concurrent;
using System.Net.Http;

namespace OsuRoam.Services;

/// <summary>
/// 从 tosu 取「当前谱面」的 `.osu` 文本。
///
/// tosu 的 HTTP 和 WebSocket 同端口，`GET /files/beatmap/file` 直接返回当前谱面的
/// `.osu`（lazer 下就是它内容仓库里那份），所以不用下载 `.osz` 也能算键型和属性。
/// </summary>
public sealed class TosuFileClient : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ConcurrentDictionary<int, string?> cache = new();
    private string baseUrl = string.Empty;

    public TosuFileClient()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("osu-roam");
    }

    /// <summary>把 WebSocket 地址（ws://127.0.0.1:24050/websocket/v2）换算成 HTTP 根地址。</summary>
    public void Configure(string websocketUrl)
    {
        baseUrl = string.Empty;
        if (!Uri.TryCreate(websocketUrl, UriKind.Absolute, out Uri? uri))
            return;

        string scheme = uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        baseUrl = $"{scheme}://{uri.Host}:{uri.Port}";
    }

    /// <summary>
    /// 取某个难度（BeatmapID）的 `.osu` 文本。同一个 id 只请求一次——
    /// 选歌界面来回到同一张谱很常见。
    /// </summary>
    public async Task<string?> TryGetBeatmapFileAsync(int beatmapId, CancellationToken cancellationToken)
    {
        if (beatmapId <= 0 || baseUrl.Length == 0)
            return null;
        if (cache.TryGetValue(beatmapId, out string? cached))
            return cached;

        try
        {
            string text = await http.GetStringAsync($"{baseUrl}/files/beatmap/file", cancellationToken);
            // tosu 返回的是「当前」谱面，拿到的可能是上一个；校验一下 id，别把 A 的键型算到 B 头上。
            if (string.IsNullOrWhiteSpace(text) || !text.Contains("osu file format", StringComparison.Ordinal))
                return null;

            if (!int.TryParse(readField(text, "BeatmapID"), out int fileBeatmapId) || fileBeatmapId != beatmapId)
                return null;

            cache[beatmapId] = text;
            return text;
        }
        catch (Exception exception)
        {
            AppLog.Info($"读取 tosu 当前谱面失败：{exception.Message}");
            return null;
        }
    }

    /// <summary>清空缓存（换连接目标时用）。</summary>
    public void Reset()
    {
        cache.Clear();
        baseUrl = string.Empty;
    }

    private static string readField(string text, string name)
    {
        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                return line[(name.Length + 1)..].Trim();
        }
        return string.Empty;
    }

    public void Dispose() => http.Dispose();
}
