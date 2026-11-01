namespace OsuRoam.Models;

/// <summary>
/// 一次显示刷新的「相位」。移植自 Mania Map Analyser 的 changeKind：
/// 换歌要重放整块入场，换难度只动内部文字，同一张谱面重算时什么都不播。
/// </summary>
public enum ChangeKind
{
    /// <summary>还没有上一张谱面（首次载入），按换歌处理。</summary>
    Initial,

    /// <summary>换了一首歌（不同 beatmapset）。</summary>
    Song,

    /// <summary>同一首歌内换了难度（同 set 不同 beatmap）。</summary>
    Difficulty,

    /// <summary>同一张谱面重新计算（改 mod / 设置 / 刷新），只有数值可能变。</summary>
    Mod,

    /// <summary>选区重算，属于图表内部状态，不改卡片相位。</summary>
    Section
}

/// <summary>谱面身份。SongKey 用来区分「同一首歌」和「换歌」。</summary>
public readonly record struct MapIdentity(int BeatmapId, int BeatmapsetId, string SongKey)
{
    public static MapIdentity Empty => new(0, 0, string.Empty);

    public bool IsEmpty => BeatmapId <= 0 && BeatmapsetId <= 0;

    /// <summary>
    /// 从 beatmap 反推身份。SongKey 优先用 beatmapset id（最可靠），
    /// 退而用「作者 - 标题」文本，两者都没有时留空——留空不等于换歌。
    /// </summary>
    public static MapIdentity From(int beatmapId, int beatmapsetId, string artist, string title)
    {
        string songKey = beatmapsetId > 0
            ? $"set:{beatmapsetId}"
            : !string.IsNullOrWhiteSpace(title)
                ? $"meta:{artist} - {title}"
                : string.Empty;
        return new MapIdentity(beatmapId, beatmapsetId, songKey);
    }
}

public static class ChangeKindDetector
{
    /// <summary>
    /// 判定顺序与 MMA 一致：身份完全相同 → Mod；
    /// SongKey 变了（且新 Key 非空）→ Song；其余 → Difficulty。
    /// </summary>
    public static ChangeKind Detect(MapIdentity previous, MapIdentity next)
    {
        if (next.IsEmpty)
            return ChangeKind.Initial;

        if (previous.IsEmpty)
            return ChangeKind.Initial;

        if (previous.BeatmapId == next.BeatmapId && previous.BeatmapsetId == next.BeatmapsetId)
            return ChangeKind.Mod;

        bool songKeyChanged = !string.IsNullOrEmpty(next.SongKey) &&
            (string.IsNullOrEmpty(previous.SongKey) ||
             !string.Equals(previous.SongKey, next.SongKey, StringComparison.Ordinal));

        return songKeyChanged ? ChangeKind.Song : ChangeKind.Difficulty;
    }

    /// <summary>首次载入按换歌的入场处理，其余原样返回。</summary>
    public static ChangeKind ForEntrance(MapIdentity previous, MapIdentity next) =>
        Detect(previous, next) switch
        {
            ChangeKind.Initial => ChangeKind.Song,
            var kind => kind
        };
}
