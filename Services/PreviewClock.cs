namespace OsuRoam.Services;

/// <summary>
/// Audio samples drive a continuous timeline. Small errors adjust velocity, never position.
/// Sync and Advance use the same monotonic time base in milliseconds.
/// </summary>
public sealed class PreviewClock
{
    // 200ms 太紧：tosu 的快照间隔本来就在 60~200ms 之间浮动，一超阈值时钟就「冻结」，
    // 下一个样本再来又跳一步 —— 那就是周期性抖动的来源。放到 600ms 只挡真正的断流。
    public const double StallThreshold = 600;
    public const double SnapThreshold = 200;
    private double sourceTime;
    private double lastArrival;
    private double lastAdvance;
    private bool reset = true;

    public double Time { get; private set; }
    public double SourceTime => sourceTime;
    public bool HasSource { get; private set; }

    private readonly List<double> sampleIntervals = [];
    private double lastSyncAt = double.NaN;

    /// <summary>记录一条样本的到达间隔，并返回最近 120 条的 p50/p95（没有就返回 null）。</summary>
    public string? DescribeSamples()
    {
        if (sampleIntervals.Count < 120)
            return null;
        List<double> copy = [.. sampleIntervals];
        sampleIntervals.Clear();
        copy.Sort();
        return $"样本间隔 p50={copy[copy.Count / 2]:0}ms p95={copy[(int)(copy.Count * 0.95)]:0}ms " +
               $"{copy.Count} 条｜冻结 {FreezeCount} 次";
    }

    /// <summary>进入冻结（断流）的次数。</summary>
    public int FreezeCount { get; private set; }
    public double Rate { get; private set; }
    public int Discontinuity { get; private set; }

    // The compositor stops at this deadline even if the application thread is delayed.
    public double PredictionRemaining(double now) => HasSource
        ? Math.Clamp(lastArrival + StallThreshold - now, 0, StallThreshold) : 0;

    public void Reset()
    {
        reset = true;
        HasSource = false;
        Rate = 0;
    }

    public void Sync(double milliseconds, double now)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0 || !double.IsFinite(now))
            return;
        // Repeated packets while audio is paused must not extend the prediction deadline.
        if (!reset && HasSource && milliseconds == sourceTime)
            return;
        if (double.IsFinite(lastSyncAt))
        {
            double interval = now - lastSyncAt;
            if (interval > 0 && interval < 5000)
                sampleIntervals.Add(interval);
        }
        lastSyncAt = now;

        sourceTime = milliseconds;
        lastArrival = now;
        HasSource = true;
        if (reset)
        {
            Time = milliseconds;
            lastAdvance = now;
            Rate = 1;
            reset = false;
            Discontinuity++;
        }
    }

    public void Advance(double now, double delta)
    {
        if (!double.IsFinite(now) || !double.IsFinite(delta) || !HasSource)
            return;
        double until = Math.Min(now, lastArrival + StallThreshold);
        Time += Math.Max(0, until - lastAdvance) * Rate;
        lastAdvance = now;
        double predicted = sourceTime + Math.Clamp(now - lastArrival, 0, StallThreshold);
        if (Math.Abs(predicted - Time) > SnapThreshold)
        {
            Time = predicted;
            Discontinuity++;
        }
        // At most +/-4% correction; independent of refresh rate and maintenance cadence.
        bool frozen = now >= lastArrival + StallThreshold;
        if (frozen)
            FreezeCount++;
        Rate = frozen
            ? 0 : 1 + Math.Clamp((predicted - Time) / 1000, -0.04, 0.04);
    }
}
