using System.Globalization;

namespace OsuRoam.Models;

/// <summary>
/// 谱面静态详情：从 <c>.osz</c> 里的 <c>.osu</c> 头解析出来的东西。
///
/// 这些字段 osu! API 的 beatmapset 接口不直接给（它只给星级 / BPM / 时长这类汇总值），
/// 但导入时手上正好有 <c>.osz</c>，读一遍就能把 CS / OD / AR / HP、物件数、实际 BPM 拿到。
/// 只做展示用，不参与推荐打分。
/// </summary>
public sealed record BeatmapDetail
{
    /// <summary>0=osu! 1=taiko 2=catch 3=mania，和 <c>.osu</c> 的 Mode 字段一致。</summary>
    public int Mode { get; init; }
    public int KeyCount { get; init; }
    public double CircleSize { get; init; }
    public double OverallDifficulty { get; init; }
    public double ApproachRate { get; init; }
    public double HPDrainRate { get; init; }
    public double Bpm { get; init; }
    public int ObjectCount { get; init; }
    public int CircleCount { get; init; }
    public int SliderCount { get; init; }
    public int SpinnerCount { get; init; }
    public int HoldCount { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Creator { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;

    public bool HasObjects => ObjectCount > 0;

    public string ModeName => Mode switch
    {
        1 => "taiko",
        2 => "catch",
        3 => "mania",
        _ => "osu"
    };

    /// <summary>模式 + 键数，例如 <c>mania 4K</c>。</summary>
    public string ModeDisplay => Mode == 3 && KeyCount > 0 ? $"mania {KeyCount}K" : ModeName;
}

/// <summary>
/// 把 <c>.osu</c> 文本解析成 <see cref="BeatmapDetail"/>。只认 sections 里的键值对和 HitObjects，
/// 不追求完整实现——遇到缺字段就按 osu! 自己的默认规则补（AR 缺省等于 OD）。
/// </summary>
public static class BeatmapDetailReader
{
    public static BeatmapDetail? Parse(string? osuText)
    {
        if (string.IsNullOrWhiteSpace(osuText))
            return null;

        Dictionary<string, string> general = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> difficulty = new(StringComparer.OrdinalIgnoreCase);

        string section = string.Empty;
        double bpm = 0;
        int objectCount = 0, circleCount = 0, sliderCount = 0, spinnerCount = 0, holdCount = 0;

        foreach (string rawLine in osuText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            switch (section)
            {
                case "General":
                    put(general, line);
                    break;
                case "Metadata":
                    put(metadata, line);
                    break;
                case "Difficulty":
                    put(difficulty, line);
                    break;
                case "TimingPoints":
                    // 第一个非继承（uninherited=1）的时间点决定 BPM。
                    if (bpm <= 0)
                    {
                        string[] timing = line.Split(',');
                        if (timing.Length >= 7 &&
                            double.TryParse(timing[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength) &&
                            timing[6].Trim() == "1" && beatLength > 0)
                        {
                            bpm = 60000.0 / beatLength;
                        }
                    }
                    break;
                case "HitObjects":
                    if (line.Contains(','))
                    {
                        objectCount++;
                        if (int.TryParse(line.Split(',')[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                        {
                            if ((type & 128) != 0) holdCount++;
                            else if ((type & 8) != 0) spinnerCount++;
                            else if ((type & 2) != 0) sliderCount++;
                            else circleCount++;
                        }
                    }
                    break;
            }
        }

        int mode = parseInt(general, "Mode", 0);
        double circleSize = parseDouble(difficulty, "CircleSize", 4);
        double overallDifficulty = parseDouble(difficulty, "OverallDifficulty", 5);
        // osu! 的规矩：没写 ApproachRate 时用 OverallDifficulty。
        double approachRate = parseDouble(difficulty, "ApproachRate", overallDifficulty);
        double hpDrain = parseDouble(difficulty, "HPDrainRate", 5);

        BeatmapDetail detail = new()
        {
            Mode = mode,
            KeyCount = mode == 3 ? (int)Math.Round(circleSize) : 0,
            CircleSize = circleSize,
            OverallDifficulty = overallDifficulty,
            ApproachRate = approachRate,
            HPDrainRate = hpDrain,
            Bpm = bpm,
            ObjectCount = objectCount,
            CircleCount = circleCount,
            SliderCount = sliderCount,
            SpinnerCount = spinnerCount,
            HoldCount = holdCount,
            Title = valueOrEmpty(metadata, "Title"),
            Artist = valueOrEmpty(metadata, "Artist"),
            Creator = valueOrEmpty(metadata, "Creator"),
            Version = valueOrEmpty(metadata, "Version"),
            Source = valueOrEmpty(metadata, "Source")
        };

        return detail.ObjectCount == 0 && detail.Bpm <= 0 && detail.Title.Length == 0 ? null : detail;
    }

    private static void put(Dictionary<string, string> target, string line)
    {
        int colon = line.IndexOf(':');
        if (colon <= 0)
            return;
        target[line[..colon].Trim()] = line[(colon + 1)..].Trim();
    }

    private static string valueOrEmpty(Dictionary<string, string> source, string key) =>
        source.TryGetValue(key, out string? value) ? value : string.Empty;

    private static int parseInt(Dictionary<string, string> source, string key, int fallback) =>
        source.TryGetValue(key, out string? value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;

    private static double parseDouble(Dictionary<string, string> source, string key, double fallback) =>
        source.TryGetValue(key, out string? value) &&
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : fallback;
}
