using System.Globalization;
using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 变速（SV）评估：把谱面的滚动速度变化拆成四个可算的量。
/// 完整口径见 <c>docs/1.5.3-SV评估算法.md</c>，这里只复述实现要点。
///
/// **速度模型**（lazer）：一屏用时 <c>C = 11485 / 流速</c>；音符的屏幕速度正比于
/// <c>m(t) = SV(t) × BPM(t) / BPM_基准</c>，其中 BPM_基准 是谱面「最常见」的红线拍长
/// （按累计时长加权，见 <c>MultiplierControlPoint</c> 的 RelativeScaleBeatLengths）。
/// 绝对 BPM 被约掉，所以 220 和 310 BPM 同速；谱内改 BPM 和改 SV 等价。
///
/// **飞行时长**：令 <c>P(t) = ∫m du</c>，解 <c>P(t_i) − P(a_i) = C</c> 得 <c>T_i = t_i − a_i</c>。
/// P 分段线性且单调，所以一次二分就能反解，不用逐段积分。
///
/// 四个量（都是负向或正向单极，**不合成单一分数**）：
/// 1. <see cref="SvMetrics.Irregularity"/>：结构上的不等时（切换频率 + 局部带宽）；
/// 2. <see cref="SvMetrics.Extremeness"/>：飞行时长偏离标称值多少（±10% 容忍带外才计）；
/// 3. <see cref="SvMetrics.Memorization"/>：<c>A² + (1−A)·B</c>，结构差时 B 的边际贡献被压缩；
/// 4. <see cref="SvMetrics.Discomfort"/>：切换时「幅度 × 当时在飞的音符数」累计；
/// 5. <see cref="SvMetrics.Exaggeration"/>：飞行途中相对自身平均速度的偏离积分。
///
/// 只处理 mania（键型分析也只做 mania）；不做 DT/HT/键数转换。
/// </summary>
public static class SvEvaluator
{
    /// <summary>lazer <c>DrawableScrollingRuleset.MAX_TIME_RANGE</c>：一屏对应的毫秒·倍率。</summary>
    public const double MaxTimeRange = 11485;

    /// <summary>默认流速（mania 的 Scroll speed 默认 25）。</summary>
    public const double DefaultScrollSpeed = 25;

    /// <summary>飞行时长偏离的容忍带：±10%。</summary>
    private const double Tolerance = 0.10;

    /// <summary>ln(1.18)：切换/偏离的最小倍数门槛。</summary>
    private const double MinLogRatio = 0.16551443847757333;

    /// <summary>切换的最小绝对差值（毫秒），挡住「倍数够但肉眼无感」的小抖动。</summary>
    private const double MinDeltaMs = 50;

    /// <summary>窗口最少行数。</summary>
    private const int MinWindowRows = 4;

    /// <summary>窗口最少时间跨度（毫秒）。</summary>
    private const double MinWindowSpanMs = 400;

    /// <summary>窗口内相邻两行的最大空档（毫秒），超过就当这段没有节奏支撑。</summary>
    private const double MaxRowGapMs = 500;

    /// <summary>切换事件的最小间隔（毫秒），附近再算到同一个事件里。</summary>
    private const double SwitchMergeMs = 1500;

    /// <summary>切换前后窗口的带宽下限：低于这个值就认为「基线干净」。</summary>
    private const double SpreadFloor = 0.22;

    /// <summary>带宽上限随切换幅度放大，免得把「渐变」当成「切换」。</summary>
    private const double SpreadFactor = 0.8;

    /// <summary>切换频率到这个数（次/分）就算结构最差。</summary>
    private const double SwitchSaturationPerMinute = 6;

    /// <summary>局部带宽到这个数就算结构最差。</summary>
    private const double WidthSaturation = 0.5;

    private const double SwitchWeight = 0.65;
    private const double WidthWeight = 0.35;

    /// <summary>单个切换事件最多扣这么多不适（再猛也不至于一击毙命）。</summary>
    private const double DiscomfortEventCap = 0.6;

    /// <summary>不适度的半饱和音符数：g(n) = n / (n + 8)。</summary>
    private const double DiscomfortHalfNotes = 8;

    private const double LogTwo = 0.6931471805599453;

    /// <summary>
    /// 算一张谱的 SV 评估。缺数据（非 mania、没有时间点、行数太少）时返回 null。
    /// </summary>
    /// <param name="osuText">.osu 原文。</param>
    /// <param name="scrollSpeed">osu! 里的 Scroll speed，决定一屏用时。</param>
    public static SvMetrics? Compute(string osuText, double scrollSpeed = DefaultScrollSpeed)
    {
        if (string.IsNullOrEmpty(osuText) || !osuText.StartsWith("osu file format", StringComparison.Ordinal))
            return null;
        if (!(scrollSpeed > 0))
            return null;

        List<TimingPoint> timing = [];
        List<double> heads = [];
        if (!tryParse(osuText, timing, heads, out double endTime))
            return null;
        if (timing.Count == 0 || heads.Count < 40)
            return null;

        double baseBeatLength = mostCommonBeatLength(timing, endTime);
        if (!(baseBeatLength > 0))
            return null;

        Timeline timeline = Timeline.Build(timing, baseBeatLength);
        if (timeline.Count == 0)
            return null;

        double screenTime = MaxTimeRange / scrollSpeed;

        // ---- 时间行：飞行时长 ----
        // 开头那一小段距离不够一屏，反解出来的飞行时长没有意义，直接丢掉（和探针口径一致）。
        List<double> rowTimes = [];
        List<double> rowFlights = [];
        foreach (double head in heads)
        {
            double flight = timeline.VisibleTime(head, screenTime);
            if (flight <= 0.001)
                continue;
            rowTimes.Add(head);
            rowFlights.Add(flight);
        }

        int count = rowTimes.Count;
        if (count < 40)
            return null;

        double[] times = [.. rowTimes];
        double[] flights = [.. rowFlights];
        double[] buffer = new double[Math.Min(count, 512)];

        double[] sortedFlights = (double[])flights.Clone();
        Array.Sort(sortedFlights);
        double medianFlight = percentile(sortedFlights, 0.5);

        double localWidth = localWidthMedian(times, flights, buffer);
        List<(double Time, double Change)> switches = findSwitches(times, flights, buffer);

        double minutes = Math.Max(1 / 60.0, (times[^1] - times[0]) / 60000.0);
        double switchesPerMinute = switches.Count / minutes;

        // ---- A：结构上的不等时 ----
        double irregularity = Math.Clamp(
            SwitchWeight * Math.Min(1, switchesPerMinute / SwitchSaturationPerMinute) +
            WidthWeight * Math.Min(1, localWidth / WidthSaturation), 0, 1);

        // ---- B：飞行时长偏离标称值的程度（容忍带内记 0） ----
        double toleranceLog = Math.Log(1 + Tolerance);
        double[] offsets = new double[count];
        double sum = 0;
        for (int index = 0; index < count; index++)
        {
            double deviation = Math.Abs(Math.Log(Math.Max(1e-6, flights[index]) / screenTime));
            offsets[index] = Math.Clamp((deviation - toleranceLog) / LogTwo, 0, 1);
            sum += offsets[index];
        }

        Array.Sort(offsets);
        double extremeness = 0.5 * (sum / count) + 0.5 * percentile(offsets, 0.75);

        // ---- 背谱：结构优先，A 已经差时 B 的边际贡献被压缩 ----
        double memorization = Math.Clamp(irregularity * irregularity + (1 - irregularity) * extremeness, 0, 1);

        // ---- 不适：切换时「幅度 × 在飞音符数」的软和 ----
        double[] appears = new double[count];
        for (int index = 0; index < count; index++)
            appears[index] = times[index] - flights[index];
        Array.Sort(appears);

        double penalty = 0;
        int notes = 0;
        foreach ((double time, double change) in switches)
        {
            int flying = upperBound(appears, time) - upperBound(times, time);
            if (flying < 0)
                flying = 0;
            notes += flying;

            double severity = change / (change + LogTwo);
            double carry = flying / (flying + DiscomfortHalfNotes);
            penalty += Math.Min(DiscomfortEventCap, severity * carry);
        }

        double discomfort = 1 - Math.Exp(-penalty);
        double switchNotesMean = switches.Count > 0 ? notes / (double)switches.Count : 0;

        // ---- 夸张：飞行途中速度相对自身平均速度的偏离 ----
        double exaggerationSum = 0;
        int exaggerationCount = 0;
        for (int index = 0; index < count; index++)
        {
            double value = flightExaggeration(timeline, times[index], flights[index], screenTime);
            if (value < 0)
                continue;
            exaggerationSum += value;
            exaggerationCount++;
        }

        double exaggeration = exaggerationCount > 0 ? exaggerationSum / exaggerationCount : 0;

        return new SvMetrics
        {
            Irregularity = irregularity,
            Extremeness = extremeness,
            Memorization = memorization,
            Discomfort = discomfort,
            Exaggeration = exaggeration,
            Rows = count,
            ScreenTimeMs = screenTime,
            MedianFlightMs = medianFlight,
            SwitchesPerMinute = switchesPerMinute,
            LocalWidth = localWidth,
            SwitchNotesMean = switchNotesMean
        };
    }

    // ---- 指标实现 ----

    /// <summary>1 秒滑动窗口里「(p90 − p10) / 中位」的中位值：越稳越接近 0。</summary>
    private static double localWidthMedian(double[] times, double[] flights, double[] buffer)
    {
        double[] widths = new double[times.Length];
        int found = 0;
        int left = 0;
        for (int right = 0; right < times.Length; right++)
        {
            while (times[right] - times[left] > 1000)
                left++;

            int size = right - left + 1;
            if (size < 3)
                continue;

            if (size > buffer.Length)
                buffer = new double[size];
            Array.Copy(flights, left, buffer, 0, size);
            Array.Sort(buffer, 0, size);

            double median = percentile(buffer, size, 0.5);
            if (median <= 0)
                continue;

            widths[found++] = (percentile(buffer, size, 0.9) - percentile(buffer, size, 0.1)) / median;
        }

        if (found == 0)
            return 0;

        Array.Sort(widths, 0, found);
        return percentile(widths, found, 0.5);
    }

    /// <summary>
    /// 基线切换：前后各 1 秒窗口都要有节奏支撑，中位飞行时长跳 ≥1.18 倍、
    /// 绝对差 ≥50ms，且两个窗口各自的带宽不能太大（否则那是渐变不是切换）。
    /// </summary>
    private static List<(double Time, double Change)> findSwitches(double[] times, double[] flights, double[] buffer)
    {
        List<(double Time, double Change)> accepted = [];
        double last = double.NegativeInfinity;

        for (int index = 0; index < times.Length; index++)
        {
            double time = times[index];
            if (!window(times, flights, time - 1000, time, ref buffer, out WindowStats before) ||
                !window(times, flights, time, time + 1000, ref buffer, out WindowStats after))
            {
                continue;
            }

            if (before.Median <= 0 || after.Median <= 0)
                continue;

            double change = Math.Abs(Math.Log(before.Median / after.Median));
            if (change < MinLogRatio || Math.Abs(before.Median - after.Median) < MinDeltaMs)
                continue;
            if (Math.Max(before.Spread, after.Spread) > Math.Max(SpreadFloor, SpreadFactor * change))
                continue;
            if (time - last <= SwitchMergeMs)
                continue;

            last = time;
            accepted.Add((time, change));
        }

        return accepted;
    }

    private readonly record struct WindowStats(double Median, double Spread);

    /// <summary>
    /// 取 [from, to) 的窗口统计；行数不够、跨度太短或中间有超过 500ms 的空档时返回 false。
    /// </summary>
    private static bool window(double[] times, double[] flights, double from, double to, ref double[] buffer, out WindowStats stats)
    {
        stats = default;
        int start = lowerBound(times, from);
        int end = lowerBound(times, to);
        int size = end - start;
        if (size < MinWindowRows)
            return false;
        if (times[end - 1] - times[start] < MinWindowSpanMs)
            return false;

        for (int index = start + 1; index < end; index++)
        {
            if (times[index] - times[index - 1] > MaxRowGapMs)
                return false;
        }

        if (size > buffer.Length)
            buffer = new double[size];
        Array.Copy(flights, start, buffer, 0, size);
        Array.Sort(buffer, 0, size);

        double median = percentile(buffer, size, 0.5);
        double spread = median > 0
            ? Math.Log(Math.Max(1e-6, percentile(buffer, size, 0.9)) / Math.Max(1e-6, percentile(buffer, size, 0.1)))
            : 0;
        stats = new WindowStats(median, spread);
        return true;
    }

    /// <summary>
    /// 音符飞行途中速度相对它自己平均速度偏离多少：(1/T)∫|ln(V(u)/V̄)|du。
    /// 匀速飞行 = 0；中途加速/刹车 = 正数。
    /// </summary>
    private static double flightExaggeration(Timeline timeline, double headTime, double flight, double screenTime)
    {
        if (flight <= 0.001)
            return -1;

        double appear = headTime - flight;
        double averageSpeed = 1.0 / flight; // 屏幕/毫秒
        double total = 0;
        int first = timeline.IndexAt(appear);

        for (int index = first; index < timeline.Count; index++)
        {
            double segmentStart = Math.Max(timeline.StartAt(index), appear);
            double segmentEnd = index + 1 < timeline.Count ? Math.Min(timeline.StartAt(index + 1), headTime) : headTime;
            if (segmentEnd <= segmentStart)
            {
                if (index > first && timeline.StartAt(index) >= headTime)
                    break;
                continue;
            }

            double speed = timeline.SpeedAt(index) / screenTime; // 屏幕/毫秒
            if (speed <= 0)
                continue;

            total += Math.Abs(Math.Log(speed / averageSpeed)) * (segmentEnd - segmentStart);
        }

        return total / flight;
    }

    // ---- 解析 ----

    private readonly record struct TimingPoint(double Time, double BeatLength, bool Uninherited);

    private static bool tryParse(string osuText, List<TimingPoint> timing, List<double> heads, out double endTime)
    {
        endTime = 0;
        int mode = 3;
        double circleSize = 0;
        string section = string.Empty;
        List<double> rawHeads = [];

        foreach (string raw in osuText.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            if (line[0] == '[')
            {
                section = line;
                continue;
            }

            switch (section)
            {
                case "[General]":
                    if (line.StartsWith("Mode:", StringComparison.Ordinal))
                        int.TryParse(line[5..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out mode);
                    break;

                case "[Difficulty]":
                    if (line.StartsWith("CircleSize:", StringComparison.Ordinal))
                        double.TryParse(line[11..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out circleSize);
                    break;

                case "[TimingPoints]":
                {
                    string[] parts = line.Split(',');
                    if (parts.Length < 2)
                        break;
                    if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time))
                        break;
                    if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength))
                        break;
                    // 第 7 列是 uninherited 标记；缺列时按红线处理。
                    bool uninherited = parts.Length < 7 || !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int flag) || flag != 0;
                    timing.Add(new TimingPoint(time, beatLength, uninherited));
                    break;
                }

                case "[HitObjects]":
                {
                    string[] parts = line.Split(',');
                    if (parts.Length < 4)
                        break;
                    if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double start))
                        break;
                    if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                        break;

                    rawHeads.Add(start);
                    double objectEnd = start;
                    if ((type & 128) != 0 && parts.Length > 5)
                    {
                        // 长条结束时间在 `…,128,0,1687:0:0:0:0:` 里，冒号后面是音效信息。
                        string endText = parts[5];
                        int colon = endText.IndexOf(':');
                        if (colon >= 0)
                            endText = endText[..colon];
                        if (!double.TryParse(endText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                            parsed = start;
                        objectEnd = Math.Max(start, parsed);
                    }

                    if (objectEnd > endTime)
                        endTime = objectEnd;
                    break;
                }
            }
        }

        int keys = (int)Math.Round(circleSize);
        if (mode != 3 || keys <= 0)
            return false;

        timing.Sort((left, right) => left.Time.CompareTo(right.Time));
        rawHeads.Sort();

        // 同一时刻的多个音符只算一行（和弦不重复计时）。
        foreach (double head in rawHeads)
        {
            if (heads.Count == 0 || head - heads[^1] > 1)
                heads.Add(head);
        }

        if (endTime <= 0 && rawHeads.Count > 0)
            endTime = rawHeads[^1];
        return heads.Count > 0;
    }

    /// <summary>lazer 的 GetMostCommonBeatLength：按累计时长取最常见的红线拍长。</summary>
    private static double mostCommonBeatLength(List<TimingPoint> timing, double endTime)
    {
        List<TimingPoint> reds = [.. timing.Where(point => point.Uninherited)];
        if (reds.Count == 0)
            return 0;

        Dictionary<double, double> durations = [];
        for (int index = 0; index < reds.Count; index++)
        {
            double start = index == 0 ? 0 : reds[index].Time;
            double next = index == reds.Count - 1 ? endTime : reds[index + 1].Time;
            double duration = Math.Max(0, next - start);
            double beatLength = Math.Round(reds[index].BeatLength * 1000) / 1000;
            if (beatLength <= 0)
                continue;
            durations[beatLength] = durations.GetValueOrDefault(beatLength) + duration;
        }

        if (durations.Count == 0)
            return 0;

        double winner = durations.OrderByDescending(pair => pair.Value).First().Key;
        return winner > 0 ? winner : 0;
    }

    /// <summary>
    /// 分段线性的速度曲线：<c>speed[i]</c> 在 <c>starts[i]</c> 之后生效，
    /// <c>cumulative[i]</c> 是 0 → <c>starts[i]</c> 的「毫秒·倍率」积分。
    /// </summary>
    private sealed class Timeline
    {
        private readonly double[] starts;
        private readonly double[] speeds;
        private readonly double[] cumulative;

        private Timeline(double[] starts, double[] speeds, double[] cumulative)
        {
            this.starts = starts;
            this.speeds = speeds;
            this.cumulative = cumulative;
        }

        public int Count => starts.Length;

        public double StartAt(int index) => starts[index];

        public double SpeedAt(int index) => speeds[index];

        /// <summary>m(t) = SV(t) × BPM(t) / BPM_基准。</summary>
        public static Timeline Build(List<TimingPoint> timing, double baseBeatLength)
        {
            List<(double Time, double Speed)> points = [];
            double beatLength = 0; // 当前红线拍长：绿线只改倍率，不改 BPM
            double sv = 1;

            foreach (TimingPoint point in timing)
            {
                if (point.Uninherited)
                {
                    if (point.BeatLength <= 0)
                        continue;
                    beatLength = point.BeatLength;
                    sv = 1; // 红线会重置绿线倍率
                }
                else
                {
                    sv = point.BeatLength < 0 ? -100.0 / point.BeatLength : 1;
                }

                if (beatLength <= 0)
                    continue;

                // m = SV × BPM/BPM_基准 = SV × 基准拍长 / 当前红线拍长
                double speed = sv * baseBeatLength / beatLength;
                if (!(speed > 0) || double.IsNaN(speed))
                    continue;

                if (points.Count == 0)
                {
                    if (point.Time > 0)
                        points.Add((0, speed));
                    points.Add((point.Time, speed));
                }
                else if (points[^1].Time == point.Time)
                {
                    points[^1] = (point.Time, speed);
                }
                else
                {
                    points.Add((point.Time, speed));
                }
            }

            if (points.Count == 0)
                return new Timeline([], [], []);

            int count = points.Count;
            double[] starts = new double[count];
            double[] speeds = new double[count];
            double[] cumulative = new double[count];
            for (int index = 0; index < count; index++)
            {
                starts[index] = points[index].Time;
                speeds[index] = points[index].Speed;
                if (index > 0)
                {
                    cumulative[index] = cumulative[index - 1] +
                        speeds[index - 1] * (starts[index] - starts[index - 1]);
                }
            }

            return new Timeline(starts, speeds, cumulative);
        }

        /// <summary>P(t)：0 → t 的「毫秒·倍率」积分。</summary>
        public double PositionAt(double time)
        {
            if (time <= starts[0])
                return speeds[0] * Math.Max(0, time);

            int index = IndexAt(time);
            return cumulative[index] + speeds[index] * (time - starts[index]);
        }

        /// <summary>P 的反函数：走完 q 的距离用掉多少毫秒。</summary>
        public double TimeAtPosition(double position)
        {
            if (position <= 0)
                return 0;

            int low = 0;
            int high = Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (cumulative[middle] <= position)
                    low = middle;
                else
                    high = middle - 1;
            }

            return starts[low] + (position - cumulative[low]) / speeds[low];
        }

        /// <summary>反解飞行时长：P(t) − P(t − T) = C。</summary>
        public double VisibleTime(double headTime, double screenTime)
        {
            double position = PositionAt(headTime) - screenTime;
            return headTime - TimeAtPosition(position);
        }

        /// <summary>t 所在的分段下标（t 之前最后一个起点）。</summary>
        public int IndexAt(double time)
        {
            int low = 0;
            int high = Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (starts[middle] <= time)
                    low = middle;
                else
                    high = middle - 1;
            }

            return low;
        }
    }

    // ---- 小工具 ----

    private static int lowerBound(double[] values, double target)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (values[middle] < target)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int upperBound(double[] values, double target)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (values[middle] <= target)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    /// <summary>线性插值分位数；<paramref name="count"/> 限制只看前 count 个（已排序）。</summary>
    private static double percentile(double[] sorted, int count, double fraction)
    {
        if (count <= 0)
            return 0;
        if (count == 1)
            return sorted[0];

        double position = (count - 1) * Math.Clamp(fraction, 0, 1);
        int low = (int)Math.Floor(position);
        int high = (int)Math.Ceiling(position);
        double weight = position - low;
        return sorted[low] * (1 - weight) + sorted[high] * weight;
    }

    private static double percentile(double[] sorted, double fraction) => percentile(sorted, sorted.Length, fraction);
}
