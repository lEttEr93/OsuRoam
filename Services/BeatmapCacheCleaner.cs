using System.IO;
using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 谱面缓存裁剪：只处理下载下来的 .osz 文件，配置 / 历史 / 键型库 / 日志一律不动。
/// 超出上限时按最后写入时间从旧到新删除，推荐池里正在用的谱面永远跳过。
/// </summary>
public static class BeatmapCacheCleaner
{
    public sealed record Result(int DeletedFiles, long FreedBytes, long RemainingBytes);

    public static Result Clean(string directory, long limitBytes, ISet<int> keepBeatmapsetIds)
    {
        if (!Directory.Exists(directory) || limitBytes <= 0)
            return new Result(0, 0, 0);

        FileInfo[] files = new DirectoryInfo(directory)
            .GetFiles("*.osz", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToArray();
        if (files.Length == 0)
            return new Result(0, 0, 0);

        long total = files.Sum(file => file.Length);
        if (total <= limitBytes)
            return new Result(0, 0, total);

        int deleted = 0;
        long freed = 0;
        foreach (FileInfo file in files)
        {
            if (total <= limitBytes)
                break;
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file.Name), out int setId) || keepBeatmapsetIds.Contains(setId))
                continue;

            long size = file.Length;
            try
            {
                file.Delete();
                total -= size;
                freed += size;
                deleted++;
            }
            catch (Exception exception)
            {
                AppLog.Error($"删除缓存谱面失败：{file.Name}", exception);
            }
        }

        return new Result(deleted, freed, total);
    }

    /// <summary>推荐池里正在使用的谱面不能被裁掉。</summary>
    public static HashSet<int> KeepSet(IEnumerable<RecommendationItem> items, RecommendationItem? current = null)
    {
        HashSet<int> keep = items.Select(item => item.BeatmapsetId).ToHashSet();
        if (current is not null)
            keep.Add(current.BeatmapsetId);
        return keep;
    }
}
