using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Calendar;

/// <summary>
/// Everything the nightly pass needs from the rest of Pulp, read once. The composition fills it from the
/// catalogue, the sports feed and the household file; the Calendar code asks nothing else of them.
/// <paramref name="HeldByAdult"/> is every adult's own held leagues, an empty set for an adult who
/// holds none (see <see cref="CalendarItems.ForHousehold"/>).
/// </summary>
public sealed record CalendarFeedSnapshot(
    CalendarSources Sources,
    IReadOnlyDictionary<ViewerId, IReadOnlySet<LeagueId>> HeldByAdult,
    Func<ViewerId, string> NameOf,
    HouseholdFileRole Role,
    string? CalendarId,
    string OwnerAccount,
    IReadOnlyList<string> AdultAccounts,
    TimeZoneInfo Zone);

public interface ICalendarFeed
{
    Task<CalendarFeedSnapshot> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Writes the new calendar's id into the household file (the owner's install only).</summary>
    Task SaveCalendarIdAsync(string calendarId, CancellationToken cancellationToken);
}

/// <summary><paramref name="Result"/> is null when this install wrote nothing (it is not the owner's).</summary>
public sealed record CalendarNightlyOutcome(string? CalendarId, CalendarSyncResult? Result);

/// <summary>The daily pass's calendar step: find or make the calendar, then bring it in line (D145, D149).</summary>
public static class CalendarNightly
{
    public static async Task<CalendarNightlyOutcome> RunAsync(
        ICalendarFeed feed, IPulpCalendar calendar, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(calendar);

        var snapshot = await feed.ReadAsync(cancellationToken);
        var id = await CalendarHousehold.EnsureAsync(
            calendar,
            snapshot.Role,
            snapshot.CalendarId,
            snapshot.OwnerAccount,
            snapshot.AdultAccounts,
            created => feed.SaveCalendarIdAsync(created, cancellationToken),
            snapshot.Zone,
            cancellationToken);

        if (id is null || PulpCalendarAccess.Decide(snapshot.Role, id) != CalendarStand.Write)
        {
            return new CalendarNightlyOutcome(id, null);
        }

        var items = CalendarItems.ForHousehold(snapshot.Sources, today, snapshot.Zone, snapshot.HeldByAdult);
        var result = await new CalendarWriter(calendar).SyncAsync(id, items, snapshot.NameOf, today, snapshot.Zone, cancellationToken);
        return new CalendarNightlyOutcome(id, result);
    }
}
