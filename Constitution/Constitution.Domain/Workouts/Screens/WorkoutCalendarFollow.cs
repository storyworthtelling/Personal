using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Screens;

public sealed class EventEdit
{
    private EventEdit(string? failedBecause) => FailedBecause = failedBecause;

    public string? FailedBecause { get; }

    public bool Succeeded => FailedBecause is null;

    public static EventEdit Done() => new(null);

    public static EventEdit Failed(string reason) => new(reason);
}

/// <summary>
/// The narrow door a skip or a swap uses to change an event it has already found by its key. It names an event by id, so it
/// cannot reach any event this app did not find; the placement writer stays find-and-create only.
/// </summary>
public interface IWorkoutEventEditor
{
    Task<EventEdit> MoveAsync(string person, string eventId, PlacementKey newKey, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken);

    Task<EventEdit> RemoveAsync(string person, string eventId, CancellationToken cancellationToken);
}

public enum FollowOutcome
{
    Moved,
    Removed,
    OwnerHadMovedIt,
    NotOnCalendar,
    Unavailable,
    Failed,
}

/// <summary>
/// Makes the calendar follow a swap or a skip without touching an event the owner has moved. Events are found by their key
/// (the placement writer's own <c>FindAsync</c>); an event is changed only when the calendar still says what placement last
/// wrote there, because anything else is the owner's last word and the owner's edits win. A calendar that cannot be looked
/// into, or a record of written keys that cannot be read, is a reason to do nothing, never a reason to guess.
///
/// <para><b>The keys are the numbers the weekly run issued</b> (B2). With the record of issued numbers (<paramref name="numbers"/>) the
/// follow reads it as it stands when it acts, since a run may have issued more since the tab loaded the plan, and a moved event wears
/// the key of its new place: the number issued to that weekday's session of its kind, or the next above the highest ever issued when
/// the place has none. Those new numbers are kept as the events take them, so the run that comes next finds them issued. An event is
/// not moved onto a key another event still wears (a dropped session's event the owner has not deleted): a key names one event.</para>
/// </summary>
public sealed class WorkoutCalendarFollow(ICalendarWriter writer, IWorkoutEventEditor editor, IWrittenKeyStore store, IKeyAssignmentStore? numbers = null)
{
    public async Task<IReadOnlyList<FollowOutcome>> FollowSwapAsync(SwapResult swap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(swap);
        var outcomes = new FollowOutcome[swap.Moves.Count];
        var record = store.Load();
        if (!record.IsReadable)
        {
            return Fill(outcomes, FollowOutcome.Unavailable);
        }

        var issued = swap.Assignments;
        if (numbers is not null)
        {
            var read = numbers.Load();
            if (!read.IsReadable)
            {
                return Fill(outcomes, FollowOutcome.Unavailable);
            }

            issued = read.Assignments;
        }

        var oldKeys = swap.Moves.Select(move => WorkoutKeys.In(swap.Before, swap.Person, move.Old, issued)).ToList();
        var newIssues = swap.Moves.Select(move => WorkoutKeys.IssueIn(swap.After, swap.Person, move.New, issued)).ToList();
        var newKeys = newIssues.Select(issue => issue.Key).ToList();
        var lookup = await writer.FindAsync(swap.Person, oldKeys.Concat(newKeys).Distinct().ToList(), cancellationToken);
        if (!lookup.IsAvailable)
        {
            return Fill(outcomes, FollowOutcome.Unavailable);
        }

        // Who will move: the event is there and the calendar still says what placement wrote. Decided first for the whole swap, so
        // an event is judged against the keys that will have been given up, not the ones still worn by an event that stays put.
        var eligible = new HeldEvent?[swap.Moves.Count];
        for (var index = 0; index < swap.Moves.Count; index++)
        {
            var move = swap.Moves[index];
            var held = lookup.Held.FirstOrDefault(item => item.Key == oldKeys[index]);
            if (held is null || move.OldPlaced is null)
            {
                outcomes[index] = FollowOutcome.NotOnCalendar;
            }
            else if (held.Start != move.OldPlaced.Start || held.End != move.OldPlaced.End)
            {
                outcomes[index] = FollowOutcome.OwnerHadMovedIt;
            }
            else
            {
                eligible[index] = held;
            }
        }

        var leaving = eligible.OfType<HeldEvent>().Select(item => item.EventId).ToHashSet(StringComparer.Ordinal);
        var written = record.Keys;
        var kept = new List<KeyIssue>();
        try
        {
            for (var index = 0; index < swap.Moves.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (eligible[index] is not { } held)
                {
                    continue;
                }

                if (lookup.Held.Any(item => item.Key == newKeys[index] && !leaving.Contains(item.EventId)))
                {
                    outcomes[index] = FollowOutcome.Failed;
                    continue;
                }

                var move = swap.Moves[index];
                var edit = await editor.MoveAsync(swap.Person, held.EventId, newKeys[index], move.New.Start, move.New.End, cancellationToken);
                outcomes[index] = edit.Succeeded ? FollowOutcome.Moved : FollowOutcome.Failed;
                if (edit.Succeeded)
                {
                    written = written.With(newKeys[index]);
                    kept.Add(newIssues[index]);
                }
            }
        }
        finally
        {
            // What the events now wear is saved even if a later edit threw: the number first, so the key is never written without it.
            if (numbers is not null && kept.Count > 0)
            {
                numbers.Save((issued ?? KeyAssignments.Empty).With(kept));
            }

            if (written.Keys.Count != record.Keys.Keys.Count)
            {
                store.Save(written);
            }
        }

        return outcomes;
    }

    public async Task<FollowOutcome> FollowSkipAsync(PendingSkip skip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skip);
        if (skip.Key is not { } key || skip.OnCalendar is not { } placed)
        {
            return FollowOutcome.NotOnCalendar;
        }

        var record = store.Load();
        if (!record.IsReadable)
        {
            return FollowOutcome.Unavailable;
        }

        var lookup = await writer.FindAsync(skip.Person, [key], cancellationToken);
        if (!lookup.IsAvailable)
        {
            return FollowOutcome.Unavailable;
        }

        // The key stays recorded as written, so a removed event reads as skipped on the next run and is never made again.
        if (!record.Keys.Contains(key))
        {
            store.Save(record.Keys.With(key));
        }

        var held = lookup.Held.FirstOrDefault(item => item.Key == key);
        if (held is null)
        {
            return FollowOutcome.NotOnCalendar;
        }

        if (held.Start != placed.Start || held.End != placed.End)
        {
            return FollowOutcome.OwnerHadMovedIt;
        }

        var edit = await editor.RemoveAsync(skip.Person, held.EventId, cancellationToken);
        return edit.Succeeded ? FollowOutcome.Removed : FollowOutcome.Failed;
    }

    private static FollowOutcome[] Fill(FollowOutcome[] outcomes, FollowOutcome with)
    {
        Array.Fill(outcomes, with);
        return outcomes;
    }
}
