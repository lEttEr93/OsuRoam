using System.Globalization;

namespace OsuRoam.Services;

/// <summary>
/// osu!mania 星数（难度）计算器的精简移植，对齐 lazer 的 <c>ManiaDifficultyCalculator</c>（Version 20241007）。
///
/// 只覆盖默认 mod（NM）：不带 DT/HT/EZ/HR，也不做键数转换。算法链路：
///   HitObjects → DifficultyHitObject（逐列 strain time） → Strain 技能
///   → StrainSkill 的 400ms 分段取峰值 → 按 0.9 权重求和 → × 0.018 = 星数
///
/// 校验方式：算出来的值要和 osu! API 的 <c>difficulty_rating</c> 对上（同一张谱误差应远小于 0.01）。
/// 用途之一是 <see cref="ComputePair"/>：把长条头降成单点、删掉身体与尾，再算一次，
/// 两个星数之差就是「长条贡献了多少难度」。
/// </summary>
public static class ManiaStarRating
{
    private const double DifficultyMultiplier = 0.018;
    private const double IndividualDecayBase = 0.125;
    private const double OverallDecayBase = 0.30;
    private const double HoldFactor = 1.25;
    private const double ReleaseThresholdMs = 30;
    private const double SectionLength = 400;
    private const double SectionDecayWeight = 0.9;

    public readonly record struct Note(int Column, double StartTime, double EndTime)
    {
        public bool IsHold => EndTime > StartTime;
    }

    /// <summary>原谱星数。</summary>
    public static double? Compute(string osuText) => Compute(osuText, stripHolds: false);

    /// <summary>
    /// 算星数。<paramref name="stripHolds"/> 为 true 时把每根长条压成起点头上的单点
    /// （删掉身体与尾），得到「米线视图」的星数。
    /// </summary>
    public static double? Compute(string osuText, bool stripHolds)
    {
        if (!TryParse(osuText, out List<Note> notes, out int keys) || notes.Count < 2)
            return null;

        return Compute(notes, keys, stripHolds);
    }

    public static double Compute(IReadOnlyList<Note> notes, int keys, bool stripHolds = false)
    {
        if (keys <= 0 || notes.Count < 2)
            return 0;

        List<Note> sorted = new(notes.Count);
        foreach (Note note in notes)
            sorted.Add(stripHolds ? note with { EndTime = note.StartTime } : note);

        // lazer 用 LegacySortHelper 按四舍五入后的开始时间排序（稳定）。
        List<Note> ordered = [.. sorted.OrderBy(note => (int)Math.Round(note.StartTime))];

        List<DiffObject> objects = build(ordered, keys);
        return computeStrain(objects, keys) * DifficultyMultiplier;
    }

    /// <summary>原谱星数与「删掉长条」后的星数。</summary>
    public static (double Star, double RiceStar)? ComputePair(string osuText)
    {
        if (!TryParse(osuText, out List<Note> notes, out int keys) || notes.Count < 2)
            return null;

        return (Compute(notes, keys, stripHolds: false), Compute(notes, keys, stripHolds: true));
    }

    // ---- 预处理：DifficultyHitObject ----

    private sealed class DiffObject
    {
        public int Index;
        public int Column;
        public double StartTime;
        public double EndTime;
        public double DeltaTime;
        public double ColumnStrainTime;
        public DiffObject?[] PreviousPerColumn = [];
    }

    private static List<DiffObject> build(List<Note> sorted, int keys)
    {
        List<DiffObject> objects = new(Math.Max(0, sorted.Count - 1));
        DiffObject?[] carry = new DiffObject?[keys];
        DiffObject? last = null;

        // lazer 从第二个物件开始建（第一个物件不参与 strain）。
        for (int index = 1; index < sorted.Count; index++)
        {
            Note current = sorted[index];
            int column = Math.Clamp(current.Column, 0, keys - 1);

            if (last is not null)
                carry[last.Column] = last;

            DiffObject? previousInColumn = carry[column];
            DiffObject obj = new()
            {
                Index = index - 1,
                Column = column,
                StartTime = current.StartTime,
                EndTime = current.EndTime,
                DeltaTime = current.StartTime - sorted[index - 1].StartTime,
                ColumnStrainTime = previousInColumn is null
                    ? current.StartTime
                    : current.StartTime - previousInColumn.StartTime,
                PreviousPerColumn = (DiffObject?[])carry.Clone()
            };

            objects.Add(obj);
            last = obj;
        }

        return objects;
    }

    // ---- 技能：Strain ----

    private static double computeStrain(List<DiffObject> objects, int keys)
    {
        double[] individualStrains = new double[keys];
        double highestIndividual = 0;
        double overall = 1; // lazer 初值是 1，不是 0
        double currentStrain = 0;

        double sectionPeak = 0;
        double sectionEnd = 0;
        List<double> peaks = [];

        for (int index = 0; index < objects.Count; index++)
        {
            DiffObject current = objects[index];

            if (current.Index == 0)
                sectionEnd = Math.Ceiling(current.StartTime / SectionLength) * SectionLength;

            while (current.StartTime > sectionEnd)
            {
                peaks.Add(sectionPeak);
                sectionPeak = initialStrain(objects, index, sectionEnd, highestIndividual, overall);
                sectionEnd += SectionLength;
            }

            individualStrains[current.Column] =
                decay(individualStrains[current.Column], current.ColumnStrainTime, IndividualDecayBase) + individualValue(current);

            highestIndividual = current.DeltaTime <= 1
                ? Math.Max(highestIndividual, individualStrains[current.Column])
                : individualStrains[current.Column];

            overall = decay(overall, current.DeltaTime, OverallDecayBase) + overallValue(current);

            // StrainDecaySkill 的 CurrentStrain 会先乘 1^Δt（恒等），再加上 StrainValueOf，
            // 而 StrainValueOf 返回 highest + overall − CurrentStrain，所以结算后正好等于 highest + overall。
            // 注意 StrainValueAt 返回的是**结算后的** CurrentStrain，子段的峰值要拿它来比。
            double strainAt = highestIndividual + overall - currentStrain;
            currentStrain += strainAt;

            sectionPeak = Math.Max(currentStrain, sectionPeak);
        }

        peaks.Add(sectionPeak);
        return weightedSum(peaks);
    }

    private static double initialStrain(List<DiffObject> objects, int index, double time, double highestIndividual, double overall)
    {
        if (index == 0)
            return 0;

        double previous = objects[index - 1].StartTime;
        double delta = time - previous;
        return decay(highestIndividual, delta, IndividualDecayBase) + decay(overall, delta, OverallDecayBase);
    }

    private static double weightedSum(List<double> peaks)
    {
        double difficulty = 0;
        double weight = 1;
        foreach (double peak in peaks.Where(value => value > 0).OrderDescending())
        {
            difficulty += peak * weight;
            weight *= SectionDecayWeight;
        }

        return difficulty;
    }

    /// <summary>IndividualStrainEvaluator：基础 2.0，被别的长条罩住时 ×1.25。</summary>
    private static double individualValue(DiffObject current)
    {
        foreach (DiffObject? previous in current.PreviousPerColumn)
        {
            if (previous is null)
                continue;
            if (previous.EndTime > current.EndTime + 1 && current.StartTime > previous.StartTime + 1)
                return 2.0 * HoldFactor;
        }

        return 2.0;
    }

    /// <summary>OverallStrainEvaluator：按住加成 + 「释放太挤」惩罚。</summary>
    private static double overallValue(DiffObject current)
    {
        double startTime = current.StartTime;
        double endTime = current.EndTime;
        bool isOverlapping = false;

        double closestEndTime = Math.Abs(endTime - startTime);
        double holdFactor = 1.0;
        double holdAddition = 0;

        foreach (DiffObject? previous in current.PreviousPerColumn)
        {
            if (previous is null)
                continue;

            isOverlapping |= previous.EndTime > startTime + 1
                             && endTime > previous.EndTime + 1
                             && startTime > previous.StartTime + 1;

            if (previous.EndTime > endTime + 1 && startTime > previous.StartTime + 1)
                holdFactor = HoldFactor;

            closestEndTime = Math.Min(closestEndTime, Math.Abs(endTime - previous.EndTime));
        }

        if (isOverlapping)
            holdAddition = logistic(closestEndTime, ReleaseThresholdMs, 0.27);

        return (1 + holdAddition) * holdFactor;
    }

    /// <summary>lazer 的 DiffUtils.Logistic：max / (1 + e^(multiplier·(offset − x)))。</summary>
    private static double logistic(double x, double midpointOffset, double multiplier) =>
        1.0 / (1 + Math.Exp(multiplier * (midpointOffset - x)));

    private static double decay(double value, double milliseconds, double decayBase) =>
        value * Math.Pow(decayBase, milliseconds / 1000.0);

    // ---- 谱面解析 ----

    public static bool TryParse(string osuText, out List<Note> notes, out int keys)
    {
        notes = [];
        keys = 0;
        if (string.IsNullOrEmpty(osuText))
            return false;

        List<(int X, double Start, double End)> raw = [];
        bool inHitObjects = false;
        int mode = 3;
        double circleSize = 0;

        foreach (string line in osuText.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
                continue;

            if (trimmed[0] == '[')
            {
                inHitObjects = trimmed.StartsWith("[HitObjects]", StringComparison.Ordinal);
                continue;
            }

            if (!inHitObjects)
            {
                if (trimmed.StartsWith("Mode:", StringComparison.Ordinal))
                    int.TryParse(trimmed[5..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out mode);
                else if (trimmed.StartsWith("CircleSize:", StringComparison.Ordinal))
                    double.TryParse(trimmed[11..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out circleSize);
                continue;
            }

            string[] parts = trimmed.Split(',');
            if (parts.Length < 5)
                continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x))
                continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double start))
                continue;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                continue;

            double end = start;
            if ((type & 128) != 0 && parts.Length > 5)
            {
                // 长条的结束时间在后半段：`…,128,0,1687:0:0:0:0:`，冒号后面是音效信息。
                string endText = parts[5];
                int colon = endText.IndexOf(':');
                if (colon >= 0)
                    endText = endText[..colon];
                if (!double.TryParse(endText, NumberStyles.Float, CultureInfo.InvariantCulture, out end))
                    end = start;
            }

            raw.Add((x, start, Math.Max(start, end)));
        }

        keys = (int)Math.Round(circleSize);
        if (mode != 3 || keys <= 0 || raw.Count == 0)
            return false;

        notes = new List<Note>(raw.Count);
        foreach ((int x, double start, double end) in raw)
        {
            int column = Math.Clamp((int)(x * keys / 512.0), 0, keys - 1);
            notes.Add(new Note(column, start, end));
        }

        return true;
    }
}
