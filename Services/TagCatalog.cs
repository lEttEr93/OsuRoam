using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace OsuRoam.Services;

/// <summary>
/// 审核过的 tag 白名单（data/tag-pool.json）。
/// 只有白名单里的 tag 会出现在可选列表里；爬到的其它 tag 进 <see cref="AppSettings.SeenTags"/>，
/// 排在白名单之后。池子内容由 Tools/build-tag-pool.py 生成，改池子改脚本而不是改代码。
///
/// 三层来源，按优先级：
/// 1. 本地缓存 <c>data/tag-pool.json</c>（上次远程更新下来的）；
/// 2. 程序内嵌的同名资源（随版本发布）；
/// 3. 远程 <see cref="RemoteUrl"/>（公开仓库，匿名可读），启动时后台拉一次。
/// 任何一层坏掉都只是回退，不影响启动。
/// </summary>
public static class TagCatalog
{
    private const string RemoteUrl = "https://raw.githubusercontent.com/lEttEr93/OsuRoam/main/data/tag-pool.json";
    private const string ResourceName = "OsuRoam.Resources.tag-pool.json";
    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(10);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private sealed class Payload
    {
        public int Version { get; set; } = 1;
        public string Audited { get; set; } = string.Empty;
        public List<PayloadGroup> Groups { get; set; } = [];
        public Dictionary<string, string> Aliases { get; set; } = [];
    }

    private sealed class PayloadGroup
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = [];
    }

    /// <summary>一份完整的池子。整体替换，避免刷新时读到半成品。</summary>
    private sealed record Snapshot(
        List<(string Id, string Label, IReadOnlyList<string> Tags)> Groups,
        List<string> Pool,
        Dictionary<string, int> Ranks,
        Dictionary<string, string> Aliases,
        byte[] Raw,
        string Source);

    private static Snapshot current = Empty("未加载");

    /// <summary>池子换了（远程更新成功），界面据此重建列表。</summary>
    public static event Action? Changed;

    static TagCatalog()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("osu-roam/tag-pool");
        loadInitial();
    }

    /// <summary>白名单条目，顺序即界面顺序（组内按审计时的真实出现次数排）。</summary>
    public static IReadOnlyList<string> Pool => current.Pool;

    /// <summary>分组视图，给以后的分类展示用。</summary>
    public static IReadOnlyList<(string Id, string Label, IReadOnlyList<string> Tags)> AllGroups => current.Groups;

    /// <summary>池子内容从哪来，只用于日志。</summary>
    public static string Source => current.Source;

    /// <summary>能不能在可选列表里出现（白名单 or 见过）。</summary>
    public static bool Contains(string tag) => current.Ranks.ContainsKey(tag);

    /// <summary>别名归一：dnb → drum and bass、j-pop → jpop。未知 tag 原样返回。</summary>
    public static string Canonical(string tag)
    {
        string trimmed = (tag ?? string.Empty).Trim();
        return current.Aliases.TryGetValue(trimmed, out string? canonical) ? canonical : trimmed;
    }

    /// <summary>白名单里的排序权重，不在白名单里返回 int.MaxValue。</summary>
    public static int Rank(string tag) => current.Ranks.TryGetValue(tag, out int rank) ? rank : int.MaxValue;

    /// <summary>
    /// 拉一次远程池子；内容变了就写进本地缓存并替换内存里的池子，返回 true。
    /// 网络失败、内容不合法、启动太慢都只是返回 false —— 调用方不需要处理异常。
    /// </summary>
    public static async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RemoteTimeout);
            byte[] bytes = await Http.GetByteArrayAsync(RemoteUrl, timeout.Token);
            if (!tryBuild(bytes, "远程", out Snapshot? snapshot) || snapshot is null)
                return false;
            if (snapshot.Raw.AsSpan().SequenceEqual(current.Raw))
                return false;

            writeCache(bytes);
            current = snapshot;
            Changed?.Invoke();
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Info($"tag 池远程更新跳过：{exception.GetType().Name} {exception.Message}");
            return false;
        }
    }

    /// <summary>解析并校验一份池子内容（JSON 文本）。给测试和远程更新共用。</summary>
    public static bool TryParse(string json)
    {
        try
        {
            return tryBuild(System.Text.Encoding.UTF8.GetBytes(json), "测试", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void loadInitial()
    {
        string cachePath = cachePathOf();
        if (File.Exists(cachePath))
        {
            try
            {
                byte[] cached = File.ReadAllBytes(cachePath);
                if (tryBuild(cached, "本地缓存", out Snapshot? snapshot) && snapshot is not null)
                {
                    current = snapshot;
                    return;
                }
            }
            catch (IOException exception)
            {
                AppLog.Info($"读 tag 池缓存失败：{exception.Message}");
            }
        }

        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
                return;
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            if (tryBuild(buffer.ToArray(), "内置", out Snapshot? embedded) && embedded is not null)
                current = embedded;
        }
        catch (Exception exception)
        {
            AppLog.Error("加载内置 tag 池失败", exception);
        }
    }

    private static bool tryBuild(byte[] bytes, string source, out Snapshot? snapshot)
    {
        snapshot = null;
        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(
                bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null)
            return false;

        List<(string, string, IReadOnlyList<string>)> groups = [];
        List<string> pool = [];
        Dictionary<string, int> ranks = new(StringComparer.OrdinalIgnoreCase);
        int rank = 0;
        foreach (PayloadGroup group in payload.Groups ?? [])
        {
            List<string> tags = [];
            foreach (string tag in group.Tags ?? [])
            {
                string name = (tag ?? string.Empty).Trim().ToLowerInvariant();
                if (name.Length == 0 || ranks.ContainsKey(name))
                    continue;
                ranks[name] = rank++;
                pool.Add(name);
                tags.Add(name);
            }
            groups.Add((group.Id, group.Label, tags));
        }

        // 空池子等于把功能关掉，宁可当解析失败、继续用旧的那份。
        if (pool.Count == 0)
            return false;

        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string alias, string canonical) in payload.Aliases ?? [])
        {
            string target = (canonical ?? string.Empty).Trim().ToLowerInvariant();
            if (target.Length == 0 || string.Equals(alias, target, StringComparison.OrdinalIgnoreCase))
                continue;
            aliases[alias.Trim().ToLowerInvariant()] = target;
        }

        snapshot = new Snapshot(groups, pool, ranks, aliases, bytes, source);
        return true;
    }

    private static string cachePathOf() => Path.Combine(StoragePaths.Root, "tag-pool.json");

    private static void writeCache(byte[] bytes)
    {
        try
        {
            string path = cachePathOf();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, true);
        }
        catch (Exception exception)
        {
            AppLog.Info($"写 tag 池缓存失败：{exception.Message}");
        }
    }

    private static Snapshot Empty(string source) =>
        new([], [], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], source);
}
