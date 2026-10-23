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
/// </summary>
public sealed class WorkoutCalendarFollow(ICalendarWriter writer, IWorkoutEventEditor editor, IWrittenKeyStore store)
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

        var oldKeys = swap.Moves.Select(move => WorkoutKeys.In(swap.Before, swap.Person, move.Old)).ToList();
        var newKeys = swap.Moves.Select(move => WorkoutKeys.In(swap.After, swap.Person, move.New)).ToList();
        var lookup = await writer.FindAsync(swap.Person, oldKeys, cancellationToken);
        if (!lookup.IsAvailable)
        {
            return Fill(outcomes, FollowOutcome.Unavailable);
        }

        var written = record.Keys;
        for (var index = 0; index < swap.Moves.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                var edit = await editor.MoveAsync(swap.Person, held.EventId, newKeys[index], move.New.Start, move.New.End, cancellationToken);
                outcomes[index] = edit.Succeeded ? FollowOutcome.Moved : FollowOutcome.Failed;
                if (edit.Succeeded)
                {
                    written = written.With(newKeys[index]);
                }
            }
        }

        if (written.Keys.Count != record.Keys.Keys.Count)
        {
            store.Save(written);
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
