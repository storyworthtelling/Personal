using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts.Pipeline;
using Google.Apis.Calendar.v3;

namespace Constitution.Infrastructure.Calendar;

/// <summary>
/// <see cref="IHouseholdCalendarsSource"/> over the Google Calendar API v3, with the account the app already signs in with (its full
/// <c>calendar</c> scope). Reads each adult's own calendar, the family calendar and the children's calendars for the window, with recurring events
/// expanded into their occurrences (<c>singleEvents</c>), page by page, and hands each event through <see cref="GoogleEventMapper"/>, which holds the
/// rules the seam states (the reading run's own events dropped; declined and cancelled events left out, never free).
///
/// <para><b>Whose events are dropped.</b> The workout run and the meal run write to the same calendars, and each must drop only its own earlier
/// events and see the other's as busy time (see <see cref="GoogleEventMapper"/>). <c>ownFamily</c> names the reading run's family, told from each
/// event's key; null drops every keyed event, as the source did before the families existed. A run that forgets to say is blind to the other:
/// the head names it in the one place that builds this source, and a guard test fails if another caller appears.</para>
///
/// <para><b>Fail closed.</b> A calendar that cannot be read is <see cref="CalendarRead.Unreadable"/> with a plain sentence, never an empty list: a
/// Google refusal (not shared yet), a network failure, an event that cannot be understood, a sign-in that gives no token, a calendar id the household has not
/// set. One unreadable children's calendar makes all of them unreadable (their events are merged, and a partial merge is a gap). Only the failures
/// <see cref="CalendarErrors"/> names are described this way; a bug propagates with its stack, and so does a cancellation the caller asked for.</para>
///
/// <para>Which calendars are read comes from <see cref="PlanningSetup"/> (the ids the household gave), and "none set" is told from "there is none":
/// a family or children's calendar the household said it does not have is an empty calendar; one it has not said anything about is unreadable.</para>
/// </summary>
public sealed class GoogleHouseholdCalendars(
    Func<CancellationToken, Task<CalendarService>> service,
    Func<HouseholdSetupRead> setup,
    KeyFamily? ownFamily = null) : IHouseholdCalendarsSource
{
    private const int PageSize = 250;
    private const int MostPages = 40;

    public const string OwnNotSet = "This person's calendar is not set.";
    public const string FamilyNotSet = "The family calendar is not set.";
    public const string ChildrenNotSet = "The children's calendars are not set.";
    public const string TooManyEvents = "A calendar has more events than can be read at once.";

    private readonly Func<CancellationToken, Task<CalendarService>> service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly Func<HouseholdSetupRead> setup = setup ?? throw new ArgumentNullException(nameof(setup));

    private readonly KeyFamily? ownFamily = ownFamily == KeyFamily.Unknown
        ? throw new ArgumentOutOfRangeException(nameof(ownFamily), ownFamily, "No run owns the unknown family, and dropping its events would drop somebody's time.")
        : ownFamily;

    public async Task<HouseholdCalendarSnapshot> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        if (setup() is not HouseholdSetupRead.Configured configured)
        {
            var reason = ((HouseholdSetupRead.NotSet)setup()).Reason;
            var nothing = CalendarRead.Unreadable(reason);
            return new HouseholdCalendarSnapshot(new Dictionary<string, CalendarRead>(), nothing, nothing);
        }

        var household = configured.Setup;
        CalendarService calendar;
        try
        {
            calendar = await service(cancellation);
        }
        catch (Exception failure) when (CalendarErrors.TryDescribe(failure, cancellation, out var signInReason))
        {
            // The sentence is the reason; the type of the failure is the head's to put in its trail.
            var unreadable = CalendarRead.Unreadable(signInReason);
            return new HouseholdCalendarSnapshot(
                household.Adults.ToDictionary(adult => adult.Name, _ => unreadable, StringComparer.OrdinalIgnoreCase), unreadable, unreadable);
        }

        var own = new Dictionary<string, CalendarRead>(StringComparer.OrdinalIgnoreCase);
        foreach (var adult in household.Adults)
        {
            own[adult.Name] = household.CalendarOf(adult.Name) is { } id
                ? await ReadOneAsync(calendar, id, from, to, cancellation)
                : CalendarRead.Unreadable(OwnNotSet);
        }

        var family = household.NoFamilyCalendar
            ? CalendarRead.Of([])
            : string.IsNullOrWhiteSpace(household.FamilyCalendarId)
                ? CalendarRead.Unreadable(FamilyNotSet)
                : await ReadOneAsync(calendar, household.FamilyCalendarId.Trim(), from, to, cancellation);

        return new HouseholdCalendarSnapshot(own, family, await ReadChildrenAsync(calendar, household, from, to, cancellation));
    }

    private async Task<CalendarRead> ReadChildrenAsync(CalendarService calendar, PlanningSetup household, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        if (household.NoChildren)
        {
            return CalendarRead.Of([]);
        }

        if (household.ChildrenCalendarIds.Count == 0)
        {
            return CalendarRead.Unreadable(ChildrenNotSet);
        }

        var merged = new List<CalendarEntry>();
        foreach (var id in household.ChildrenCalendarIds)
        {
            var read = await ReadOneAsync(calendar, id, from, to, cancellation);
            if (!read.IsReadable)
            {
                return read;
            }

            merged.AddRange(read.Entries);
        }

        return CalendarRead.Of(merged);
    }

    private async Task<CalendarRead> ReadOneAsync(CalendarService calendar, string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
    {
        var entries = new List<CalendarEntry>();
        string? pageToken = null;
        try
        {
            for (var page = 0; page < MostPages; page++)
            {
                var request = calendar.Events.List(calendarId);
#pragma warning disable CS0618 // The DateTime properties are marked obsolete only to steer toward DateTimeOffset, whose offset the client writes as hours alone (-05).
                request.TimeMin = from.UtcDateTime;
                request.TimeMax = to.UtcDateTime;
#pragma warning restore CS0618
                request.SingleEvents = true;
                request.ShowDeleted = false;
                request.MaxResults = PageSize;
                request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
                request.PageToken = pageToken;

                var events = await request.ExecuteAsync(cancellation);
                foreach (var googleEvent in events.Items ?? [])
                {
                    switch (GoogleEventMapper.Map(googleEvent, calendarId, ownFamily))
                    {
                        case EventMapping.Kept kept:
                            entries.Add(kept.Entry);
                            break;
                        case EventMapping.Unusable unusable:
                            return CalendarRead.Unreadable(unusable.Reason);
                    }
                }

                pageToken = events.NextPageToken;
                if (string.IsNullOrEmpty(pageToken))
                {
                    return CalendarRead.Of(entries);
                }
            }

            return CalendarRead.Unreadable(TooManyEvents);
        }
        catch (Exception failure) when (CalendarErrors.TryDescribe(failure, cancellation, out var reason))
        {
            return CalendarRead.Unreadable(reason);
        }
    }
}
