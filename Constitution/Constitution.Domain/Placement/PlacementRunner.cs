namespace Constitution.Domain.Placement;

/// <summary>A new event the calendar would not take, and the reason it gave.</summary>
public sealed record CreateFailure(EventToCreate Event, string Reason);

/// <summary>
/// What one run did. <see cref="Plan"/> is every event with what became of it (null only when the run was
/// blocked before planning); <see cref="Created"/> is what was actually written; <see cref="Failed"/> is what the
/// calendar refused; <see cref="BlockedBecause"/> says why nothing was done when the record could not be read.
/// </summary>
public sealed record PlacementRun(
    PlacementPlan? Plan,
    IReadOnlyList<EventToCreate> Created,
    IReadOnlyList<CreateFailure> Failed,
    string? BlockedBecause);

/// <summary>
/// The part that touches a calendar: reads the record, looks our events up by key, plans with
/// <see cref="PlacementEngine"/> and writes what is new, in that order and only that.
///
/// <list type="bullet">
///   <item><b>Nothing is written on a guess.</b> A record that cannot be read blocks the run before any
///   calendar is touched, and is left exactly as it was. A calendar that cannot be looked into gets nothing
///   written and nothing concluded.</item>
///   <item><b>A failed write is not a delete.</b> A key is recorded as written only after the calendar accepted
///   the event, so a refused write is tried again next run rather than read as the owner's delete.</item>
///   <item><b>The record heals.</b> An event found on a calendar under our key is recorded as written even if
///   the record did not know it (a run that died between writing and saving), so a later delete is still a skip.</item>
///   <item><b>A re-run on an unchanged week writes nothing</b> (R8): every event is then held, and a held event is
///   never touched.</item>
///   <item><b>A session keeps its key</b> (B2). Given a store of issued numbers (<see cref="IKeyAssignmentStore"/>) the run plans by
///   them, and keeps the numbers the plan issued <i>before</i> it writes anything, so an event on the calendar always has a number
///   in the record. A record that cannot be read blocks the run exactly as an unreadable record of written events does.</item>
/// </list>
/// </summary>
public sealed class PlacementRunner
{
    private readonly ICalendarWriter writer;
    private readonly IWrittenKeyStore store;
    private readonly IKeyAssignmentStore? issuedNumbers;

    /// <param name="writer">The calendar, found in and written to.</param>
    /// <param name="store">The record of keys written.</param>
    /// <param name="assignments">The record of workout numbers issued; null plans by whatever record the request itself carries (none, usually).</param>
    public PlacementRunner(ICalendarWriter writer, IWrittenKeyStore store, IKeyAssignmentStore? assignments = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(store);
        this.writer = writer;
        this.store = store;
        issuedNumbers = assignments;
    }

    public async Task<PlacementRun> RunAsync(PlacementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var record = store.Load();
        if (!record.IsReadable)
        {
            return new PlacementRun(null, [], [], $"The record of written events could not be read, so nothing was written: {record.UnreadableBecause}");
        }

        KeyAssignments? issued = null;
        if (issuedNumbers is not null)
        {
            var numbers = issuedNumbers.Load();
            if (!numbers.IsReadable)
            {
                return new PlacementRun(null, [], [], $"The record of issued workout numbers could not be read, so nothing was written: {numbers.UnreadableBecause}");
            }

            issued = numbers.Assignments;
            request = request with { Assignments = issued };
        }

        // Asked before any calendar is touched, so a request that makes no sense is refused with nothing done.
        var wanted = PlacementEngine.Wanted(request);

        var found = new List<HeldEvent>();
        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var person in wanted.GroupBy(item => item.Person, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lookup = await writer.FindAsync(person.Key, person.Select(item => item.Key).ToList(), cancellationToken);
            if (lookup.IsAvailable)
            {
                found.AddRange(lookup.Held);
            }
            else
            {
                unavailable.Add(person.Key);
            }
        }

        var written = record.Keys.With(found.Select(item => item.Key));
        var plan = PlacementEngine.Plan(request, new CalendarState(written, found, unavailable));

        // Before the first write, so that whatever is written afterwards has a number in the record, whatever happens next.
        if (issuedNumbers is not null && plan.Issued.Count > 0)
        {
            issuedNumbers.Save(issued!.With(plan.Issued));
        }

        var created = new List<EventToCreate>();
        var failed = new List<CreateFailure>();
        try
        {
            foreach (var make in plan.ToCreate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await writer.CreateAsync(make, cancellationToken);
                if (result.Succeeded)
                {
                    created.Add(make);
                    written = written.With(make.Key);
                }
                else
                {
                    failed.Add(new CreateFailure(make, result.FailedBecause!));
                }
            }
        }
        finally
        {
            // Saved even if a write threw, so what did get written is not forgotten; saved only if it grew.
            if (written.Keys.Count != record.Keys.Keys.Count)
            {
                store.Save(written);
            }
        }

        return new PlacementRun(plan, created, failed, null);
    }
}
