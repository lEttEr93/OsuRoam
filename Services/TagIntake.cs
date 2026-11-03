using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 待审 tag 队列：候选谱面里出现、通过粗筛、但不在白名单里的 tag 先存本地，
/// 攒够数量再由用户决定要不要提交到仓库的待审核区。
///
/// 只记录 tag 文本与出现次数 —— 不记谱面 ID、不记歌名、不记时间线，提交内容完全可见。
/// 上限 <see cref="Capacity"/> 条，满了先丢「见得最少、最久没再见到」的。
/// </summary>
public static class TagIntake
{
    public const int Capacity = 500;
    private static readonly object Gate = new();
    private static readonly List<Entry> Entries = [];
    private static bool loaded;

    public sealed class Entry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("first")] public DateTimeOffset FirstSeenUtc { get; set; }
        [JsonPropertyName("last")] public DateTimeOffset LastSeenUtc { get; set; }
    }

    private sealed class Payload
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("entries")] public List<Entry> Entries { get; set; } = [];
    }

    private static string Path_ => Path.Combine(StoragePaths.Root, "pending-tags.json");

    /// <summary>当前待审条目（按出现次数降序的副本）。</summary>
    public static IReadOnlyList<Entry> Snapshot()
    {
        lock (Gate)
        {
            ensureLoaded();
            return Entries.OrderByDescending(entry => entry.Count)
                .ThenByDescending(entry => entry.LastSeenUtc)
                .ToList();
        }
    }

    public static int Count
    {
        get
        {
            lock (Gate)
            {
                ensureLoaded();
                return Entries.Count;
            }
        }
    }

    /// <summary>把一批 tag 过一遍粗筛，没见过的进队列。已经在白名单/别名里的直接忽略。</summary>
    public static void Offer(IEnumerable<string>? tags)
    {
        if (tags is null)
            return;

        lock (Gate)
        {
            ensureLoaded();
            bool dirty = false;
            foreach (string raw in tags)
            {
                string name = TagCatalog.Canonical((raw ?? string.Empty).Trim().ToLowerInvariant());
                if (!AppSettings.IsMeaningfulTag(name) || TagCatalog.Contains(name))
                    continue;

                Entry? existing = Entries.FirstOrDefault(entry =>
                    string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    Entries.Add(new Entry
                    {
                        Name = name,
                        Count = 1,
                        FirstSeenUtc = DateTimeOffset.UtcNow,
                        LastSeenUtc = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    existing.Count += 1;
                    existing.LastSeenUtc = DateTimeOffset.UtcNow;
                }
                dirty = true;
            }

            if (!dirty)
                return;

            trim();
            save();
        }
    }

    /// <summary>提交成功后清空（或只清掉提交过的那批）。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            ensureLoaded();
            Entries.Clear();
            save();
        }
    }

    /// <summary>提交成功后只删掉已提交的那批，之后新见的继续留在队列里。</summary>
    public static void RemoveRange(IEnumerable<string> names)
    {
        HashSet<string> drop = new(names ?? [], StringComparer.OrdinalIgnoreCase);
        if (drop.Count == 0)
            return;

        lock (Gate)
        {
            ensureLoaded();
            int removed = Entries.RemoveAll(entry => drop.Contains(entry.Name));
            if (removed > 0)
                save();
        }
    }

    /// <summary>提交用的载荷：只带 tag 和次数。</summary>
    public static string BuildPayloadJson(int limit = 200)
    {
        lock (Gate)
        {
            ensureLoaded();
            List<object> rows = Entries
                .OrderByDescending(entry => entry.Count)
                .Take(limit)
                .Select(entry => (object)new { tag = entry.Name, count = entry.Count })
                .ToList();
            return JsonSerializer.Serialize(
                new { kind = "osu-roam/tag-review", version = 1, tags = rows },
                new JsonSerializerOptions { WriteIndented = false });
        }
    }

    private static void trim()
    {
        if (Entries.Count <= Capacity)
            return;
        Entries.Sort((left, right) =>
        {
            int byCount = right.Count.CompareTo(left.Count);
            return byCount != 0 ? byCount : right.LastSeenUtc.CompareTo(left.LastSeenUtc);
        });
        Entries.RemoveRange(Capacity, Entries.Count - Capacity);
    }

    private static void ensureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        try
        {
            if (!File.Exists(Path_))
                return;
            Payload? payload = JsonSerializer.Deserialize<Payload>(File.ReadAllBytes(Path_));
            if (payload?.Entries is null)
                return;
            Entries.Clear();
            Entries.AddRange(payload.Entries.Where(entry =>
                entry is not null && AppSettings.IsMeaningfulTag(entry.Name)));
        }
        catch (Exception exception)
        {
            AppLog.Info($"读待审 tag 队列失败：{exception.Message}");
        }
    }

    private static void save()
    {
        try
        {
            Directory.CreateDirectory(StoragePaths.Root);
            File.WriteAllBytes(Path_,
                JsonSerializer.SerializeToUtf8Bytes(new Payload { Entries = Entries },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            AppLog.Info($"写待审 tag 队列失败：{exception.Message}");
        }
    }
}
