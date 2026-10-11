namespace Pulp.Domain.Sports;

/// <summary>
/// The days a schedule is asked for: <paramref name="DaysBack"/> before <paramref name="Today"/> (the
/// games whose recordings may still be waiting, D58) and <paramref name="DaysAhead"/> after it (D245:
/// two weeks ahead). The clock is an argument.
/// </summary>
public readonly record struct SportsWindow
{
    /// <summary>How long a service keeps a recording when Pulp cannot know (D58: asks "still want this?" after two weeks).</summary>
    public const int RecordingDays = 14;

    public const int AheadDays = 14;

    public SportsWindow(DateOnly today, int daysBack, int daysAhead)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(daysBack);
        ArgumentOutOfRangeException.ThrowIfNegative(daysAhead);
        Today = today;
        DaysBack = daysBack;
        DaysAhead = daysAhead;
    }

    public DateOnly Today { get; }

    public int DaysBack { get; }

    public int DaysAhead { get; }

    public DateOnly From => Today.AddDays(-DaysBack);

    public DateOnly To => Today.AddDays(DaysAhead);

    public static SportsWindow Around(DateOnly today) => new(today, RecordingDays, AheadDays);

    /// <summary>Every day in the window, first to last.</summary>
    public IEnumerable<DateOnly> Days()
    {
        for (var day = From; day <= To; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
