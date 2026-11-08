namespace OsuRoam.Services;

/// <summary>
/// Audio samples drive a continuous timeline. Small errors adjust velocity, never position.
/// Sync and Advance use the same monotonic time base in milliseconds.
/// </summary>
public sealed class PreviewClock
{
    public const double StallThreshold = 200;
    public const double SnapThreshold = 200;
    private double sourceTime;
    private double lastArrival;
    private double lastAdvance;
    private bool reset = true;

    public double Time { get; private set; }
    public double SourceTime => sourceTime;
    public bool HasSource { get; private set; }
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
        Rate = now >= lastArrival + StallThreshold
            ? 0 : 1 + Math.Clamp((predicted - Time) / 1000, -0.04, 0.04);
    }
}
