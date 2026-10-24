using System.Net;
using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts.Screens;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;

namespace Constitution.Infrastructure.Calendar;

/// <summary>
/// The calendar writer over the Google Calendar API v3 on each person's own calendar (its id from <see cref="PlanningSetup"/>), with the full
/// <c>calendar</c> scope the app signs in with. It is the head's one <see cref="ICalendarWriter"/>; <see cref="GoogleWorkoutEventEditor"/> is its edit door.
///
/// <para><b>Find</b> asks Google for events carrying the private extended property <see cref="PlacementKey.Property"/> equal to each key, across all
/// time (an event the owner dragged to another month is still found), and answers <see cref="CalendarLookup.Unavailable"/> whenever it cannot give a
/// complete and trustworthy answer: any failure on any key or page, an event that does not carry the key it was asked for (the filter was ignored, so the
/// whole calendar came back), an event with no usable times. A partial or empty list read as "not there" would be the planner's "the owner deleted it",
/// which is permanent.</para>
///
/// <para><b>Create</b> makes one event on the person's calendar: the clean title, the key in the private property only, no guests, no description, and
/// <c>sendUpdates=none</c> so nobody is told. The calendar's default reminders apply, as for any event the owner makes.</para>
///
/// <para>Times are written as RFC 3339 text with the offset in hours and minutes (<see cref="Rfc3339"/>); the client's own formatting of a
/// <c>DateTimeOffset</c> writes the offset as hours alone. Only the failures <see cref="CalendarErrors"/> names are reported as results; a bug, or a
/// cancellation the caller asked for, propagates with its stack.</para>
/// </summary>
public sealed class GoogleCalendarWriter(
    Func<CancellationToken, Task<CalendarService>> service,
    Func<HouseholdSetupRead> setup,
    TimeZoneInfo zone) : ICalendarWriter
{
    private const int PageSize = 250;
    private const int MostPages = 40;

    public const string OwnNotSet = "This person's calendar is not set.";
    public const string NotTrusted = "The calendar's answer could not be trusted.";
    public const string NoIdGiven = "Google gave no id for the new event.";

    private readonly Func<CancellationToken, Task<CalendarService>> service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly Func<HouseholdSetupRead> setup = setup ?? throw new ArgumentNullException(nameof(setup));
    private readonly TimeZoneInfo zone = zone ?? throw new ArgumentNullException(nameof(zone));

    public async Task<CalendarLookup> FindAsync(string person, IReadOnlyList<PlacementKey> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (CalendarOf(person) is not { } calendarId)
        {
            return CalendarLookup.Unavailable(WhyNoCalendar());
        }

        var held = new List<HeldEvent>();
        try
        {
            if (keys.Count == 0)
            {
                return CalendarLookup.Found(held);
            }

            var calendar = await service(cancellationToken);
            foreach (var key in keys)
            {
                if (!await FindKeyAsync(calendar, calendarId, key, held, cancellationToken))
                {
                    return CalendarLookup.Unavailable(NotTrusted);
                }
            }

            return CalendarLookup.Found(held);
        }
        catch (Exception failure) when (CalendarErrors.TryDescribe(failure, cancellationToken, out var reason))
        {
            return CalendarLookup.Unavailable(reason);
        }
    }

    public async Task<CalendarCreate> CreateAsync(EventToCreate placed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placed);
        if (CalendarOf(placed.Person) is not { } calendarId)
        {
            return CalendarCreate.Failed(WhyNoCalendar());
        }

        try
        {
            var calendar = await service(cancellationToken);
            var body = new Event
            {
                Summary = placed.Title,
                Start = new EventDateTime { DateTimeRaw = Rfc3339.Format(placed.Start) },
                End = new EventDateTime { DateTimeRaw = Rfc3339.Format(placed.End) },
                ExtendedProperties = KeyProperty(placed.Key),
            };
            var request = calendar.Events.Insert(body, calendarId);
            request.SendUpdates = EventsResource.InsertRequest.SendUpdatesEnum.None;
            var made = await request.ExecuteAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(made.Id) ? CalendarCreate.Failed(NoIdGiven) : CalendarCreate.Created(made.Id);
        }
        catch (Exception failure) when (CalendarErrors.TryDescribe(failure, cancellationToken, out var reason))
        {
            return CalendarCreate.Failed(reason);
        }
    }

    // True when the key was answered completely and every event in the answer is trustworthy; false is a doubt.
    private async Task<bool> FindKeyAsync(CalendarService calendar, string calendarId, PlacementKey key, List<HeldEvent> held, CancellationToken cancellationToken)
    {
        string? pageToken = null;
        for (var page = 0; page < MostPages; page++)
        {
            var request = calendar.Events.List(calendarId);
            request.PrivateExtendedProperty = new Google.Apis.Util.Repeatable<string>([$"{PlacementKey.Property}={key.Value}"]);
            request.ShowDeleted = false;
            request.MaxResults = PageSize;
            request.PageToken = pageToken;

            var events = await request.ExecuteAsync(cancellationToken);
            foreach (var googleEvent in events.Items ?? [])
            {
                if (string.Equals(googleEvent.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (HeldFrom(googleEvent, key) is not { } found)
                {
                    return false;
                }

                held.Add(found);
            }

            pageToken = events.NextPageToken;
            if (string.IsNullOrEmpty(pageToken))
            {
                return true;
            }
        }

        return false;
    }

    // The event as the calendar holds it, or null when it is not about the key asked for or has no times: a doubt, not a miss.
    private HeldEvent? HeldFrom(Event googleEvent, PlacementKey key)
    {
        if (string.IsNullOrWhiteSpace(googleEvent.Id)
            || googleEvent.ExtendedProperties?.Private__ is not { } properties
            || !properties.TryGetValue(PlacementKey.Property, out var stored)
            || !string.Equals(stored, key.Value, StringComparison.Ordinal))
        {
            return null;
        }

        if (googleEvent.Start?.Date is { } firstText && DateOnly.TryParseExact(firstText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var first))
        {
            // The owner turned it into an all-day event: it holds that day on the household's clock.
            var endExclusive = googleEvent.End?.Date is { } endText
                && DateOnly.TryParseExact(endText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var end) && end > first
                    ? end
                    : first.AddDays(1);
            return new HeldEvent(googleEvent.Id, key, ZoneClock.StartOfDay(first, zone), ZoneClock.StartOfDay(endExclusive, zone));
        }

        if (googleEvent.Start?.DateTimeDateTimeOffset is not { } start)
        {
            return null;
        }

        var stop = googleEvent.End?.DateTimeDateTimeOffset ?? start;
        return stop < start ? null : new HeldEvent(googleEvent.Id, key, start, stop);
    }

    private static Event.ExtendedPropertiesData KeyProperty(PlacementKey key) =>
        new() { Private__ = new Dictionary<string, string> { [PlacementKey.Property] = key.Value } };

    private string? CalendarOf(string person) =>
        setup() is HouseholdSetupRead.Configured configured ? configured.Setup.CalendarOf(person) : null;

    private string WhyNoCalendar() =>
        setup() is HouseholdSetupRead.NotSet notSet ? notSet.Reason : OwnNotSet;
}
