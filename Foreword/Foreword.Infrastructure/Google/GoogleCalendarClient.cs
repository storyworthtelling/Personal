using global::Google.Apis.Calendar.v3;
using global::Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Options;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;
using GoogleCalendar = global::Google.Apis.Calendar.v3.Data.Calendar;

namespace Foreword.Infrastructure.Google;

// Reads events from every calendar in the account's calendar list and creates events/calendars.
// Column assignment is left to CalendarColumnMapper; this client only fills SourceCalendarId.
public class GoogleCalendarClient : IGoogleCalendarClient
{
    private readonly IGoogleClientFactory factory;
    private readonly GoogleOptions options;
    private readonly TimeZoneInfo timeZone;

    public GoogleCalendarClient(IGoogleClientFactory factory, IOptions<GoogleOptions> options)
    {
        this.factory = factory;
        this.options = options.Value;
        timeZone = TimeZoneInfo.FindSystemTimeZoneById(this.options.TimeZoneId);
    }

    public Task<IReadOnlyList<CalendarEvent>> GetEventsForDay(DateOnly date) =>
        FetchEvents(date, date, resolveRecurrence: false);

    public Task<IReadOnlyList<CalendarEvent>> GetEventsInRange(DateOnly start, DateOnly end) =>
        FetchEvents(start, end, resolveRecurrence: true);

    public async Task AddEvent(CalendarEvent calendarEvent)
    {
        var service = factory.CreateCalendarService();
        var googleEvent = new Event
        {
            Summary = calendarEvent.Title,
            Location = calendarEvent.Location
        };

        if (calendarEvent.IsAllDay)
        {
            googleEvent.Start = new EventDateTime { Date = DateOnly.FromDateTime(calendarEvent.Start).ToString("yyyy-MM-dd") };
            googleEvent.End = new EventDateTime { Date = DateOnly.FromDateTime(calendarEvent.End).ToString("yyyy-MM-dd") };
        }
        else
        {
            googleEvent.Start = new EventDateTime { DateTimeDateTimeOffset = ToOffset(calendarEvent.Start) };
            googleEvent.End = new EventDateTime { DateTimeDateTimeOffset = ToOffset(calendarEvent.End) };
        }

        await service.Events.Insert(googleEvent, options.AddEventCalendarId).ExecuteAsync();
    }

    public async Task<string> EnsureCalendar(string summary)
    {
        var service = factory.CreateCalendarService();
        var existing = (await service.CalendarList.List().ExecuteAsync()).Items
            ?.FirstOrDefault(c => string.Equals(c.Summary, summary, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing.Id;

        var created = await service.Calendars.Insert(new GoogleCalendar { Summary = summary }).ExecuteAsync();
        return created.Id;
    }

    public async Task<IReadOnlyDictionary<string, string>> ListCalendars()
    {
        var service = factory.CreateCalendarService();
        var items = (await service.CalendarList.List().ExecuteAsync()).Items ?? [];
        return items.ToDictionary(c => c.Id, c => c.Summary ?? c.Id);
    }

    private async Task<IReadOnlyList<CalendarEvent>> FetchEvents(DateOnly start, DateOnly end, bool resolveRecurrence)
    {
        var service = factory.CreateCalendarService();
        var calendars = (await service.CalendarList.List().ExecuteAsync()).Items ?? [];

        var timeMin = DayStart(start);
        var timeMax = DayStart(end.AddDays(1));
        var masterCache = new Dictionary<string, (RecurrenceFrequency, int?)>();
        var results = new List<CalendarEvent>();

        foreach (var calendar in calendars)
        {
            var request = service.Events.List(calendar.Id);
            request.TimeMinDateTimeOffset = timeMin;
            request.TimeMaxDateTimeOffset = timeMax;
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.ShowDeleted = false;

            var events = (await request.ExecuteAsync()).Items ?? [];
            foreach (var googleEvent in events)
            {
                if (googleEvent.Start is null)
                    continue;

                var (recurrence, count) = resolveRecurrence
                    ? await ResolveRecurrence(service, calendar.Id, googleEvent, masterCache)
                    : (RecurrenceFrequency.None, null);

                results.Add(ToCalendarEvent(googleEvent, calendar.Id, recurrence, count));
            }
        }

        return results;
    }

    private CalendarEvent ToCalendarEvent(Event e, string calendarId, RecurrenceFrequency recurrence, int? count)
    {
        var isAllDay = e.Start.Date is not null;
        DateTime start, end;

        if (isAllDay)
        {
            start = DateOnly.Parse(e.Start.Date!).ToDateTime(TimeOnly.MinValue);
            end = e.End?.Date is not null
                ? DateOnly.Parse(e.End.Date).ToDateTime(TimeOnly.MinValue)
                : start.AddDays(1);
        }
        else
        {
            start = ToLocal(e.Start.DateTimeDateTimeOffset);
            end = e.End?.DateTimeDateTimeOffset is not null ? ToLocal(e.End.DateTimeDateTimeOffset) : start.AddHours(1);
        }

        return new CalendarEvent
        {
            Title = e.Summary ?? "(no title)",
            Location = e.Location,
            SourceCalendarId = calendarId,
            Start = start,
            End = end,
            IsAllDay = isAllDay,
            Recurrence = recurrence,
            RecurrenceCount = count,

            // Who was invited. Read FIRST when splitting a shared calendar (D31), because an
            // invited address is a fact about who is going where a name in a title is a heuristic.
            // ResponseStatus is deliberately not carried: a family that never answers an invitation
            // would otherwise get nothing, which is the common case rather than an edge one.
            Guests = e.Attendees?
                .Select(a => a.Email)
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .ToList() ?? [],
        };
    }

    private static async Task<(RecurrenceFrequency, int?)> ResolveRecurrence(
        CalendarService service,
        string calendarId,
        Event instance,
        Dictionary<string, (RecurrenceFrequency, int?)> cache)
    {
        if (string.IsNullOrEmpty(instance.RecurringEventId))
            return (RecurrenceFrequency.None, null);

        var key = $"{calendarId}:{instance.RecurringEventId}";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var master = await service.Events.Get(calendarId, instance.RecurringEventId).ExecuteAsync();
        var parsed = RecurrenceParser.Parse(master.Recurrence);
        cache[key] = parsed;
        return parsed;
    }

    private DateTimeOffset DayStart(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
    }

    private DateTime ToLocal(DateTimeOffset? value) =>
        TimeZoneInfo.ConvertTime(value!.Value, timeZone).DateTime;

    private DateTimeOffset ToOffset(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }
}
