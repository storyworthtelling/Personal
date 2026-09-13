using Folio.Domain.Time;

namespace Folio.Domain.Pipeline;

// Sliding-window rate limiter enforcing both a per-minute and a per-day request budget,
// driven by an IClock so pacing is deterministic and testable with a fake clock. One limiter
// instance guards one external provider (Claude or Gemini).
public sealed class RequestRateLimiter
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private readonly int perMinute;
    private readonly int perDay;
    private readonly IClock clock;
    private readonly Queue<DateTimeOffset> grants = new();

    public RequestRateLimiter(int perMinute, int perDay, IClock clock)
    {
        if (perMinute <= 0)
            throw new ArgumentOutOfRangeException(nameof(perMinute), "Per-minute budget must be positive.");
        if (perDay <= 0)
            throw new ArgumentOutOfRangeException(nameof(perDay), "Per-day budget must be positive.");

        this.perMinute = perMinute;
        this.perDay = perDay;
        this.clock = clock;
    }

    // Attempts to consume one request slot. Returns true and records the grant if budget allows.
    public bool TryAcquire()
    {
        var now = clock.UtcNow;
        Purge(now);
        if (CountWithin(now, Minute) < perMinute && grants.Count < perDay)
        {
            grants.Enqueue(now);
            return true;
        }
        return false;
    }

    // How long the caller should wait before a slot is expected to free up. Zero if one is free now.
    public TimeSpan TimeUntilNext()
    {
        var now = clock.UtcNow;
        Purge(now);

        if (CountWithin(now, Minute) < perMinute && grants.Count < perDay)
            return TimeSpan.Zero;

        var wait = TimeSpan.Zero;

        if (CountWithin(now, Minute) >= perMinute)
        {
            var oldestInMinute = grants.Where(g => g > now - Minute).Min();
            wait = Max(wait, oldestInMinute + Minute - now);
        }
        if (grants.Count >= perDay)
        {
            var oldest = grants.Peek(); // FIFO => oldest
            wait = Max(wait, oldest + Day - now);
        }
        return wait;
    }

    private void Purge(DateTimeOffset now)
    {
        while (grants.Count > 0 && grants.Peek() <= now - Day)
            grants.Dequeue();
    }

    private int CountWithin(DateTimeOffset now, TimeSpan window) =>
        grants.Count(g => g > now - window);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
