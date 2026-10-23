using Constitution.Domain.Voice;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Voice;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The encouragement seat over the shared seat cache: composed at most once per facts and day, the plain sentence when the
/// voice is missing, and nothing when the load is not up on last time (no news is no request, and nobody is asked).
/// </summary>
public sealed class SeatCacheEncouragement(SeatCache cache) : IWorkoutEncouragement
{
    private readonly SeatCache cache = cache ?? throw new ArgumentNullException(nameof(cache));

    public async Task<string?> LineAsync(ProgressionFacts news, DateTimeOffset now, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(news);
        if (SeatFacts.WorkoutEncouragement(news) is not { } request)
        {
            return null;
        }

        var line = await cache.ComposeAsync(request, now, cancellation);
        return line.Text;
    }
}
