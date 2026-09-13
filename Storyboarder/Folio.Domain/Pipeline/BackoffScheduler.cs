namespace Folio.Domain.Pipeline;

// Pure exponential-backoff calculator with jitter and an optional server-supplied Retry-After
// floor. The result is persisted as a unit's NextAttemptAtUtc, so backoff survives restarts.
public sealed class BackoffScheduler
{
    private readonly TimeSpan baseDelay;
    private readonly TimeSpan maxDelay;
    private readonly double jitterFactor;
    private readonly Func<double> nextUnit;

    // jitterFactor is the fraction of the (capped) base delay that may be added as jitter, so the
    // returned delay lies in [exp, exp * (1 + jitterFactor)]. nextUnit returns a value in [0, 1).
    public BackoffScheduler(TimeSpan baseDelay, TimeSpan maxDelay, double jitterFactor = 0.25, Func<double>? nextUnit = null)
    {
        if (baseDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(baseDelay), "Base delay must be positive.");
        if (maxDelay < baseDelay)
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "Max delay must be >= base delay.");
        if (jitterFactor < 0)
            throw new ArgumentOutOfRangeException(nameof(jitterFactor), "Jitter factor cannot be negative.");

        this.baseDelay = baseDelay;
        this.maxDelay = maxDelay;
        this.jitterFactor = jitterFactor;
        this.nextUnit = nextUnit ?? Random.Shared.NextDouble;
    }

    // attempt is the zero-based count of attempts already made (0 => first retry uses baseDelay).
    public TimeSpan NextDelay(int attempt, TimeSpan? retryAfter = null)
    {
        if (attempt < 0)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt cannot be negative.");

        // 2^attempt can overflow a double for large attempts; cap the exponent growth at maxDelay.
        var exponential = baseDelay.TotalSeconds * Math.Pow(2, Math.Min(attempt, 30));
        var capped = Math.Min(maxDelay.TotalSeconds, exponential);
        var jitter = capped * jitterFactor * nextUnit();
        var delay = TimeSpan.FromSeconds(capped + jitter);

        if (retryAfter is { } ra && ra > delay)
            return ra;

        return delay;
    }
}
