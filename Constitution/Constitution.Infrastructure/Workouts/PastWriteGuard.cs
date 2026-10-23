using Constitution.Domain.Placement;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// Refuses to write a calendar event for a time that has already begun.
///
/// <para>The week's plan has to be made from the week's start on every run (placement numbers a session among its kind in the
/// plan it is handed, so a plan re-made mid-week without the days already gone would renumber, and a key already written
/// would then stand for the wrong day). That keeps the days that are over in the plan, and a session of one of them that was
/// never written (a phone that was off on Monday) would otherwise be written into the past. The refusal comes back as an
/// ordinary failed write, so placement does not record the key as written and nothing is touched on the calendar.</para>
/// </summary>
public sealed class PastWriteGuard(ICalendarWriter inner, Func<DateTimeOffset> clock) : ICalendarWriter
{
    /// <summary>What a refused write says. A plain sentence: nothing of the calendar is in it.</summary>
    public const string Reason = "That session's time has already passed, so no event was made for it.";

    private readonly ICalendarWriter inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Func<DateTimeOffset> clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public Task<CalendarLookup> FindAsync(string person, IReadOnlyList<PlacementKey> keys, CancellationToken cancellationToken) =>
        inner.FindAsync(person, keys, cancellationToken);

    public Task<CalendarCreate> CreateAsync(EventToCreate placed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placed);
        return placed.Start < clock()
            ? Task.FromResult(CalendarCreate.Failed(Reason))
            : inner.CreateAsync(placed, cancellationToken);
    }
}
