using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// mania 键型分类。分类体系与判定规则移植自 osumania_map_analyser（MIT）的
/// RC 键型分析器；其核心 matcher 源自 Interlude（YAVSRG），LN 检测为 MMA 自研。
/// 这里只做「谱面 → 键型占比 + 主导分类」，不参与难度/星级估算。
/// </summary>
public static class ManiaPatternAnalyzer
{
    /// <summary>
    /// 分析口径的版本号。改了核心型判定 / 份额算法就 +1 —— 旧缓存会被当成过期，
    /// 预取时会重新解析（见 PatternLibrary.TryGetVerified）。
    /// </summary>
    public const int Version = 2;

    // ---- 与 MMA patterns/config.js 对齐的阈值 ----
    private const double PatternStabilityThreshold = 5.0;
    private const double LnModeLowThreshold = 0.15;
    private const double LnModeHighThreshold = 0.9;
    private const double HbRowRatioThreshold = 0.1;
    private const double CategoryJsHsSecondaryRatio = 0.4;
    private const double JackyMinBpm = 90.0;
    private const double ShieldMaxBeatRatio = 0.25;
    private const double InverseGapToleranceMs = 5.0;
    private const int InverseMinFilledLanes = 3;
    private const int ReleaseScanRows = 4;
    private const int ReleaseMinTailRows = 4;
    private const int ReleaseRollPoints = 2;
    private const int ReleaseFullMatchRows = 5;
    private const int JackyContextWindow = 6;
    private const double JackyFallbackMaxMspb = 185.0;
    private const int MatcherWindow = 8;
    private const double SpecificShareFloor = 0.05;

    public static ManiaPattern? Analyze(string osuText, int beatmapId = 0, int beatmapsetId = 0)
    {
        Chart? chart = parseChart(osuText);
        if (chart is null || chart.Keys <= 0 || chart.Rows.Count == 0)
            return null;

        List<PatternRow> rows = buildPrimitives(chart);
        if (rows.Count == 0)
            return null;

        bool is4K = chart.Keys == 4;
        List<PatternMatch> matches = findMatches(rows, is4K, chart);

        double lnRatio = chart.HoldNotes / (double)Math.Max(1, chart.TotalNotes);
        double hbRatio = chart.HbRows / (double)Math.Max(1, chart.Rows.Count);
        string modeTag = lnRatio <= LnModeLowThreshold ? "RC"
            : lnRatio >= LnModeHighThreshold ? "LN"
            : hbRatio >= HbRowRatioThreshold ? "HB" : "Mix";

        // RC 谱面按 MMA 的做法丢弃三个 LN 核心型，避免把残留长条算成键型。
        if (modeTag == "RC")
            matches = matches.Where(match => !LnCorePatterns.Contains(match.Pattern)).ToList();

        double totalAmount = matches.Sum(match => Math.Max(0, match.Amount));
        if (totalAmount <= 0)
            totalAmount = 1;

        Dictionary<string, double> coreShares = aggregate(matches, match => match.Pattern, totalAmount);
        Dictionary<string, double> specificShares = aggregate(
            matches.Where(match => match.SpecificType is not null),
            match => match.SpecificType!,
            totalAmount);

        ManiaPattern pattern = new()
        {
            BeatmapId = beatmapId,
            BeatmapsetId = beatmapsetId,
            KeyCount = chart.Keys,
            TotalNotes = chart.TotalNotes,
            TotalHolds = chart.HoldNotes,
            NotesPerSecond = chart.DurationMs > 0 ? chart.TotalNotes / (chart.DurationMs / 1000.0) : 0,
            LnRatio = lnRatio,
            HbRowRatio = hbRatio,
            ModeTag = modeTag,
            SvAmount = chart.SvAmount,
            ChartsDurationMs = chart.DurationMs,
            CoreShares = coreShares,
            SpecificShares = specificShares,
            MixedShare = totalAmount <= 0 ? 0 : matches.Count(match => match.Mixed) / (double)Math.Max(1, matches.Count),
            Category = buildCategory(coreShares, specificShares, matches),
            Source = PatternSource.Osu,
            AnalyzerVersion = Version
        };
        pattern.SkillAxes = buildSkillAxes(pattern);
        SkillResult skills = computeSkills(chart, pattern);
        pattern.SkillAxes = skills.Axes;
        pattern.SkillSource = skills.Source;
        return pattern;
    }

    private sealed record SkillResult(double[] Axes, string Source);

    /// <summary>
    /// 优先用 MinaCalc 算真 MSD（Overall + 七个 skillset）；wasm 不可用时退回本地估算。
    /// 归一化按最大值缩放，保证雷达图半径范围稳定。
    /// </summary>
    private static SkillResult computeSkills(Chart chart, ManiaPattern pattern)
    {
        // 只取"有音符起点"的行：长条结束时间在我这里会生成空行（掩码 0），
        // 而 MinaCalc 收到空行会当作 junk 直接返回全 0。MMA 的 buildRows 也只收集起点时间。
        List<uint> masks = [];
        List<float> times = [];
        foreach (Row row in chart.Rows)
        {
            uint mask = 0;
            foreach (int column in row.Heads.Concat(row.Normals))
                mask |= 1u << column;
            if (mask == 0)
                continue;
            masks.Add(mask);
            times.Add((float)(row.Time / 1000.0));
        }

        double[]? msd = MinaCalc.Compute(chart.Keys, masks.ToArray(), times.ToArray());
        if (msd is { Length: >= 8 })
        {
            double[] skills = new double[7];
            double max = 0;
            for (int index = 0; index < 7; index++)
            {
                skills[index] = Math.Max(0, msd[index + 1]);
                max = Math.Max(max, skills[index]);
            }
            if (max > 0)
            {
                for (int index = 0; index < 7; index++)
                    skills[index] = Math.Round(skills[index] / max, 4);
                return new SkillResult(skills, $"MinaCalc {msd[0]:0.0}");
            }
        }

        return new SkillResult(buildSkillAxes(pattern), "本地估算");
    }

    /// <summary>把键型占比与密度折算成七轴"技能形状"。仅用于展示倾向，不是 MinaCalc。</summary>
    private static double[] buildSkillAxes(ManiaPattern pattern)    {
        double share(string name) => pattern.CoreShares.TryGetValue(name, out double value) ? value : 0;
        double specific(string name) => pattern.SpecificShares.TryGetValue(name, out double value) ? value : 0;

        double nps = pattern.NotesPerSecond;
        double seconds = pattern.ChartsDurationMs / 1000.0;
        double speed = Math.Clamp(nps / 10.0, 0, 1);
        double endurance = Math.Clamp(seconds / 180.0, 0, 1) * Math.Clamp(nps / 6.0, 0, 1);
        double trills = specific("Trills") + specific("Minitrills") + specific("Rolls");

        double[] raw =
        [
            share("Stream") * 0.8 + trills * 0.6 + speed * 0.4,
            specific("Jumpstream") * 1.2 + share("Chordstream") * 0.35 + speed * 0.2,
            specific("Handstream") * 1.2 + share("Chordstream") * 0.25,
            endurance * 1.2,
            share("Jacks") * 0.8 + specific("Minijacks") * 1.0 + speed * 0.3,
            specific("Chordjacks") * 1.4 + share("Jacks") * 0.3,
            share("Wildcard") * 0.6 + pattern.MixedShare * 0.8 + Math.Clamp(pattern.SvAmount / 4.0, 0, 1) * 0.4
        ];

        double max = raw.Max();
        if (max <= 0)
            return new double[raw.Length];
        return [.. raw.Select(value => Math.Round(Math.Clamp(value / max, 0, 1), 4))];
    }

    private static readonly HashSet<string> LnCorePatterns = ["Coordination", "Density", "Wildcard"];

    private static Dictionary<string, double> aggregate(
        IEnumerable<PatternMatch> matches,
        Func<PatternMatch, string> keySelector,
        double totalAmount) =>
        matches
            .GroupBy(keySelector)
            .ToDictionary(
                group => group.Key,
                group => Math.Round(group.Sum(match => Math.Max(0, match.Amount)) / totalAmount, 4))
            .OrderByDescending(pair => pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>对应 MMA categorise.js：主导核心型 + 最显著的子型，必要时补 Tech 后缀。</summary>
    private static string buildCategory(
        Dictionary<string, double> coreShares,
        Dictionary<string, double> specificShares,
        List<PatternMatch> matches)
    {
        if (coreShares.Count == 0)
            return "Uncategorised";

        string dominant = coreShares.First().Key;
        List<KeyValuePair<string, double>> specifics = matches
            .Where(match => match.Pattern == dominant && match.SpecificType is not null)
            .GroupBy(match => match.SpecificType!)
            .Select(group => new KeyValuePair<string, double>(
                group.Key,
                group.Sum(match => Math.Max(0, match.Amount)) / Math.Max(1, matches.Where(m => m.Pattern == dominant).Sum(m => Math.Max(0, m.Amount)))))
            .OrderByDescending(pair => pair.Value)
            .ToList();

        string name = dominant;
        if (specifics.Count > 0 && specifics[0].Value > SpecificShareFloor)
        {
            name = specifics[0].Key;
        }
        else if (specifics.Count >= 2 &&
                 specifics[0].Key == "Jumpstream" &&
                 specifics[1].Key == "Handstream" &&
                 specifics[0].Value > 0 &&
                 specifics[1].Value / specifics[0].Value > CategoryJsHsSecondaryRatio)
        {
            name = "Jumpstream/Handstream";
        }

        // MMA 里 Tech 来自"该 cluster 内部节奏不稳"，所以按该分类里 Mixed 的占比判定，
        // 不能用"出现过一次不稳"——那样几乎所有谱面都会被标成 Tech。
        List<PatternMatch> group = specifics.Count == 0
            ? matches.Where(match => match.Pattern == dominant).ToList()
            : matches.Where(match => match.Pattern == dominant && match.SpecificType == specifics[0].Key).ToList();
        double mixedRatio = group.Count == 0 ? 0 : group.Count(match => match.Mixed) / (double)group.Count;
        return mixedRatio > 0.5 ? $"{name} Tech" : name;
    }

    // ================= 谱面解析 =================

    private sealed class Chart
    {
        public int Keys { get; set; }
        public int TotalNotes { get; set; }
        public int HoldNotes { get; set; }
        public int HbRows { get; set; }
        public double DurationMs { get; set; }
        public double SvAmount { get; set; }
        public List<Row> Rows { get; } = [];
        public List<(double Time, double MsPerBeat)> Bpm { get; } = [];
    }

    private sealed class Row
    {
        public double Time { get; init; }
        public List<int> Heads { get; init; } = [];
        public List<int> Normals { get; init; } = [];
        public List<int> Bodies { get; init; } = [];
        public List<int> Tails { get; init; } = [];
    }

    private static Chart? parseChart(string text)
    {
        Chart chart = new();
        string section = string.Empty;
        List<(double Time, int Column, double EndTime)> notes = [];
        List<(double Time, double BeatLength)> timing = [];

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line;
                continue;
            }

            switch (section)
            {
                case "[Difficulty]":
                    if (line.StartsWith("CircleSize:", StringComparison.OrdinalIgnoreCase) &&
                        double.TryParse(line[11..].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double keys))
                        chart.Keys = (int)Math.Round(keys);
                    break;

                case "[TimingPoints]":
                    string[] timingParts = line.Split(',');
                    if (timingParts.Length >= 2 &&
                        double.TryParse(timingParts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double timingTime) &&
                        double.TryParse(timingParts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double beatLength))
                        timing.Add((timingTime, beatLength));
                    break;

                case "[HitObjects]":
                    string[] parts = line.Split(',');
                    if (parts.Length < 5 ||
                        !int.TryParse(parts[0], out int x) ||
                        !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double time) ||
                        !int.TryParse(parts[3], out int type))
                        continue;
                    double endTime = time;
                    bool isHold = (type & 128) != 0;
                    // 长条的结束时间是 "endTime:hitSample" 形式，只取冒号前一段。
                    if (isHold && parts.Length >= 6 &&
                        double.TryParse(parts[5].Split(':')[0], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double holdEnd))
                        endTime = holdEnd;
                    notes.Add((time, x, isHold ? endTime : time));
                    break;
            }
        }

        if (chart.Keys <= 0 || notes.Count == 0)
            return null;

        int keyCount = chart.Keys;
        chart.TotalNotes = notes.Count;
        chart.HoldNotes = notes.Count(note => note.EndTime != note.Time);

        foreach ((double time, double beatLength) in timing)
        {
            if (beatLength > 0)
                chart.Bpm.Add((time, beatLength));
        }
        chart.Bpm.Sort((left, right) => left.Time.CompareTo(right.Time));

        chart.SvAmount = computeSvAmount(timing, notes);

        // ---- 组装按时间排序的行 ----
        SortedDictionary<double, Row> rowsByTime = [];
        List<(double Start, double End, int Column)> holds = [];

        foreach ((double time, int x, double endTime) in notes)
        {
            int column = Math.Clamp((int)(x * keyCount / 512.0), 0, keyCount - 1);
            if (!rowsByTime.TryGetValue(time, out Row? row))
            {
                row = new Row { Time = time };
                rowsByTime[time] = row;
            }
            if (endTime > time)
            {
                row.Heads.Add(column);
                holds.Add((time, endTime, column));
                if (!rowsByTime.ContainsKey(endTime))
                    rowsByTime[endTime] = new Row { Time = endTime };
            }
            else
            {
                row.Normals.Add(column);
            }
        }

        foreach ((double start, double end, int column) in holds)
            rowsByTime[end].Tails.Add(column);

        // 长条持续期间的每一行都标记 body，供 Column Lock / Inverse 判定使用
        foreach (Row row in rowsByTime.Values)
        {
            foreach ((double start, double end, int column) in holds)
            {
                if (row.Time > start && row.Time < end && !row.Tails.Contains(column))
                    row.Bodies.Add(column);
            }
            row.Heads.Sort();
            row.Normals.Sort();
            row.Bodies.Sort();
            row.Tails.Sort();
            if (row.Heads.Count > 0 && row.Normals.Count > 0)
                chart.HbRows++;
        }

        chart.Rows.AddRange(rowsByTime.Values);
        chart.DurationMs = Math.Max(0, chart.Rows[^1].Time - chart.Rows[0].Time);
        return chart;
    }

    private static double computeSvAmount(List<(double Time, double BeatLength)> timing, List<(double Time, int Column, double EndTime)> notes)
    {
        if (timing.Count == 0 || notes.Count == 0)
            return 0;

        List<double> svPoints = timing.Where(point => point.BeatLength < 0).Select(point => -100.0 / point.BeatLength).ToList();
        if (svPoints.Count < 2)
            return 0;

        double nonOne = svPoints.Count(velocity => Math.Abs(velocity - 1) > 0.05);
        return nonOne >= 2 ? nonOne : 0;
    }

    // ================= primitives（对应 MMA primitives.js）=================

    private sealed class PatternRow
    {
        public double Time { get; init; }
        public double MsPerBeat { get; init; }
        public double BeatLength { get; init; }
        public int Keys { get; init; }
        public int LeftHandKeys { get; init; }
        public int Notes { get; init; }
        public int Jacks { get; init; }
        public Direction Direction { get; init; }
        public bool Roll { get; init; }
        public List<int> LnHeads { get; init; } = [];
        public List<int> LnBodies { get; init; } = [];
        public List<int> LnTails { get; init; } = [];
        public List<int> NormalNotes { get; init; } = [];
        public List<int> RawNotes { get; init; } = [];
    }

    private enum Direction { None, Left, Right, Outwards, Inwards }

    private static int keysOnLeftHand(int keymode) => keymode switch
    {
        3 or 4 => 2,
        5 or 6 => 3,
        7 or 8 => 4,
        9 or 10 => 5,
        _ => Math.Max(1, keymode / 2)
    };

    private static (Direction Direction, bool Roll) detectDirection(List<int> previous, List<int> current)
    {
        if (previous.Count == 0 || current.Count == 0)
            return (Direction.None, false);

        int previousLeft = previous[0];
        int previousRight = previous[^1];
        int currentLeft = current[0];
        int currentRight = current[^1];

        int leftChange = currentLeft - previousLeft;
        int rightChange = currentRight - previousRight;

        Direction direction = Direction.None;
        if (leftChange > 0)
            direction = rightChange > 0 ? Direction.Right : Direction.Inwards;
        else if (leftChange < 0)
            direction = rightChange < 0 ? Direction.Left : Direction.Outwards;
        else if (rightChange < 0)
            direction = Direction.Inwards;
        else if (rightChange > 0)
            direction = Direction.Outwards;

        bool isRoll = previousLeft > currentRight || previousRight < currentLeft;
        return (direction, isRoll);
    }

    private static double beatLengthAt(Chart chart, double time)
    {
        if (chart.Bpm.Count == 0)
            return 500;

        double current = chart.Bpm[0].MsPerBeat;
        foreach ((double itemTime, double msPerBeat) in chart.Bpm)
        {
            if (itemTime > time)
                break;
            current = msPerBeat;
        }
        return current;
    }

    private static List<PatternRow> buildPrimitives(Chart chart)
    {
        List<PatternRow> output = [];
        if (chart.Rows.Count < 2)
            return output;

        List<int> previous = [.. chart.Rows[0].Heads, .. chart.Rows[0].Normals];
        previous.Sort();
        if (previous.Count == 0)
            return output;

        double previousTime = chart.Rows[0].Time;
        int leftHandKeys = keysOnLeftHand(chart.Keys);

        for (int index = 1; index < chart.Rows.Count; index++)
        {
            Row row = chart.Rows[index];
            double time = row.Time;
            List<int> current = [.. row.Heads, .. row.Normals];
            current.Sort();

            if (current.Count == 0 && row.Bodies.Count == 0 && row.Tails.Count == 0)
                continue;

            Direction direction = Direction.None;
            bool roll = false;
            int jacks = 0;
            if (current.Count > 0)
            {
                (direction, roll) = detectDirection(previous, current);
                jacks = current.Count(column => previous.Contains(column));
                previous = current;
            }

            output.Add(new PatternRow
            {
                Time = time - chart.Rows[0].Time,
                MsPerBeat = (time - previousTime) * 4.0,
                BeatLength = beatLengthAt(chart, time),
                Keys = chart.Keys,
                LeftHandKeys = leftHandKeys,
                Notes = current.Count,
                Jacks = jacks,
                Direction = direction,
                Roll = roll,
                LnHeads = [.. row.Heads],
                LnBodies = [.. row.Bodies],
                LnTails = [.. row.Tails],
                NormalNotes = [.. row.Normals],
                RawNotes = current
            });
            previousTime = time;
        }

        return output;
    }

    // ================= matcher（对应 MMA patternsDef.js）=================

    private sealed record PatternMatch(string Pattern, string? SpecificType, bool Mixed, double Amount);

    private static PatternRow asHeadPointRow(PatternRow row, List<int> previousHeads)
    {
        List<int> headColumns = row.LnHeads;
        int jacks = headColumns.Count(column => previousHeads.Contains(column));
        Direction direction = Direction.None;
        bool roll = false;
        if (previousHeads.Count > 0 && headColumns.Count > 0)
            (direction, roll) = detectDirection(previousHeads, headColumns);

        return new PatternRow
        {
            Time = row.Time,
            MsPerBeat = row.MsPerBeat,
            BeatLength = row.BeatLength,
            Keys = row.Keys,
            LeftHandKeys = row.LeftHandKeys,
            Notes = headColumns.Count,
            Jacks = jacks,
            Direction = direction,
            Roll = roll,
            LnHeads = row.LnHeads,
            LnBodies = row.LnBodies,
            LnTails = row.LnTails,
            NormalNotes = [],
            RawNotes = [.. headColumns]
        };
    }

    private static List<PatternRow> headRows(List<PatternRow> window, int count)
    {
        List<PatternRow> result = [];
        List<int> previous = [];
        foreach (PatternRow row in window.Take(count))
        {
            PatternRow headRow = asHeadPointRow(row, previous);
            result.Add(headRow);
            if (headRow.RawNotes.Count > 0)
                previous = headRow.RawNotes;
        }
        return result;
    }

    private static bool isLnHeadContext(List<PatternRow> window) => window.Count > 0 && window[0].LnHeads.Count > 0;

    private static bool hasLnContext(List<PatternRow> window, int count) =>
        window.Take(count).Any(row => row.LnHeads.Count > 0 || row.LnBodies.Count > 0 || row.LnTails.Count > 0);

    private static bool inverseReady(List<PatternRow> window)
    {
        if (window.Count < 5)
            return false;
        List<PatternRow> win = window.Take(5).ToList();
        if (win.Any(row => row.NormalNotes.Count > 0))
            return false;
        if (win.Max(row => row.LnBodies.Count) < InverseMinFilledLanes)
            return false;

        List<double> gaps = [];
        for (int index = 0; index < win.Count - 1; index++)
        {
            if (win[index].LnTails.Count > 0 && win[index + 1].LnHeads.Count > 0)
                gaps.Add(win[index + 1].Time - win[index].Time);
        }
        if (gaps.Count < 2)
            return false;
        return gaps.Max() - gaps.Min() <= InverseGapToleranceMs;
    }

    private static double jackBpm(double deltaMs) => deltaMs <= 0 ? 230 : Math.Min(15000 / deltaMs, 230);

    private static int coreStream(List<PatternRow> window)
    {
        if (window.Count < 5) return 0;
        bool allSingle = window.Take(5).All(row => row.Notes == 1 && row.Jacks == 0);
        if (!allSingle) return 0;
        return window[0].RawNotes[0] != window[4].RawNotes[0] ? 5 : 0;
    }

    private static int coreJacks(List<PatternRow> window) =>
        window.Count > 0 && window[0].Jacks > 1 && window[0].MsPerBeat < 2000 ? 1 : 0;

    private static int coreChordstream(List<PatternRow> window)
    {
        if (window.Count < 4) return 0;
        PatternRow a = window[0];
        if (a.Notes <= 1 || a.Jacks != 0) return 0;
        if (window.Take(4).Skip(1).Any(row => row.Jacks != 0)) return 0;
        return window.Skip(1).Take(3).Any(row => row.Notes > 1) ? 4 : 0;
    }

    private static int coreCoordination(List<PatternRow> window) =>
        window.Count > 0 && (window[0].LnHeads.Count > 0 || window[0].LnBodies.Count > 0 || window[0].LnTails.Count > 0) ? 1 : 0;

    private static int coreDensity(List<PatternRow> window) => window.Count > 0 && isLnHeadContext(window) ? 1 : 0;

    private static int coreWildcard(List<PatternRow> window) => window.Count > 0 && isLnHeadContext(window) ? 1 : 0;

    private static int jacksChordjacks(List<PatternRow> window)
    {
        if (window.Count < 2) return 0;
        PatternRow a = window[0];
        PatternRow b = window[1];
        return a.Notes > 2 && b.Notes > 1 && b.Jacks >= 1 && (b.Notes < a.Notes || b.Jacks < b.Notes) ? 2 : 0;
    }

    private static int jacksMinijacks(List<PatternRow> window)
    {
        if (window.Count < 2) return 0;
        return window[0].Jacks > 0 && window[1].Jacks == 0 ? 2 : 0;
    }

    private static int jacksLongjacks(List<PatternRow> window)
    {
        if (window.Count < 5) return 0;
        if (window.Take(5).Any(row => row.Jacks <= 0)) return 0;
        foreach (int column in window[0].RawNotes)
        {
            if (window.Skip(1).Take(4).All(row => row.RawNotes.Contains(column)))
                return 5;
        }
        return 0;
    }

    private static int jacks4KQuadstream(List<PatternRow> window) =>
        window.Count >= 4 && window[0].Notes == 4 && window[2].Jacks == 0 && window[3].Jacks == 0 ? 4 : 0;

    private static int jacks4KGluts(List<PatternRow> window)
    {
        if (window.Count < 3) return 0;
        PatternRow a = window[0];
        PatternRow b = window[1];
        PatternRow c = window[2];
        if (b.Jacks != 1 || c.Jacks != 1) return 0;
        foreach (int column in a.RawNotes)
        {
            if (b.RawNotes.Contains(column) && c.RawNotes.Contains(column))
                return 0;
        }
        return 3;
    }

    private static int chordstream4KHandstream(List<PatternRow> window)
    {
        if (window.Count < 4) return 0;
        return window[0].Notes == 3 && window.Take(4).All(row => row.Jacks == 0) ? 4 : 0;
    }

    private static int chordstream4KJumpstream(List<PatternRow> window)
    {
        if (window.Count < 4) return 0;
        PatternRow a = window[0];
        PatternRow b = window[1];
        if (a.Notes != 2 || a.Jacks != 0 || b.Notes != 1 || b.Jacks != 0) return 0;
        if (window[2].Jacks != 0 || window[3].Jacks != 0) return 0;
        return window[2].Notes < 3 && window[3].Notes < 3 ? 4 : 0;
    }

    private static int chordstream4KDoubleJumpstream(List<PatternRow> window)
    {
        if (window.Count < 4) return 0;
        PatternRow a = window[0];
        PatternRow b = window[1];
        PatternRow c = window[2];
        PatternRow d = window[3];
        bool match = a.Notes == 1 && a.Jacks == 0 && b.Notes == 2 && b.Jacks == 0 &&
                     c.Notes == 2 && c.Jacks == 0 && d.Notes == 1 && d.Jacks == 0;
        return match ? 4 : 0;
    }

    private static int chordstream4KTripleJumpstream(List<PatternRow> window)
    {
        if (window.Count < 5) return 0;
        PatternRow a = window[0];
        PatternRow b = window[1];
        PatternRow c = window[2];
        PatternRow d = window[3];
        PatternRow e = window[4];
        bool match = a.Notes == 1 && a.Jacks == 0 && b.Notes == 2 && b.Jacks == 0 &&
                     c.Notes == 2 && c.Jacks == 0 && d.Notes == 2 && d.Jacks == 0 &&
                     e.Notes == 1 && e.Jacks == 0;
        return match ? 4 : 0;
    }

    private static int chordstream4KJumptrill(List<PatternRow> window)
    {
        if (window.Count < 4) return 0;
        bool match = window.Take(4).All(row => row.Notes == 2) && window[1].Roll && window[2].Roll && window[3].Roll;
        return match ? 4 : 0;
    }

    private static int chordstream4KSplitTrill(List<PatternRow> window)
    {
        if (window.Count < 3) return 0;
        PatternRow a = window[0];
        PatternRow b = window[1];
        PatternRow c = window[2];
        bool match = a.Notes == 2 && b.Notes == 2 && c.Notes == 2 && b.Jacks == 0 && c.Jacks == 0 && !b.Roll && !c.Roll;
        return match ? 3 : 0;
    }

    private static int stream4KRoll(List<PatternRow> window)
    {
        if (window.Count < 3) return 0;
        if (window.Take(3).Any(row => row.Notes != 1)) return 0;
        bool left = window.Take(3).All(row => row.Direction == Direction.Left);
        bool right = window.Take(3).All(row => row.Direction == Direction.Right);
        return left || right ? 3 : 0;
    }

    private static bool sameColumns(List<int> left, List<int> right) => left.SequenceEqual(right);

    private static int stream4KTrill(List<PatternRow> window)
    {
        if (window.Count < 4) return 0;
        if (window.Skip(1).Take(3).Any(row => row.Jacks != 0)) return 0;
        return sameColumns(window[0].RawNotes, window[2].RawNotes) &&
               sameColumns(window[1].RawNotes, window[3].RawNotes) ? 4 : 0;
    }

    private static int stream4KMinitrill(List<PatternRow> window)
    {
        if (window.Count < 4) return 0;
        if (window[1].Jacks != 0 || window[2].Jacks != 0) return 0;
        return sameColumns(window[0].RawNotes, window[2].RawNotes) &&
               !sameColumns(window[1].RawNotes, window[3].RawNotes) ? 4 : 0;
    }

    private static int coordinationColumnLock(List<PatternRow> window)
    {
        if (window.Count < 3) return 0;
        int split = window[0].LeftHandKeys;
        if (window[0].LnHeads.Count == 0) return 0;
        int lnColumn = window[0].LnHeads[0];

        List<int> adjacent = new[] { lnColumn - 1, lnColumn + 1 }
            .Where(column => column >= 0 && column < window[0].Keys)
            .Where(column => isSameHandAdjacent(lnColumn, column, split))
            .ToList();
        if (adjacent.Count == 0) return 0;

        foreach (int column in adjacent)
        {
            List<double> hits = window.Take(8)
                .Where(row => row.LnBodies.Contains(lnColumn) && row.NormalNotes.Contains(column))
                .Select(row => row.Time)
                .ToList();
            if (hits.Count < 3) continue;
            List<double> bpms = [];
            for (int index = 0; index < hits.Count - 1; index++)
                bpms.Add(jackBpm(hits[index + 1] - hits[index]));
            if (bpms.Count > 0 && bpms.Max() >= JackyMinBpm)
                return 3;
        }
        return 0;
    }

    private static bool isSameHandAdjacent(int left, int right, int split) =>
        Math.Abs(left - right) == 1 && (left < split) == (right < split);

    private static int coordinationShield(List<PatternRow> window)
    {
        if (window.Count < 2) return 0;
        PatternRow a = window[0];
        PatternRow b = window[1];
        double delta = b.Time - a.Time;
        if (delta < 0 || delta > b.BeatLength * ShieldMaxBeatRatio) return 0;
        if (a.NormalNotes.Any(column => b.LnHeads.Contains(column))) return 2;
        if (a.LnTails.Any(column => b.NormalNotes.Contains(column))) return 2;
        return 0;
    }

    private static int coordinationRelease(List<PatternRow> window)
    {
        if (window.Count < ReleaseMinTailRows) return 0;
        if (coordinationShield(window) != 0) return 0;
        if (inverseReady(window)) return 0;
        if (wildcardJack(window) != 0) return 0;

        List<PatternRow> picked = window.Take(ReleaseScanRows).Where(row => row.LnTails.Count == 1).ToList();
        if (picked.Count < ReleaseMinTailRows) return 0;

        int useRows = Math.Min(ReleaseFullMatchRows, picked.Count);
        List<int> tails = picked.Take(useRows).Select(row => row.LnTails[0]).ToList();

        List<int> previous = [tails[0]];
        List<PatternRow> rows = [];
        for (int index = 0; index < useRows; index++)
        {
            PatternRow row = picked[index];
            List<int> current = [tails[index]];
            (Direction direction, bool roll) = detectDirection(previous, current);
            rows.Add(new PatternRow
            {
                Time = row.Time,
                MsPerBeat = row.MsPerBeat,
                BeatLength = row.BeatLength,
                Keys = row.Keys,
                LeftHandKeys = row.LeftHandKeys,
                Notes = 1,
                Jacks = current[0] == previous[0] ? 1 : 0,
                Direction = direction,
                Roll = roll,
                LnHeads = row.LnHeads,
                LnBodies = row.LnBodies,
                LnTails = row.LnTails,
                NormalNotes = [],
                RawNotes = current
            });
            previous = current;
        }

        List<PatternRow> effective = rows.Count > 1 ? rows.Skip(1).ToList() : [];
        if (effective.Count < ReleaseRollPoints) return 0;

        bool matched;
        // MMA 的 RELEASE_ROLL_POINTS 常量为 2，走下面的双尾判定分支。
        {
            int a = effective[0].RawNotes[0];
            int b = effective.Count > 1 ? effective[1].RawNotes[0] : a;
            double delta = effective.Count > 1 ? effective[1].Time - effective[0].Time : 0;
            matched = a != b && delta > 0;
        }

        if (!matched) return 0;
        return useRows >= ReleaseFullMatchRows ? 5 : 4;
    }

    private static int density4KJumpstream(List<PatternRow> window)
    {
        if (window.Count < 4 || !isLnHeadContext(window)) return 0;
        return chordstream4KJumpstream(headRows(window, 4)) != 0 ? 4 : 0;
    }

    private static int density4KHandstream(List<PatternRow> window)
    {
        if (window.Count < 4 || !isLnHeadContext(window)) return 0;
        return chordstream4KHandstream(headRows(window, 4)) != 0 ? 4 : 0;
    }

    private static int density4KInverse(List<PatternRow> window) => inverseReady(window) ? 5 : 0;

    private static int wildcardJack(List<PatternRow> window)
    {
        if (window.Count < 2 || !hasLnContext(window, JackyContextWindow)) return 0;

        List<PatternRow> rows = window.Take(Math.Max(4, JackyContextWindow)).Where(row => row.Notes > 0).ToList();
        if (rows.Count < 2) return 0;

        if (jacksChordjacks(rows) != 0 || jacksMinijacks(rows) != 0) return 4;

        List<PatternRow> checkRows = rows.Take(Math.Min(4, rows.Count)).ToList();
        int jackRows = checkRows.Count(row => row.Jacks > 0);
        if (jackRows >= 2 && checkRows.Any(row => row.Notes >= 2)) return 3;

        double fastestMspb = checkRows.Min(row => row.MsPerBeat);
        return jackRows >= 2 && fastestMspb <= JackyFallbackMaxMspb ? 3 : 0;
    }

    private static int wildcardSpeed(List<PatternRow> window)
    {
        if (window.Count < 2 || !hasLnContext(window, 4)) return 0;
        List<PatternRow> rows = headRows(window, Math.Min(4, window.Count));
        if (window[0].Keys == 4)
        {
            if (rows.Count >= 3 && stream4KRoll(rows.Take(3).ToList()) != 0) return 3;
            if (rows.Count >= 2)
            {
                bool sameDirection = (rows[0].Direction is Direction.Left or Direction.Right) &&
                                     rows[0].Direction == rows[1].Direction;
                if (sameDirection || rows[0].MsPerBeat <= 180) return 3;
            }
        }
        else
        {
            if (rows.Count >= 2)
            {
                bool condition = rows[0].Notes >= 2 && rows[1].Notes >= 2 &&
                                 rows[0].Direction == rows[1].Direction &&
                                 (rows[0].Direction is Direction.Left or Direction.Right);
                if (condition || rows[0].MsPerBeat <= 170) return 3;
            }
        }
        return 0;
    }

    // ---- 4K / 通用子型注册表（对齐 SPECIFIC_4K 的匹配顺序）----

    private sealed record SpecificDetector(string Name, Func<List<PatternRow>, int> Detector);

    private static List<SpecificDetector> streamSpecifics4K() =>
    [
        new("Rolls", stream4KRoll),
        new("Trills", stream4KTrill),
        new("Minitrills", stream4KMinitrill)
    ];

    private static List<SpecificDetector> chordstreamSpecifics4K() =>
    [
        new("Handstream", chordstream4KHandstream),
        new("Split Trill", chordstream4KSplitTrill),
        new("Jumptrill", chordstream4KJumptrill),
        new("Jumpstream", chordstream4KJumpstream)
    ];

    private static List<SpecificDetector> jackSpecifics4K() =>
    [
        new("Longjacks", jacksLongjacks),
        new("Quadstream", jacks4KQuadstream),
        new("Gluts", jacks4KGluts),
        new("Chordjacks", jacksChordjacks),
        new("Minijacks", jacksMinijacks)
    ];

    private static List<SpecificDetector> jackSpecificsOther() =>
    [
        new("Longjacks", jacksLongjacks),
        new("Chordjacks", jacksChordjacks),
        new("Minijacks", jacksMinijacks)
    ];

    private static List<SpecificDetector> coordinationSpecifics() =>
    [
        new("Column Lock", coordinationColumnLock),
        new("Release", coordinationRelease),
        new("Shield", coordinationShield)
    ];

    private static List<SpecificDetector> densitySpecifics4K() =>
    [
        new("Inverse", density4KInverse),
        new("JS Density", density4KJumpstream),
        new("HS Density", density4KHandstream)
    ];

    private static List<SpecificDetector> wildcardSpecifics() =>
    [
        new("Speedy WC", wildcardSpeed),
        new("Jacky WC", wildcardJack)
    ];

    private static List<PatternMatch> findMatches(List<PatternRow> rows, bool is4K, Chart chart)
    {
        List<PatternMatch> results = [];
        double lastNote = Math.Max(0, chart.DurationMs);

        List<SpecificDetector> streamSpecifics = is4K ? streamSpecifics4K() : [];
        List<SpecificDetector> chordstreamSpecifics = is4K ? chordstreamSpecifics4K() : [];
        List<SpecificDetector> jackSpecifics = is4K ? jackSpecifics4K() : jackSpecificsOther();
        List<SpecificDetector> coordinationList = coordinationSpecifics();
        List<SpecificDetector> densitySpecifics = is4K ? densitySpecifics4K() : [new("Inverse", density4KInverse)];
        List<SpecificDetector> wildcardList = wildcardSpecifics();

        for (int start = 0; start < rows.Count; start++)
        {
            List<PatternRow> window = rows.Skip(start).Take(MatcherWindow).ToList();
            appendCoreMatches(results, "Stream", coreStream(window), streamSpecifics, window, lastNote);
            appendCoreMatches(results, "Chordstream", coreChordstream(window), chordstreamSpecifics, window, lastNote);
            appendCoreMatches(results, "Jacks", coreJacks(window), jackSpecifics, window, lastNote);
            appendCoreMatches(results, "Coordination", coreCoordination(window), coordinationList, window, lastNote);
            appendCoreMatches(results, "Density", coreDensity(window), densitySpecifics, window, lastNote);
            appendCoreMatches(results, "Wildcard", coreWildcard(window), wildcardList, window, lastNote);
        }

        return results;
    }

    private static void appendCoreMatches(
        List<PatternMatch> results,
        string pattern,
        int coreLength,
        List<SpecificDetector> specifics,
        List<PatternRow> window,
        double lastNote)
    {
        if (coreLength == 0)
            return;

        List<(int Length, string Name)> matched = [];
        foreach (SpecificDetector detector in specifics)
        {
            int length = detector.Detector(window);
            if (length != 0)
                matched.Add((length, detector.Name));
        }

        if (matched.Count == 0)
        {
            appendFoundPattern(results, pattern, null, coreLength, window, lastNote);
            return;
        }

        foreach ((int length, string name) in matched)
            appendFoundPattern(results, pattern, name, Math.Max(coreLength, length), window, lastNote);
    }

    private static void appendFoundPattern(
        List<PatternMatch> results,
        string pattern,
        string? specificType,
        int length,
        List<PatternRow> window,
        double lastNote)
    {
        List<PatternRow> slice = window.Take(length).ToList();
        if (slice.Count == 0)
            return;

        double meanMspb = slice.Average(row => row.MsPerBeat);
        bool mixed = !slice.All(row => Math.Abs(row.MsPerBeat - meanMspb) < PatternStabilityThreshold);

        double start = window[0].Time;
        double end;
        if (pattern == "Jacks")
        {
            double candidate = length < window.Count ? window[length].Time : lastNote;
            end = Math.Max(window[0].Time + window[0].MsPerBeat * 0.5, candidate);
        }
        else
        {
            end = length < window.Count ? window[length].Time : lastNote;
        }

        if (end <= start)
            end = start + 1;
        results.Add(new PatternMatch(pattern, specificType, mixed, end - start));
    }
}
