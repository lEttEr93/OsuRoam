using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OsuRoam.Services;

/// <summary>
/// 本地已安装谱面：直接扫 osu! 的谱面目录，拿到「哪几个难度已经在你机器上」。
///
/// 两种客户端布局都得认，而且**可能同时存在**（很多人 stable / lazer 都装着）：
/// - osu!stable：`%LOCALAPPDATA%\osu!\Songs\&lt;setId&gt; artist - title\*.osu`，文件带扩展名；
/// - osu!lazer：`%APPDATA%\osu\files\&lt;hex&gt;\&lt;hex&gt;\&lt;sha256&gt;` 是内容寻址仓库，
///   名字里没有 ID，只能读文件头判断是不是 `.osu`，再抠 `[Metadata]`。
///
/// lazer 那个仓库两万多个文件，实测并发扫 2–3 秒（沙箱里串行 16ms/文件，并发后开销被摊掉）。
/// 结果按「每个根目录：文件数 + 取样文件名」缓存，没装新谱面就不用重扫。
/// </summary>
public static class LocalBeatmapLibrary
{
    private static readonly Regex BeatmapIdPattern = new(@"^\s*BeatmapID\s*:\s*(-?\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex BeatmapSetIdPattern = new(@"^\s*BeatmapSetID\s*:\s*(-?\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private const int HeaderBytes = 8 * 1024;

    private static HashSet<int> installedBeatmapIds = [];
    private static HashSet<int> installedBeatmapsetIds = [];
    private static string? note;

    /// <summary>已安装的难度（BeatmapID）——去重就是按它来的。</summary>
    public static IReadOnlySet<int> InstalledBeatmapIds => installedBeatmapIds;

    /// <summary>已安装的谱面集，只用于日志和统计。</summary>
    public static IReadOnlySet<int> InstalledBeatmapsetIds => installedBeatmapsetIds;

    public static string? Note => note;

    public static bool HasData => installedBeatmapIds.Count > 0;

    /// <summary>所有存在的谱面目录（stable 的 Songs、lazer 的 files）。</summary>
    public static IReadOnlyList<string> ResolveRoots()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string[] candidates =
        [
            Path.Combine(localAppData, "osu!", "Songs"),
            Path.Combine(roaming, "osu", "files")
        ];

        return candidates.Where(Directory.Exists).ToList();
    }

    /// <summary>扫所有目录并取并集；失败不抛，保持上一次的结果。</summary>
    public static async Task RefreshAsync(string? cacheDirectory = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> roots = ResolveRoots();
        if (roots.Count == 0)
        {
            note = "没找到 osu! 谱面目录（Songs / files 都不存在）";
            return;
        }

        CacheFile cache = cacheDirectory is null ? new CacheFile([]) : loadCache(cacheDirectory);
        List<CacheEntry> entries = [];
        List<string> parts = [];
        int scannedFiles = 0;

        foreach (string root in roots)
        {
            bool lazerStore = Path.GetFileName(root).Equals("files", StringComparison.OrdinalIgnoreCase);
            List<string> files = await Task.Run(() => enumerate(root, lazerStore, cancellationToken), cancellationToken);
            string signature = signatureOf(files);

            CacheEntry? cached = cache.Entries.FirstOrDefault(entry =>
                string.Equals(entry.Root, root, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Signature, signature, StringComparison.Ordinal));

            if (cached is not null)
            {
                entries.Add(cached);
                parts.Add($"{Path.GetFileName(root)} 缓存 {cached.BeatmapIds.Length}");
                continue;
            }

            (int[] beatmaps, int[] sets) = await Task.Run(() => scan(files, cancellationToken), cancellationToken);
            entries.Add(new CacheEntry(root, signature, beatmaps, sets));
            scannedFiles += files.Count;
            parts.Add($"{Path.GetFileName(root)} 扫 {files.Count} 个文件 → {beatmaps.Length}");
        }

        installedBeatmapIds = [.. entries.SelectMany(entry => entry.BeatmapIds).Distinct()];
        installedBeatmapsetIds = [.. entries.SelectMany(entry => entry.BeatmapsetIds).Distinct()];
        note = $"已安装谱面：{installedBeatmapIds.Count} 个难度 / {installedBeatmapsetIds.Count} 个谱面集" +
               $"（{string.Join("，", parts)}；本次实际读取 {scannedFiles} 个文件）";

        if (cacheDirectory is not null)
            saveCache(cacheDirectory, new CacheFile([.. entries]));
    }

    /// <summary>
    /// 枚举候选文件。lazer 的仓库里混着音频和图片，先按大小粗筛，
    /// 真正的判断留给读文件头。
    /// </summary>
    private static List<string> enumerate(string folder, bool lazerStore, CancellationToken cancellationToken)
    {
        List<string> result = [];
        try
        {
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            };

            foreach (string path in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (lazerStore)
                {
                    // lazer 的仓库是 SHA256 命名、没有扩展名，只能靠文件头认。
                    // 这里不按大小预筛：FileInfo.Length 是串行的 stat，19k 次要 10 秒以上，
                    // 而读 24 字节判断和 stat 差不多便宜，交给后面的并发扫描。
                }
                else if (!path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(path);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("枚举谱面目录失败", exception);
        }

        return result;
    }

    private static (int[] Beatmaps, int[] Sets) scan(List<string> files, CancellationToken cancellationToken)
    {
        ConcurrentDictionary<int, byte> beatmaps = new();
        ConcurrentDictionary<int, byte> sets = new();

        Parallel.ForEach(files,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken },
            path =>
            {
                if (!tryReadIds(path, out int beatmapId, out int setId))
                    return;
                beatmaps.TryAdd(beatmapId, 0);
                if (setId > 0)
                    sets.TryAdd(setId, 0);
            });

        return ([.. beatmaps.Keys], [.. sets.Keys]);
    }

    /// <summary>只读文件头：lazer 仓库里靠 `osu file format` 认谱面，再抠 [Metadata] 的两个 ID。</summary>
    internal static bool tryReadIds(string path, out int beatmapId, out int setId)
    {
        beatmapId = 0;
        setId = 0;
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 24, FileOptions.SequentialScan);

            // 先看 24 字节：lazer 仓库里九成是音频和图片，确认是 .osu 才值得再读 8KB。
            byte[] probe = new byte[24];
            int probed = stream.Read(probe, 0, probe.Length);
            if (probed <= 0 || !Encoding.UTF8.GetString(probe, 0, probed).Contains("osu file format", StringComparison.Ordinal))
                return false;

            stream.Position = 0;
            byte[] buffer = new byte[HeaderBytes];
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                return false;

            return tryParseIds(Encoding.UTF8.GetString(buffer, 0, read), out beatmapId, out setId);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>抠 `[Metadata]` 里的 BeatmapID / BeatmapSetID；解析单独拆出来便于测试。</summary>
    internal static bool tryParseIds(string text, out int beatmapId, out int setId)
    {
        beatmapId = 0;
        setId = 0;

        Match beatmap = BeatmapIdPattern.Match(text);
        if (!beatmap.Success || !int.TryParse(beatmap.Groups[1].Value, out beatmapId) || beatmapId <= 0)
        {
            beatmapId = 0;
            return false;
        }

        Match set = BeatmapSetIdPattern.Match(text);
        if (set.Success && int.TryParse(set.Groups[1].Value, out setId) && setId < 0)
            setId = 0;
        return true;
    }

    /// <summary>
    /// 缓存指纹：文件数 + 头尾几个文件名。比 mtime 便宜，也够判断「有没有装新谱面」。
    /// </summary>
    private static string signatureOf(List<string> files)
    {
        if (files.Count == 0)
            return "0";

        string first = files[0];
        string last = files[^1];
        return $"{files.Count}|{Path.GetFileName(first)}|{Path.GetFileName(last)}";
    }

    private static string cachePath(string directory) => Path.Combine(directory, "installed-beatmaps.json");

    private static CacheFile loadCache(string directory)
    {
        try
        {
            string path = cachePath(directory);
            if (!File.Exists(path))
                return new CacheFile([]);
            return JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path)) ?? new CacheFile([]);
        }
        catch
        {
            return new CacheFile([]);
        }
    }

    private static void saveCache(string directory, CacheFile cache)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(cachePath(directory), JsonSerializer.Serialize(cache));
        }
        catch (Exception exception)
        {
            AppLog.Error("写入已安装谱面缓存失败", exception);
        }
    }

    private sealed record CacheEntry(string Root, string Signature, int[] BeatmapIds, int[] BeatmapsetIds);

    private sealed record CacheFile(CacheEntry[] Entries)
    {
        public CacheFile() : this([]) { }
    }
}
