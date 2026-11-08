namespace OsuRoam.Services;

/// <summary>Continuous compositor trajectory, evaluated in source milliseconds and QPC seconds.</summary>
public sealed class PreviewMotion
{
    private double epoch, position, velocity, acceleration, blend, duration;
    private bool initialized;
    public double Position { get; private set; }
    public double Velocity { get; private set; }
    public double Acceleration { get; private set; }
    public double Blend { get; private set; }
    public double EndVelocity { get; private set; }

    public void Update(double now, double target, double rate, double remaining, bool seek)
    {
        double elapsed = Math.Clamp(now - epoch, 0, duration);
        double curved = Math.Min(elapsed, blend);
        double current = position + velocity * curved + acceleration * curved * curved;
        double currentVelocity = velocity + 2 * acceleration * curved;
        current += currentVelocity * (elapsed - curved);
        if (elapsed >= duration) currentVelocity = 0;
        if (!initialized || seek)
        {
            current = target;
            currentVelocity = rate * 1000;
        }
        // Correct phase through velocity only. Position and velocity stay continuous across commits,
        // including commits delayed by tile painting or application scheduling.
        double desired = rate <= 0 ? 0 : Math.Clamp(rate + (target - current) / 500, 0.94, 1.06) * 1000;
        duration = Math.Max(0, remaining / 1000);
        blend = Math.Min(0.1, duration);
        epoch = now;
        position = Position = current;
        velocity = Velocity = currentVelocity;
        acceleration = Acceleration = blend > 0 ? (desired - velocity) / (2 * blend) : 0;
        Blend = blend;
        EndVelocity = blend > 0 ? desired : 0;
        initialized = true;
    }
}
