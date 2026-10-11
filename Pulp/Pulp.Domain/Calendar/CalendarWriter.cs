using Pulp.Domain.Core;

namespace Pulp.Domain.Calendar;

public sealed record CalendarSyncResult(int Created, int Moved, int Deleted, int Left);

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
        foreach (var operation in plan)
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

        return new CalendarSyncResult(created, moved, deleted, left);
    }
}
