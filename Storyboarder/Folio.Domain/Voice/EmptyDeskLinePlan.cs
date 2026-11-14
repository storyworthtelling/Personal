namespace Folio.Domain.Voice;

/// <summary>
/// Whether the empty desk's line in the owner's voice needs writing again: once a day, and then it
/// stays. The desk refreshes every few seconds, so composing on each refresh rewrote the line under
/// the owner's eyes and billed a model call each time.
/// </summary>
public static class EmptyDeskLinePlan
{
    /// <summary>How long an attempt that came back empty (no voice yet) holds off the next (efficiency audit §6).</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// True when there is no stored line, or it was written on a day other than today (local time), and the
    /// last attempt was not within the hour. A line with no recorded day cannot be known to be today's, so it
    /// is written once.
    /// </summary>
    public static bool NeedsComposing(string? storedLine, DateOnly? storedDay, DateTimeOffset? lastAskedAt, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (!string.IsNullOrWhiteSpace(storedLine) && storedDay == today)
        {
            return false;
        }
        return lastAskedAt is not { } asked || now - asked >= RetryAfter || now < asked;
    }
}
