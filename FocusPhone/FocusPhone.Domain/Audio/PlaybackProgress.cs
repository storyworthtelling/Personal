namespace FocusPhone.Domain.Audio;

/// <summary>
/// The last-known playback position for one media item (podcast episode or music
/// track). Replaces the old binary listened flag as the source of truth: an item
/// counts as finished once playback reaches the end, from which "listened" is
/// derived.
/// </summary>
public record PlaybackProgress(
    string MediaId,
    TimeSpan Position,
    TimeSpan? Duration,
    DateTimeOffset UpdatedUtc)
{
    /// <summary>
    /// Finished when playback is within the final 30 seconds or past 95% of a
    /// known duration — either edge, so both very short and very long items mark
    /// complete a little before the hard end.
    /// </summary>
    public bool IsComplete =>
        Duration is { } duration
        && duration > TimeSpan.Zero
        && (Position >= duration - TimeSpan.FromSeconds(30)
            || Position.TotalSeconds >= duration.TotalSeconds * 0.95);
}

/// <summary>
/// The catch-up rewind applied when resuming an item you stepped away from: a
/// little nudge back so you re-enter mid-thought rather than mid-word. Scales
/// with how long you were away (10s within the hour, 15s beyond).
/// </summary>
public static class SmartRewind
{
    public static TimeSpan Amount(TimeSpan awayFor)
    {
        if (awayFor < TimeSpan.FromMinutes(1))
        {
            return TimeSpan.Zero;
        }

        return awayFor < TimeSpan.FromHours(1)
            ? TimeSpan.FromSeconds(10)
            : TimeSpan.FromSeconds(15);
    }
}
