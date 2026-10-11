using Pulp.Domain.Core;

namespace Pulp.Domain.Calendar;

public sealed record CalendarSyncResult(int Created, int Moved, int Deleted, int Left);

/// <summary>
/// One or more operations of a night's write failed after the rest had been tried. <see cref="Done"/> is what
/// did get written; <see cref="AggregateException.InnerExceptions"/> is every failure, in plan order.
/// </summary>
public sealed class CalendarSyncException(IEnumerable<Exception> failures, CalendarSyncResult done)
    : AggregateException($"{failures.Count()} calendar write(s) failed; {done.Created} created, {done.Moved} moved and {done.Deleted} deleted succeeded.", failures)
{
    public CalendarSyncResult Done { get; } = done;
}

/// <summary>Runs the nightly plan against the calendar: reads it, plans, and does exactly what the plan says.</summary>
public sealed class CalendarWriter
{
    /// <summary>
    /// How far ahead the calendar is read: wider than the two weeks written (D245), so a title moved
    /// from beyond the window into it finds its old event instead of making a second.
    /// </summary>
    public const int ReadDaysAhead = 120;

    private readonly IPulpCalendar calendar;

    public CalendarWriter(IPulpCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        this.calendar = calendar;
    }

    public async Task<CalendarSyncResult> SyncAsync(
        string calendarId,
        IReadOnlyList<CalendarItem> items,
        Func<ViewerId, string> nameOf,
        DateOnly today,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();

        var drafts = items.Select(item => CalendarDrafts.From(item, nameOf)).ToList();
        var held = await calendar.ListAsync(calendarId, today, today.AddDays(ReadDaysAhead), zone, cancellationToken);
        var plan = CalendarSyncPlan.Compute(drafts, held, today);

        var created = 0;
        var moved = 0;
        var deleted = 0;
        var left = 0;
        var failures = new List<Exception>();
        foreach (var operation in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Each operation stands alone: one that fails must not stop the ones behind it, above all a
            // delete, which is what takes a spoiler off the shared calendar. The failures are thrown together.
            try
            {
                switch (operation)
                {
                    case CreateEvent create:
                        await calendar.InsertAsync(calendarId, create.Draft, zone, cancellationToken);
                        created++;
                        break;
                    case MoveEvent move:
                        await calendar.PatchAsync(calendarId, move.EventId, move.Draft, zone, cancellationToken);
                        moved++;
                        break;
                    case DeleteEvent delete:
                        await calendar.DeleteAsync(calendarId, delete.EventId, cancellationToken);
                        deleted++;
                        break;
                    default:
                        left++;
                        break;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new CalendarSyncException(failures, new CalendarSyncResult(created, moved, deleted, left));
        }

        return new CalendarSyncResult(created, moved, deleted, left);
    }
}
