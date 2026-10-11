namespace Pulp.Domain.Calendar;

/// <summary>
/// Where Pulp writes its key on an event it made (<c>extendedProperties.private</c>). An event without
/// it was made by hand and is never touched (D149's counterpart: Pulp moves what it made, nothing else).
/// </summary>
public static class PulpEventKey
{
    public const string Property = "pulpKey";
}

/// <summary>
/// One event as Pulp would write it. <paramref name="Key"/> is the item's key, which never contains the
/// date, so a changed date is a change to this event, not a different one (D149). A time of day means a
/// timed event of <paramref name="Length"/>; none means all-day (D148).
/// </summary>
public sealed record CalendarEventDraft(string Key, string Title, string? Description, CalendarWhen When, TimeSpan Length);

/// <summary>An event the calendar holds. <paramref name="Key"/> is null when Pulp did not make it.</summary>
public sealed record ExistingEvent(string EventId, string? Key, string Title, string? Description, CalendarWhen When);

public abstract record CalendarOperation;

public sealed record CreateEvent(CalendarEventDraft Draft) : CalendarOperation;

/// <summary>An in-place patch of an event Pulp made, sent without telling anyone (D149).</summary>
public sealed record MoveEvent(string EventId, CalendarEventDraft Draft) : CalendarOperation;

public sealed record DeleteEvent(string EventId, string Key) : CalendarOperation;

public sealed record LeaveEvent(string EventId, string Key) : CalendarOperation;

/// <summary>
/// What the nightly pass does to the Pulp calendar: compare what should be on it with what is, event
/// by key. Pure, so the two promises that matter are testable without a calendar: a changed date moves
/// the existing event (never delete-and-create, which would announce itself again) and an event
/// without Pulp's key is not so much as read as a candidate.
/// </summary>
public static class CalendarSyncPlan
{
    public static IReadOnlyList<CalendarOperation> Compute(
        IReadOnlyList<CalendarEventDraft> desired,
        IReadOnlyList<ExistingEvent> existing,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(existing);

        var duplicate = desired.GroupBy(draft => draft.Key, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"The calendar was asked to hold '{duplicate.Key}' twice; a key names one event.", nameof(desired));
        }

        var madeByPulp = existing
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.EventId, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        // Deleting comes first: a delete is what takes a spoiler off the shared calendar, so no failing
        // move or create may stand in front of it. Moves come before creates for the same reason (a create
        // announces itself; a move was already there). Leaves are bookkeeping and go last.
        var deletes = new List<CalendarOperation>();
        var moves = new List<CalendarOperation>();
        var creates = new List<CalendarOperation>();
        var leaves = new List<CalendarOperation>();
        foreach (var draft in desired)
        {
            if (!madeByPulp.TryGetValue(draft.Key, out var held))
            {
                creates.Add(new CreateEvent(draft));
                continue;
            }

            if (Same(draft, held[0]))
            {
                leaves.Add(new LeaveEvent(held[0].EventId, draft.Key));
            }
            else
            {
                moves.Add(new MoveEvent(held[0].EventId, draft));
            }

            deletes.AddRange(held.Skip(1).Select(extra => new DeleteEvent(extra.EventId, draft.Key)));
        }

        var wanted = desired.Select(draft => draft.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, held) in madeByPulp.Where(pair => !wanted.Contains(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var item in held)
            {
                // A day already gone is history, not a title that left the plan.
                if (item.When.Date >= today)
                {
                    deletes.Add(new DeleteEvent(item.EventId, key));
                }
                else
                {
                    leaves.Add(new LeaveEvent(item.EventId, key));
                }
            }
        }

        var operations = new List<CalendarOperation>(deletes.Count + moves.Count + creates.Count + leaves.Count);
        operations.AddRange(deletes);
        operations.AddRange(moves);
        operations.AddRange(creates);
        operations.AddRange(leaves);
        return operations;
    }

    private static bool Same(CalendarEventDraft draft, ExistingEvent held) =>
        SameWhen(draft.When, held.When)
        && string.Equals(draft.Title, held.Title, StringComparison.Ordinal)
        && string.Equals(draft.Description ?? string.Empty, held.Description ?? string.Empty, StringComparison.Ordinal);

    /// <summary>The calendar reads back whole seconds, so a feed time is compared at that precision.</summary>
    private static bool SameWhen(CalendarWhen wanted, CalendarWhen held) =>
        wanted.Date == held.Date && Seconds(wanted.Time) == Seconds(held.Time);

    private static TimeOnly? Seconds(TimeOnly? time) =>
        time is { } value ? new TimeOnly(value.Hour, value.Minute, value.Second) : null;
}
