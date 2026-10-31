using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts.Screens;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The calendar side of the deadline step (D111): the writer to look an event up by its key, the narrow editor to remove it by id, and the record of written keys
/// that makes a removal permanent. These are the very ones the meal run placed the cook blocks with, so a key means the same event to both. None holds a token.
/// </summary>
public sealed record MealDeadlineCalendar(ICalendarWriter Writer, IWorkoutEventEditor Editor, IWrittenKeyStore WrittenKeys);

/// <summary>
/// Takes the cook block of a denied evening off its cook's calendar, by the same rules a workout skip follows (<see cref="WorkoutCalendarFollow.FollowSkipAsync"/>): the
/// event is found by its key, and removed only when the calendar still says what placement wrote, because anything else is the owner's last word and the owner's edits
/// win, silently. It only ever looks up and removes; it cannot create or move an event.
///
/// <para>The key stays recorded as written, so a removed event reads as skipped on the next meal run and is never made again. A calendar that cannot be looked into, a
/// record of written keys that cannot be read, a failed edit and a step handed no calendar at all are each a typed "not removed" (<see cref="FollowOutcome.Unavailable"/>,
/// <see cref="FollowOutcome.Failed"/>), never a thrown reason: the evening is already unplanned and the next run tries again until it has passed.</para>
/// </summary>
public static class DeniedEveningCalendar
{
    /// <param name="calendar">The calendar side; null when the step was handed none.</param>
    /// <param name="week">The Sunday the sheet planned.</param>
    /// <param name="dish">The denied cooked evening and where its block was placed.</param>
    /// <param name="people">Whose calendars to look on: the cook on the first run; every adult when a later run no longer knows the cook.</param>
    /// <param name="cookIsKnown">True when <paramref name="people"/> holds exactly the cook, so the key is recorded as written even if the event is gone.</param>
    public static async Task<FollowOutcome> RemoveAsync(
        MealDeadlineCalendar? calendar, DateOnly week, DeniedDish dish, IReadOnlyList<string> people, bool cookIsKnown, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(dish);
        ArgumentNullException.ThrowIfNull(people);
        if (calendar is null || !DeniedEveningWords.TryDay(dish.Evening, out var weekday))
        {
            return FollowOutcome.Unavailable;
        }

        var day = week.AddDays((int)weekday);
        var sawUnavailable = false;
        foreach (var person in people)
        {
            cancellation.ThrowIfCancellationRequested();
            var key = MealPlacement.KeyOf(MealEventRole.Cook, person, week, day);
            var record = calendar.WrittenKeys.Load();
            if (!record.IsReadable)
            {
                sawUnavailable = true;
                continue;
            }

            var lookup = await calendar.Writer.FindAsync(person, [key], cancellation);
            if (!lookup.IsAvailable)
            {
                sawUnavailable = true;
                continue;
            }

            var held = lookup.Held.FirstOrDefault(item => item.Key == key);
            if (held is null && !cookIsKnown)
            {
                continue;
            }

            if (!record.Keys.Contains(key))
            {
                calendar.WrittenKeys.Save(record.Keys.With(key));
            }

            if (held is null)
            {
                return FollowOutcome.NotOnCalendar;
            }

            if (held.Start != dish.Start || held.End != dish.End)
            {
                return FollowOutcome.OwnerHadMovedIt;
            }

            var edit = await calendar.Editor.RemoveAsync(person, held.EventId, cancellation);
            return edit.Succeeded ? FollowOutcome.Removed : FollowOutcome.Failed;
        }

        return sawUnavailable ? FollowOutcome.Unavailable : FollowOutcome.NotOnCalendar;
    }
}
