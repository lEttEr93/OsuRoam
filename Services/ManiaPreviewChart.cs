using System.Globalization;

namespace OsuRoam.Services;

/// <summary>预览里的一个物件：列号 + 起止时间（毫秒，和 <c>.osu</c> 的时间轴同一把尺）。</summary>
public readonly record struct PreviewNote(int Column, double Start, double End)
{
    public double Length => End - Start;

    public bool IsHold => End > Start;
}

/// <summary>
/// 谱面预览用的极简 mania 物件表：只要「第几列、什么时候、多长」，其余（音效、连击色、SV）
/// 预览都不画，解析成本因此可以忽略（4000 个物件约 2ms）。
///
/// 列号按 osu!mania 自己的规则换算：<c>column = floor(x * keys / 512)</c>，
/// 长条取 <c>HitObjects</c> 第 6 段的 <c>endTime</c>（形如 <c>24000:0:0:0:0:</c>）。
/// </summary>
public sealed class ManiaPreviewChart
{
    public const int MaximumKeys = 10;

    private readonly PreviewNote[] notes;

    private ManiaPreviewChart(int keyCount, PreviewNote[] notes, double first, double last, double longestHold)
    {
        KeyCount = keyCount;
        this.notes = notes;
        FirstNoteTime = first;
        LastNoteTime = last;
        LongestHold = longestHold;
    }

    public int KeyCount { get; }

    public IReadOnlyList<PreviewNote> Notes => notes;

    /// <summary>第一个物件的时间（毫秒）——底部的进度条按它到最后一个物件的跨度算位置。</summary>
    public double FirstNoteTime { get; }

    /// <summary>最后一个物件结束的时间（毫秒）。</summary>
    public double LastNoteTime { get; }

    /// <summary>最长的长条有多长：二分查找要往前多找一个，否则前一段的长条会被漏掉。</summary>
    public double LongestHold { get; }

    /// <summary>
    /// 解析 <c>.osu</c> 文本。不是 mania、没有物件、键数离谱都返回 <c>null</c>——
    /// 预览只对 mania 有意义，别的模式宁可什么都不显示。
    /// </summary>
    public static ManiaPreviewChart? Parse(string? osuText)
    {
        if (string.IsNullOrWhiteSpace(osuText))
            return null;

        int mode = 0;
        double circleSize = 0;
        List<(int X, int Start, int End)> raw = [];
        string section = string.Empty;

        foreach (string rawLine in osuText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1];
                continue;
            }

            switch (section)
            {
                case "General":
                    if (mode == 0 && line.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line.AsSpan(5).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out mode);
                    break;
                case "Difficulty":
                    if (circleSize <= 0 && line.StartsWith("CircleSize:", StringComparison.OrdinalIgnoreCase))
                        double.TryParse(line.AsSpan(11).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out circleSize);
                    break;
                case "HitObjects":
                    if (tryParseRawNote(line, out (int X, int Start, int End) note))
                        raw.Add(note);
                    break;
            }
        }

        if (mode != 3 || raw.Count == 0)
            return null;

        int keyCount = (int)Math.Round(circleSize, MidpointRounding.AwayFromZero);
        if (keyCount is < 1 or > MaximumKeys)
            return null;

        PreviewNote[] notes =
        [
            .. raw
                .Select(note => new PreviewNote(ColumnOf(note.X, keyCount), note.Start, note.End))
                .OrderBy(note => note.Start)
        ];

        double first = notes[0].Start;
        double last = first;
        double longest = 0;
        foreach (PreviewNote note in notes)
        {
            if (note.End > last)
                last = note.End;
            if (note.Length > longest)
                longest = note.Length;
        }

        return new ManiaPreviewChart(keyCount, notes, first, last, longest);
    }

    /// <summary>把列号从「按 512 宽度的 x 坐标」换算成列：<c>floor(x * keys / 512)</c>。</summary>
    public static int ColumnOf(int x, int keyCount) =>
        Math.Clamp((int)(x * (long)keyCount / 512), 0, keyCount - 1);

    /// <summary>
    /// 第一个「起点不早于 <paramref name="time"/>」的物件下标（二分查找）。
    /// 渲染时用 <c>time - LongestHold</c> 去查，保证还在屏幕上的长条不会被跳过。
    /// </summary>
    public int FirstIndexAt(double time)
    {
        int low = 0;
        int high = notes.Length - 1;
        int result = notes.Length;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (notes[middle].Start >= time)
            {
                result = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }
        return result;
    }

    /// <summary>解析一行 <c>HitObjects</c>：<c>x,y,time,type,hitSound[,endTime:…]</c>。</summary>
    private static bool tryParseRawNote(string line, out (int X, int Start, int End) note)
    {
        note = default;
        string[] parts = line.Split(',');
        if (parts.Length < 5)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
        {
            return false;
        }

        // mania 只会有圈（1）和长条（128）；滑条/转盘是转换谱面的残留，跳过。
        bool hold = (type & 128) != 0;
        if (!hold && (type & 1) == 0)
            return false;

        int end = start;
        if (hold)
        {
            if (parts.Length < 6)
                return false;

            string tail = parts[5];
            int colon = tail.IndexOf(':');
            if (colon >= 0)
                tail = tail[..colon];
            if (!int.TryParse(tail.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out end))
                return false;
            if (end < start)
                end = start;
        }

        note = (x, start, end);
        return true;
    }
}
