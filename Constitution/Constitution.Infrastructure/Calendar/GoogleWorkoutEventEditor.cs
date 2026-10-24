using System.Net;
using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts.Screens;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;

namespace Constitution.Infrastructure.Calendar;

/// <summary>
/// The narrow edit door over the Google Calendar API v3: it names an event by the id the writer's find returned, and changes only the event's
/// times and its private <see cref="PlacementKey.Property"/>. It never touches a title, a description or guests, and every call is
/// <c>sendUpdates=none</c> so nobody is told. Removing an event that is already gone is done. Only the failures
/// <see cref="CalendarErrors"/> names are reported as results; a bug, or a cancellation the caller asked for, propagates with its stack.
/// </summary>
public sealed class GoogleWorkoutEventEditor(
    Func<CancellationToken, Task<CalendarService>> service,
    Func<HouseholdSetupRead> setup) : IWorkoutEventEditor
{
    private readonly Func<CancellationToken, Task<CalendarService>> service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly Func<HouseholdSetupRead> setup = setup ?? throw new ArgumentNullException(nameof(setup));

    public async Task<EventEdit> MoveAsync(string person, string eventId, PlacementKey newKey, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        if (CalendarOf(person) is not { } calendarId)
        {
            return EventEdit.Failed(WhyNoCalendar());
        }

        try
        {
            var calendar = await service(cancellationToken);
            var body = new Event
            {
                Start = new EventDateTime { DateTimeRaw = Rfc3339.Format(start) },
                End = new EventDateTime { DateTimeRaw = Rfc3339.Format(end) },
                ExtendedProperties = KeyProperty(newKey),
            };
            var request = calendar.Events.Patch(body, calendarId, eventId.Trim());
            request.SendUpdates = EventsResource.PatchRequest.SendUpdatesEnum.None;
            await request.ExecuteAsync(cancellationToken);
            return EventEdit.Done();
        }
        catch (Exception failure) when (CalendarErrors.TryDescribe(failure, cancellationToken, out var reason))
        {
            return EventEdit.Failed(reason);
        }
    }

    public async Task<EventEdit> RemoveAsync(string person, string eventId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        if (CalendarOf(person) is not { } calendarId)
        {
            return EventEdit.Failed(WhyNoCalendar());
        }

        try
        {
            var calendar = await service(cancellationToken);
            var request = calendar.Events.Delete(calendarId, eventId.Trim());
            request.SendUpdates = EventsResource.DeleteRequest.SendUpdatesEnum.None;
            await request.ExecuteAsync(cancellationToken);
            return EventEdit.Done();
        }
        catch (GoogleApiException gone) when (gone.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // The event was found on this calendar a moment ago, so "not found" or "gone" means it is already as the owner wants it.
            return EventEdit.Done();
        }
        catch (Exception failure) when (CalendarErrors.TryDescribe(failure, cancellationToken, out var reason))
        {
            return EventEdit.Failed(reason);
        }
    }

    private string? CalendarOf(string person) =>
        setup() is HouseholdSetupRead.Configured configured ? configured.Setup.CalendarOf(person) : null;

    private string WhyNoCalendar() =>
        setup() is HouseholdSetupRead.NotSet notSet ? notSet.Reason : GoogleCalendarWriter.OwnNotSet;

    private static Event.ExtendedPropertiesData KeyProperty(PlacementKey key) =>
        new() { Private__ = new Dictionary<string, string> { [PlacementKey.Property] = key.Value } };
}
