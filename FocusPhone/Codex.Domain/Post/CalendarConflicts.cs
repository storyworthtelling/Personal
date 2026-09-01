namespace Codex.Domain.Post;

/// <summary>Something already in the day, read from the calendar provider.</summary>
public sealed record Appointment(string Title, DateTime Starts, TimeSpan Duration)
{
    public DateTime Ends => Starts + Duration;
}

/// <summary>Why a proposed date is awkward.</summary>
public enum ConflictKind
{
    /// <summary>It runs into something already there.</summary>
    Overlap,

    /// <summary>It does not overlap, but there is not enough room either side to get there.</summary>
    TightMargin,
}

/// <summary>One reason the owner might want to think again.</summary>
public sealed record CalendarConflict(ConflictKind Kind, Appointment With);

/// <summary>
/// Checks a date found in a message against what is already in the day, before Post
/// proposes it. The date is always <b>proposed</b> — never written silently, and never
/// always-asked — so this is the material the proposal is made with.
///
/// The owner asked for both kinds of clash, and the second is the one a calendar app
/// usually misses: an appointment that ends eight minutes before the next one starts is
/// not a clear day, it is a day you cannot cross town in.
/// </summary>
public static class CalendarConflicts
{
    /// <summary>
    /// How much room either side counts as enough. Travel, parking and the walk in — the
    /// figure is a judgement rather than a measurement, which is why it is named here
    /// instead of being buried in a comparison.
    /// </summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long a proposed event is assumed to run, when the message does not say. Most
    /// things a message announces are about an hour.
    /// </summary>
    public static readonly TimeSpan AssumedDuration = TimeSpan.FromHours(1);

    public static IReadOnlyList<CalendarConflict> Against(
        ProposedEvent proposal, IEnumerable<Appointment> day)
    {
        // An all-day proposal has no time to compare against, so it cannot clash on the
        // clock. Reporting it as clashing with everything that day would make the check
        // useless on exactly the days it fires most.
        if (proposal.StartsAt is not { } startsAt)
        {
            return [];
        }

        var starts = proposal.StartsOn.ToDateTime(startsAt);
        var ends = starts + AssumedDuration;

        return day
            .Select(appointment => Compare(starts, ends, appointment))
            .OfType<CalendarConflict>()
            .ToList();
    }

    private static CalendarConflict? Compare(DateTime starts, DateTime ends, Appointment appointment)
    {
        if (starts < appointment.Ends && appointment.Starts < ends)
        {
            return new CalendarConflict(ConflictKind.Overlap, appointment);
        }

        // A margin is needed on both sides: leaving somewhere is as much of a problem as
        // arriving somewhere.
        var gapBefore = starts - appointment.Ends;
        var gapAfter = appointment.Starts - ends;
        var gap = gapBefore >= TimeSpan.Zero ? gapBefore : gapAfter;

        return gap >= TimeSpan.Zero && gap < Margin
            ? new CalendarConflict(ConflictKind.TightMargin, appointment)
            : null;
    }
}
