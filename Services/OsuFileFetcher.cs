using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace OsuRoam.Services;

/// <summary>
/// 抓 <c>https://osu.ppy.sh/osu/{beatmapId}</c>（单难度纯文本，实测 40–90 KB、约 2 秒）。
/// 匿名端点；并发压在 4 路以内，遇 429 冷却一段时间，同一个 id 失败过就不再试。
/// 取消不算失败，不会被记进失败集合。
/// </summary>
public sealed class OsuFileFetcher
{
    private const int MaxConcurrency = 4;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RateLimitCooldown = TimeSpan.FromSeconds(60);
    private static readonly HttpClient Http = createClient();

    private readonly SemaphoreSlim gate = new(MaxConcurrency, MaxConcurrency);
    private readonly ConcurrentDictionary<int, byte> failed = new();
    private readonly object cooldownGate = new();
    private DateTimeOffset rateLimitedUntil = DateTimeOffset.MinValue;
    private int fetched;

    public int Fetched => Volatile.Read(ref fetched);

    public int FailedCount => failed.Count;

    /// <summary>抓不到就返回 null；只有真正的网络/内容错误会记入失败集合。</summary>
    public async Task<string?> TryFetchAsync(int beatmapId, CancellationToken cancellationToken = default)
    {
        if (beatmapId <= 0 || failed.ContainsKey(beatmapId))
            return null;
        lock (cooldownGate)
        {
            if (DateTimeOffset.UtcNow < rateLimitedUntil)
                return null;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using HttpRequestMessage request = new(HttpMethod.Get, $"https://osu.ppy.sh/osu/{beatmapId}");
            using HttpResponseMessage response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lock (cooldownGate)
                    rateLimitedUntil = DateTimeOffset.UtcNow + RateLimitCooldown;
                failed.TryAdd(beatmapId, 0);
                AppLog.Info($"预取被限流（429），冷却 {RateLimitCooldown.TotalSeconds:0} 秒");
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                failed.TryAdd(beatmapId, 0);
                return null;
            }

            string text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!text.Contains("osu file format", StringComparison.Ordinal))
            {
                failed.TryAdd(beatmapId, 0);
                return null;
            }

            Interlocked.Increment(ref fetched);
            return text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户取消：不记失败，交给上层停止调度。
            throw;
        }
        catch (Exception exception)
        {
            failed.TryAdd(beatmapId, 0);
            AppLog.Info($"预取 .osu 失败 {beatmapId}：{exception.GetType().Name}");
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private static HttpClient createClient()
    {
        HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
        string version = typeof(OsuFileFetcher).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"osu-roam/{version}");
        return client;
    }
}
