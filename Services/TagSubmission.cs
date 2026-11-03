using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 待审 tag 的自动提交：攒够数量后往公开仓库开一个 <c>tag-pool</c> issue。
///
/// 凭据是编译期塞进 exe 的细粒度 PAT（只给该仓库 issues:write），
/// 放在 <c>data/tag-submit-token.txt</c>（.gitignore 里，不进仓库）。
/// 没有这个文件 = 功能自动关闭，程序其它部分不受影响。
///
/// 提交内容只有 tag 文本和出现次数，不带谱面 ID、歌名、时间线。
/// </summary>
public static class TagSubmission
{
    private const string RepoApi = "https://api.github.com/repos/lEttEr93/OsuRoam/issues";
    private const string ResourceName = "OsuRoam.Resources.tag-submit-token.txt";
    private const string Label = "tag-pool";

    /// <summary>攒够这么多条才提交。</summary>
    public const int MinimumPending = 30;
    /// <summary>两次提交至少隔这么多天。</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromDays(7);
    /// <summary>单次最多提交这么多条。</summary>
    public const int MaxPerSubmit = 200;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static string? token;

    public static bool HasToken
    {
        get
        {
            token ??= readToken();
            return !string.IsNullOrEmpty(token);
        }
    }

    /// <summary>该不该提交：有凭据、开关开着、攒够了、距离上次够久。</summary>
    public static bool ShouldSubmit(AppSettings settings, int pending, DateTimeOffset now) =>
        ShouldSubmit(settings, pending, now, HasToken);

    /// <summary>同上，但把「有没有凭据」显式传进来，方便测试策略本身。</summary>
    public static bool ShouldSubmit(AppSettings settings, int pending, DateTimeOffset now, bool hasToken)
    {
        if (!hasToken || !settings.TagSubmitEnabled)
            return false;
        if (pending < MinimumPending)
            return false;
        if (settings.LastTagSubmitUtc is DateTimeOffset last && now - last < Cooldown)
            return false;
        return true;
    }

    /// <summary>提交一次；成功返回 true，并把已提交的条目从队列里删掉。</summary>
    public static async Task<bool> SubmitAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!HasToken)
            return false;

        IReadOnlyList<TagIntake.Entry> queued = TagIntake.Snapshot();
        if (queued.Count == 0)
            return false;

        List<TagIntake.Entry> batch = queued.Take(MaxPerSubmit).ToList();
        string body = buildBody(batch);
        try
        {
            HttpRequestMessage request = new(HttpMethod.Post, RepoApi);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("osu-roam");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    title = $"[tag-pool] 待审 tag 提交（{batch.Count} 条，{DateTime.UtcNow:yyyy-MM-dd}）",
                    body,
                    labels = new[] { Label }
                }),
                Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // 标签不存在时 GitHub 返回 422：去掉标签重试一次。
                return await retryWithoutLabel(body, batch, settings, cancellationToken);
            }
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Info($"tag 提交失败：{(int)response.StatusCode} {response.ReasonPhrase}");
                return false;
            }

            AppLog.Info($"tag 提交成功：{batch.Count} 条");
            return afterSuccess(settings, batch);
        }
        catch (Exception exception)
        {
            AppLog.Info($"tag 提交异常：{exception.GetType().Name} {exception.Message}");
            return false;
        }
    }

    private static async Task<bool> retryWithoutLabel(string body, List<TagIntake.Entry> batch, AppSettings settings,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, RepoApi);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("osu-roam");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                title = $"[tag-pool] 待审 tag 提交（{batch.Count} 条，{DateTime.UtcNow:yyyy-MM-dd}）",
                body
            }),
            Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            AppLog.Info($"tag 提交失败（无标签重试）：{(int)response.StatusCode}");
            return false;
        }

        AppLog.Info($"tag 提交成功（无标签）：{batch.Count} 条");
        return afterSuccess(settings, batch);
    }

    private static bool afterSuccess(AppSettings settings, List<TagIntake.Entry> batch)
    {
        settings.LastTagSubmitUtc = DateTimeOffset.UtcNow;
        TagIntake.RemoveRange(batch.Select(entry => entry.Name));
        return true;
    }

    internal static string BuildBody(IReadOnlyList<TagIntake.Entry> batch) => buildBody(batch.ToList());

    private static string buildBody(List<TagIntake.Entry> batch)
    {
        string payload = JsonSerializer.Serialize(new
        {
            kind = "osu-roam/tag-review",
            version = 1,
            tags = batch.Select(entry => new { tag = entry.Name, count = entry.Count })
        }, new JsonSerializerOptions { WriteIndented = false });

        return $"""
                由 osu! Roam 客户端自动提交。这些是谱面里出现过、通过粗筛、但不在白名单里的 tag。

                审核方式见 `Tools/review-pending-tags.py`（去重 → 聚簇 → AI 归类 → 人工确认）。

                ```json
                {payload}
                ```
                """;
    }

    private static string? readToken()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
                return null;
            using StreamReader reader = new(stream, Encoding.UTF8);
            string value = reader.ReadToEnd().Trim();
            return value.Length == 0 ? null : value;
        }
        catch (Exception exception)
        {
            AppLog.Info($"读 tag 提交凭据失败：{exception.Message}");
            return null;
        }
    }
}
