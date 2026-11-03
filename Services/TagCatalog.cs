using System.IO;
using System.Reflection;
using System.Text.Json;

namespace OsuRoam.Services;

/// <summary>
/// 审核过的 tag 白名单（Resources/tag-pool.json）。
/// 只有白名单里的 tag 会出现在可选列表里；爬到的其它 tag 进 <see cref="AppSettings.SeenTags"/>，
/// 排在白名单之后。池子内容由 Tools/build-tag-pool.py 生成，改池子改脚本而不是改代码。
/// </summary>
public static class TagCatalog
{
    private sealed class Payload
    {
        public int Version { get; set; } = 1;
        public List<PayloadGroup> Groups { get; set; } = [];
        public Dictionary<string, string> Aliases { get; set; } = [];
    }

    private sealed class PayloadGroup
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = [];
    }

    private static readonly List<(string Id, string Label, IReadOnlyList<string> Tags)> Groups = [];
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> Ranks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> DefaultTags = [];

    static TagCatalog() => load();

    /// <summary>白名单条目，顺序即界面顺序（组内按审计时的真实出现次数排）。</summary>
    public static IReadOnlyList<string> Pool => DefaultTags;

    /// <summary>分组视图，给以后的分类展示用。</summary>
    public static IReadOnlyList<(string Id, string Label, IReadOnlyList<string> Tags)> AllGroups => Groups;

    /// <summary>能不能在可选列表里出现（白名单 or 见过）。</summary>
    public static bool Contains(string tag) => Ranks.ContainsKey(tag);

    /// <summary>别名归一：dnb → drum and bass、j-pop → jpop。未知 tag 原样返回。</summary>
    public static string Canonical(string tag)
    {
        string trimmed = (tag ?? string.Empty).Trim();
        return Aliases.TryGetValue(trimmed, out string? canonical) ? canonical : trimmed;
    }

    /// <summary>白名单里的排序权重，不在白名单里返回 int.MaxValue。</summary>
    public static int Rank(string tag) => Ranks.TryGetValue(tag, out int rank) ? rank : int.MaxValue;

    private static void load()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("OsuRoam.Resources.tag-pool.json");
            if (stream is null)
                return;

            Payload? payload = JsonSerializer.Deserialize<Payload>(
                stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null)
                return;

            int rank = 0;
            foreach (PayloadGroup group in payload.Groups)
            {
                List<string> tags = [];
                foreach (string tag in group.Tags)
                {
                    string name = tag.Trim().ToLowerInvariant();
                    if (name.Length == 0 || Ranks.ContainsKey(name))
                        continue;
                    Ranks[name] = rank++;
                    DefaultTags.Add(name);
                    tags.Add(name);
                }
                Groups.Add((group.Id, group.Label, tags));
            }

            foreach ((string alias, string canonical) in payload.Aliases)
            {
                string target = canonical.Trim().ToLowerInvariant();
                if (target.Length == 0)
                    continue;
                Aliases[alias.Trim().ToLowerInvariant()] = target;
            }
        }
        catch (Exception exception)
        {
            // 池子坏了不能让整个程序起不来：退回空池，行为等同「只能选见过的 tag」。
            AppLog.Error("加载 tag 池失败", exception);
        }
    }
}
