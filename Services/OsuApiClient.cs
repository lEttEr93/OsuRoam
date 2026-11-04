using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using OsuRoam.Models;

namespace OsuRoam.Services;

public sealed class OsuApiClient : IDisposable
{
    private const int ScorePageSize = 100;
    private const int ManiaScorePageCount = 10;
    private const int OtherModeScorePageCount = 3;
    private readonly HttpClient http = new() { BaseAddress = new Uri("https://osu.ppy.sh/") };
    private string accessToken = string.Empty;
    private DateTimeOffset tokenExpiresAt;
    private string clientId = string.Empty;
    private string clientSecret = string.Empty;

    public void Configure(string id, string secret)
    {
        clientId = id.Trim();
        clientSecret = secret.Trim();
        accessToken = string.Empty;
    }

    public async Task<PlayerProfile> BuildProfileAsync(string username, string mode, int? maniaKeys, CancellationToken cancellationToken)
    {
        await authorizeAsync(cancellationToken);
        string encodedUser = Uri.EscapeDataString(username.Trim());
        OsuUser user = await getAsync<OsuUser>($"api/v2/users/{encodedUser}/{mode}?key=username", cancellationToken);
        int pageCount = string.Equals(mode, "mania", StringComparison.OrdinalIgnoreCase)
            ? ManiaScorePageCount
            : OtherModeScorePageCount;
        List<OsuScore> recent = await getScoresAsync(
            user.Id, "recent", mode, pageCount, includeFails: true, cancellationToken);
        List<OsuScore> best = await getScoresAsync(
            user.Id, "best", mode, pageCount, includeFails: false, cancellationToken);

        List<OsuScore> relevantRecent = recent.Where(score => matchesCategory(score.Beatmap, mode, maniaKeys)).ToList();
        List<OsuScore> relevantBest = best.Where(score => matchesCategory(score.Beatmap, mode, maniaKeys)).ToList();
        List<OsuScore> useful = relevantBest.Concat(relevantRecent.Where(score => score.Passed))
            .Where(score => score.Beatmap is not null).ToList();
        double[] stars = useful.Select(score => score.Beatmap!.DifficultyRating).OrderBy(value => value).ToArray();
        double center = percentile(stars, 0.62, 4.5);
        double preferredBpm = median(useful.Select(score => score.Beatmap!.Bpm), 180);
        double preferredLength = median(useful.Select(score => (double)score.Beatmap!.TotalLength), 150);
        Dictionary<string, int> mappers = useful
            .Where(score => score.Beatmapset is not null)
            .GroupBy(score => score.Beatmapset!.Creator, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        HashSet<int> playedSets = relevantRecent.Where(score => score.Beatmap is not null)
            .Select(score => score.Beatmap!.BeatmapsetId).ToHashSet();

        AppLog.Info($"读取 {mode}{(maniaKeys is int keys ? $" {keys}K" : string.Empty)} 成绩样本：recent={relevantRecent.Count}, best={relevantBest.Count}, useful={useful.Count}");

        return new PlayerProfile(user, relevantRecent, relevantBest, Math.Max(0.5, center - 0.45), center + 0.45,
            preferredBpm, preferredLength, useful.Count, mappers, playedSets);
    }

    private async Task<List<OsuScore>> getScoresAsync(
        int userId,
        string scoreType,
        string mode,
        int maxPages,
        bool includeFails,
        CancellationToken cancellationToken)
    {
        List<OsuScore> scores = [];
        for (int page = 0; page < maxPages; page++)
        {
            int offset = page * ScorePageSize;
            string path = $"api/v2/users/{userId}/scores/{scoreType}?mode={mode}&limit={ScorePageSize}&offset={offset}";
            if (includeFails)
                path += "&include_fails=1";

            List<OsuScore> batch = await getAsync<List<OsuScore>>(path, cancellationToken);
            scores.AddRange(batch);
            if (batch.Count < ScorePageSize)
                break;
        }
        return scores;
    }

    public async Task<IReadOnlyList<OsuBeatmapset>> SearchCandidatesAsync(
        AppSettings settings, double minimumStars, double maximumStars, CancellationToken cancellationToken)
    {
        List<OsuBeatmapset> results = [];
        await SearchCandidatesInBatchesAsync(settings, minimumStars, maximumStars, batch =>
        {
            results.AddRange(batch);
            return Task.CompletedTask;
        }, cancellationToken);
        return results;
    }

    /// <summary>
    /// 搜索排序轮换表。单一排序 + 固定 8 页会让候选池长期只有同一批 400 条
    /// 谱面，推荐几轮后就只能回收历史，表现为每次启动都是那几首。
    /// </summary>
    internal static readonly string[] CandidateSorts =
    [
        "ranked_desc", "rating_desc", "ranked_asc", "plays_desc", "favourites_desc", "difficulty_desc"
    ];

    /// <summary>把一次漫游的页数预算拆给两种排序，并按轮次整体滑动。</summary>
    internal static string[] BuildSortPlan(int rotation, int pageCount)
    {
        if (pageCount <= 0)
            return [];
        int sortCount = CandidateSorts.Length;
        int start = ((rotation % sortCount) + sortCount) % sortCount;
        int perSort = Math.Max(1, pageCount / 2);
        string[] plan = new string[pageCount];
        for (int page = 0; page < pageCount; page++)
            plan[page] = CandidateSorts[(start + page / perSort) % sortCount];
        return plan;
    }

    /// <summary>一次漫游的常规页数预算（50 条/页）。</summary>
    public const int SearchPageBudget = 8;

    /// <summary>池子填不满时最多翻到这么多页，别把一次漫游拖成几分钟。</summary>
    public const int ExtendedPageBudget = 24;

    public async Task SearchCandidatesInBatchesAsync(
        AppSettings settings,
        double minimumStars,
        double maximumStars,
        Func<IReadOnlyList<OsuBeatmapset>, Task> onBatch,
        CancellationToken cancellationToken,
        Func<bool>? wantsMore = null,
        int maximumPages = SearchPageBudget)
    {
        await authorizeAsync(cancellationToken);
        int resultCount = 0;
        int total = 0;
        string? cursor = null;
        int mode = settings.Mode switch { "taiko" => 1, "fruits" => 2, "mania" => 3, _ => 0 };
        // Status, genre, and language are hard filters applied after the API
        // response arrives; requesting any status avoids dropping selections.
        const string status = "any";
        string query = FormattableString.Invariant(
            $"stars>={minimumStars:0.0} stars<={maximumStars:0.0} length>={settings.MinimumLengthSeconds} length<={settings.MaximumLengthSeconds}");
        if (settings.Mode == "mania")
            query += $" keys={settings.ManiaKeys}";

        int pageBudget = Math.Max(SearchPageBudget, maximumPages);
        string[] sortPlan = BuildSortPlan(settings.SearchRotation, pageBudget);
        string? activeSort = null;

        // 勾了键型/面谱时，先用关键字搜一轮（q 会匹配 mapper tags，见 PatternSearchHints）。
        // 这一轮是为了「快速启动」：拿到大概率符合的候选，再让默认轮换取更广的池子。
        foreach (string hint in PatternSearchHints.For(settings))
        {
            string hinted = $"{query} {hint}";
            string path = $"api/v2/beatmapsets/search?m={mode}&s={status}&sort=relevance&q={Uri.EscapeDataString(hinted)}";
            try
            {
                OsuBeatmapsetSearch response = await getAsync<OsuBeatmapsetSearch>(path, cancellationToken);
                if (response.Beatmapsets.Count > 0)
                {
                    resultCount += response.Beatmapsets.Count;
                    await onBatch(response.Beatmapsets);
                    AppLog.Info($"键型关键字搜索「{hint}」：{response.Beatmapsets.Count} 个谱面集");
                }
            }
            catch (OperationCanceledException)
            {
                // 用户取消不是「这条关键字没搜到」，不能吞掉。
                throw;
            }
            catch (Exception exception)
            {
                AppLog.Info($"键型关键字搜索「{hint}」跳过：{exception.GetType().Name} {exception.Message}");
            }
        }

        for (int page = 0; page < pageBudget && resultCount < pageBudget * 50; page++)
        {
            // 常规预算用完之后，只有当调用方说「池子还没填满」才继续翻：
            // 筛选条件很窄的时候（tag 白名单 + 排除已安装），400 条候选经常一个都不剩。
            if (page >= SearchPageBudget && wantsMore?.Invoke() != true)
                break;

            string sort = sortPlan[page];
            if (!string.Equals(sort, activeSort, StringComparison.Ordinal))
            {
                // cursor_string 绑定在具体查询（含 sort）上，换排序必须重置游标。
                activeSort = sort;
                cursor = null;
            }

            string path = $"api/v2/beatmapsets/search?m={mode}&s={status}&sort={sort}&q={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrEmpty(cursor))
                path += $"&cursor_string={Uri.EscapeDataString(cursor)}";

            OsuBeatmapsetSearch response = await getAsync<OsuBeatmapsetSearch>(path, cancellationToken);
            total = Math.Max(total, response.Total);
            if (response.Beatmapsets.Count > 0)
            {
                resultCount += response.Beatmapsets.Count;
                await onBatch(response.Beatmapsets);
            }
            cursor = response.CursorString;
            if (string.IsNullOrEmpty(cursor))
                break;
        }

        if (resultCount > 0 && total > resultCount)
            AppLog.Info($"候选检索：本轮到 {resultCount} 个谱面集（该查询服务端命中约 {total} 个，页面上限 {pageBudget * 50}）");
    }

    private async Task authorizeAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(accessToken) && tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("请先填写 osu! OAuth Client ID 和 Client Secret。");
        using HttpResponseMessage response = await http.PostAsJsonAsync("oauth/token", new { client_id = clientId, client_secret = clientSecret, grant_type = "client_credentials", scope = "public" }, cancellationToken);
        await ensureSuccessAsync(response, cancellationToken);
        TokenResponse token = (await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)) ?? throw new InvalidOperationException("osu! OAuth 返回了空响应。");
        accessToken = token.AccessToken;
        tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private async Task<T> getAsync<T>(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(path, cancellationToken);
        await ensureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)) ?? throw new InvalidOperationException("osu! API 返回了空响应。");
    }

    private static async Task ensureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "osu! API 认证失败，请检查 Client ID 和 Client Secret。",
            System.Net.HttpStatusCode.NotFound => "找不到该 osu! 玩家。",
            _ => $"osu! API 请求失败 ({(int)response.StatusCode})。"
        };
        throw new HttpRequestException($"{message} {body}");
    }
    private static double median(IEnumerable<double> source, double fallback)
    {
        double[] values = source.Where(value => value > 0).OrderBy(value => value).ToArray();
        return values.Length == 0 ? fallback : percentile(values, 0.5, fallback);
    }

    private static bool matchesCategory(OsuBeatmap? beatmap, string mode, int? maniaKeys)
    {
        if (beatmap is null)
            return false;
        if (!string.Equals(beatmap.Mode, mode, StringComparison.OrdinalIgnoreCase))
            return false;
        return mode != "mania" || maniaKeys is null || (int)Math.Round(beatmap.CircleSize) == maniaKeys.Value;
    }

    private static double percentile(double[] sorted, double percentile, double fallback)
    {
        if (sorted.Length == 0)
            return fallback;
        double index = (sorted.Length - 1) * percentile;
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        return lower == upper ? sorted[lower] : sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }

    public void Dispose() => http.Dispose();

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
