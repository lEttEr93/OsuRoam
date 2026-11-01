using System.IO;
using System.IO.Compression;

namespace OsuRoam.Services;

/// <summary>
/// 从 <c>.osz</c> 里取出指定谱面的 <c>.osu</c> 文本，或按 <c>.osu</c> 内容自己解析。
///
/// 之所以单独抽出来：键型分析和谱面详情（CS/OD/AR/物件数…）都要读同一个文件，
/// 以前只有键型分析读；现在让两边共用一次读取，也方便单元测试只喂文本。
/// </summary>
public static class OszReader
{
    /// <summary>
    /// 找到并读出匹配 <paramref name="beatmapId"/> 的 <c>.osu</c> 文本。
    /// <paramref name="beatmapId"/> 为 0 时取第一个（兼容只想知道「随便哪张」的调用）。
    /// </summary>
    public static bool TryReadBeatmapText(string oszPath, int beatmapId, out string text, out string entryName)
    {
        text = string.Empty;
        entryName = string.Empty;
        if (!File.Exists(oszPath))
            return false;

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(oszPath);
            List<ZipArchiveEntry> candidates = archive.Entries
                .Where(item => item.FullName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
            {
                AppLog.Error($"谱面压缩包里没有 .osu：{Path.GetFileName(oszPath)}");
                return false;
            }

            // .osu 文件名通常是难度名（[Peko]、[1.1x]），不含 beatmap id；
            // 可靠做法是读文件里的 BeatmapID 元数据字段。
            foreach (ZipArchiveEntry candidate in candidates)
            {
                using StreamReader reader = new(candidate.Open());
                string content = reader.ReadToEnd();
                if (beatmapId <= 0 || HasBeatmapId(content, beatmapId))
                {
                    text = content;
                    entryName = candidate.FullName;
                    return true;
                }
            }

            if (candidates.Count == 1)
            {
                using StreamReader reader = new(candidates[0].Open());
                text = reader.ReadToEnd();
                entryName = candidates[0].FullName;
                return true;
            }

            AppLog.Error($"包里没有 BeatmapID={beatmapId} 的 .osu（共 {candidates.Count} 个）：{Path.GetFileName(oszPath)}");
            return false;
        }
        catch (Exception exception)
        {
            AppLog.Error($"读取谱面文件失败：{Path.GetFileName(oszPath)}", exception);
            return false;
        }
    }

    /// <summary><c>.osu</c> 的 <c>BeatmapID</c> 字段是否等于给定 id。</summary>
    public static bool HasBeatmapId(string osuText, int beatmapId)
    {
        foreach (string line in osuText.Split('\n'))
        {
            if (!line.StartsWith("BeatmapID:", StringComparison.Ordinal))
                continue;
            return int.TryParse(line.AsSpan(10).Trim(), out int value) && value == beatmapId;
        }
        return false;
    }
}
