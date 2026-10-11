using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pulp.Domain.Calendar;
using Pulp.Domain.Core;

namespace Pulp.Infrastructure.Calendar;

/// <summary>Why a calendar call failed, in the terms Diagnostics needs.</summary>
public enum CalendarFailure
{
    /// <summary>Google refused the sign-in or the scope (401, 403): the account has to grant it again.</summary>
    Rejected,

    /// <summary>A rate limit: try again later.</summary>
    Throttled,

    /// <summary>The service could not be reached, timed out, or failed on its side.</summary>
    Unavailable,

    /// <summary>A success whose body is not the documented shape.</summary>
    Malformed,
}

/// <summary>A calendar call that failed. The message never carries the access token.</summary>
public sealed class CalendarException : Exception
{
    public CalendarException(CalendarFailure failure, int? statusCode, string message, Exception? inner = null, string? reason = null)
        : base(message, inner)
    {
        Failure = failure;
        StatusCode = statusCode;
        Reason = reason;
    }

    /// <summary>Google's own reason for the refusal (<c>rateLimitExceeded</c>, <c>insufficientPermissions</c>…), when the body gave one.</summary>
    public string? Reason { get; }

    public CalendarFailure Failure { get; }

    public int? StatusCode { get; }
}

/// <summary>
/// The Pulp calendar over Google Calendar API v3 (HTTP, no SDK). The key Pulp finds its own events by
/// is kept in <c>extendedProperties.private</c>; every event write passes <c>sendUpdates=none</c>, so a
/// move never emails anyone (D149). The owner's install is the only caller that creates or writes
/// (<see cref="CalendarHousehold"/>); this class does not decide that.
/// </summary>
public sealed class GoogleCalendarClient : IPulpCalendar
{
    /// <summary>
    /// The narrowest Calendar pair that can do the whole job. <c>calendar.app.created</c> creates secondary
    /// calendars and manages events on the ones this app made; it cannot share, so
    /// <c>calendar.acls</c> (view and change sharing on calendars you own) covers the one other call.
    /// The full <c>calendar</c> scope would also read every other calendar the owner has. These are only the
    /// Calendar scopes: the sign-in built from them (<c>VoiceGoogleAccount</c>) also carries Drive, because it
    /// shares the stored Google account with the vault and the voice profile.
    /// </summary>
    public static readonly IReadOnlyList<string> Scopes =
    [
        "https://www.googleapis.com/auth/calendar.app.created",
        "https://www.googleapis.com/auth/calendar.acls",
    ];

    private const string Root = "https://www.googleapis.com/calendar/v3/";
    private const string Rfc3339 = "yyyy-MM-dd'T'HH:mm:ss";

    private readonly HttpClient httpClient;
    private readonly Func<Task<string>> accessToken;

    public GoogleCalendarClient(HttpClient httpClient, Func<Task<string>> accessToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(accessToken);
        this.httpClient = httpClient;
        this.accessToken = accessToken;
    }

    public async Task<string> CreateAsync(string title, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["summary"] = title, ["timeZone"] = zone.Id };
        using var response = await Send(HttpMethod.Post, Root + "calendars", body, "creating the calendar", cancellationToken);
        return Required(response, "id", "creating the calendar");
    }

    public async Task<IReadOnlyList<string>> SharedWithAsync(string calendarId, CancellationToken cancellationToken)
    {
        using var response = await Send(HttpMethod.Get, Root + "calendars/" + Uri.EscapeDataString(calendarId) + "/acl", null, "reading who the calendar is shared with", cancellationToken);
        return Items(response)
            .Where(rule => rule["scope"]?["type"]?.GetValue<string>() == "user")
            .Select(rule => rule["scope"]?["value"]?.GetValue<string>())
            .OfType<string>()
            .ToList();
    }

    public async Task ShareAsReaderAsync(string calendarId, string account, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["role"] = CalendarSharing.Role,
            ["scope"] = new JsonObject { ["type"] = "user", ["value"] = account },
        };
        using var response = await Send(HttpMethod.Post, Root + "calendars/" + Uri.EscapeDataString(calendarId) + "/acl", body, "sharing the calendar", cancellationToken);
    }

    public async Task<IReadOnlyList<ExistingEvent>> ListAsync(
        string calendarId, DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var events = new List<ExistingEvent>();
        string? page = null;
        do
        {
            var url = EventsUrl(calendarId)
                + "?singleEvents=true&showDeleted=false&maxResults=2500"
                + "&timeZone=" + Uri.EscapeDataString(zone.Id)
                + "&timeMin=" + Uri.EscapeDataString(Midnight(from))
                + "&timeMax=" + Uri.EscapeDataString(Midnight(to))
                + (page is null ? string.Empty : "&pageToken=" + Uri.EscapeDataString(page));
            using var response = await Send(HttpMethod.Get, url, null, "reading the calendar's events", cancellationToken);
            foreach (var item in Items(response))
            {
                var read = ReadEvent(item, zone);
                if (read is not null)
                {
                    events.Add(read);
                }
            }

            page = response.Root["nextPageToken"]?.GetValue<string>();
        }
        while (!string.IsNullOrEmpty(page));

        return events;
    }

    public async Task InsertAsync(string calendarId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        using var response = await Send(HttpMethod.Post, Silent(EventsUrl(calendarId)), EventBody(draft, zone, true), "adding an event", cancellationToken);
    }

    public async Task PatchAsync(string calendarId, string eventId, CalendarEventDraft draft, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        using var response = await Send(HttpMethod.Patch, Silent(EventsUrl(calendarId) + "/" + Uri.EscapeDataString(eventId)), EventBody(draft, zone, false), "moving an event", cancellationToken, HttpStatusCode.NotFound);
        if (response.Status == HttpStatusCode.NotFound)
        {
            // Someone deleted the event by hand: what the plan wanted is the event, so make it.
            await InsertAsync(calendarId, draft, zone, cancellationToken);
        }
    }

    public async Task DeleteAsync(string calendarId, string eventId, CancellationToken cancellationToken)
    {
        // 404 and 410 both mean the event is already gone, which is what was asked for.
        using var response = await Send(HttpMethod.Delete, Silent(EventsUrl(calendarId) + "/" + Uri.EscapeDataString(eventId)), null, "deleting an event", cancellationToken, HttpStatusCode.NotFound, HttpStatusCode.Gone);
    }

    private static string EventsUrl(string calendarId) => Root + "calendars/" + Uri.EscapeDataString(calendarId) + "/events";

    /// <summary>The one place an event write gets its query: nobody is told, ever.</summary>
    private static string Silent(string url) => url + "?sendUpdates=none";

    private static string Midnight(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static JsonObject EventBody(CalendarEventDraft draft, TimeZoneInfo zone, bool inserting)
    {
        var body = new JsonObject
        {
            ["summary"] = draft.Title,
            ["extendedProperties"] = new JsonObject { ["private"] = new JsonObject { [PulpEventKey.Property] = draft.Key } },
        };

        if (!string.IsNullOrEmpty(draft.Description))
        {
            body["description"] = draft.Description;
        }
        else if (!inserting)
        {
            body["description"] = null;
        }

        if (draft.When.Time is { } time)
        {
            var start = draft.When.Date.ToDateTime(time);
            var end = start + draft.Length;
            body["start"] = Timed(start, zone, inserting);
            body["end"] = Timed(end, zone, inserting);
        }
        else
        {
            body["start"] = Dated(draft.When.Date, inserting);
            body["end"] = Dated(draft.When.Date.AddDays(1), inserting);
        }

        return body;
    }

    private static JsonObject Timed(DateTime local, TimeZoneInfo zone, bool inserting)
    {
        var offset = zone.GetUtcOffset(local);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var stamp = local.ToString(Rfc3339, CultureInfo.InvariantCulture) + sign + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        var value = new JsonObject { ["dateTime"] = stamp, ["timeZone"] = zone.Id };
        if (!inserting)
        {
            value["date"] = null;
        }

        return value;
    }

    private static JsonObject Dated(DateOnly date, bool inserting)
    {
        var value = new JsonObject { ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        if (!inserting)
        {
            // A patch merges: an event that was timed keeps its dateTime unless it is cleared.
            value["dateTime"] = null;
            value["timeZone"] = null;
        }

        return value;
    }

    private static ExistingEvent? ReadEvent(JsonNode item, TimeZoneInfo zone)
    {
        if (item["status"]?.GetValue<string>() == "cancelled")
        {
            return null;
        }

        var id = item["id"]?.GetValue<string>();
        var start = item["start"];
        if (id is null || start is null)
        {
            return null;
        }

        CalendarWhen when;
        if (start["dateTime"]?.GetValue<string>() is { } stamp && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
        {
            when = CalendarWhen.At(instant, zone);
        }
        else if (start["date"]?.GetValue<string>() is { } day && DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            when = CalendarWhen.AllDay(date);
        }
        else
        {
            return null;
        }

        var key = item["extendedProperties"]?["private"]?[PulpEventKey.Property]?.GetValue<string>();
        return new ExistingEvent(id, key, item["summary"]?.GetValue<string>() ?? string.Empty, item["description"]?.GetValue<string>(), when);
    }

    private static IEnumerable<JsonNode> Items(ParsedResponse response) =>
        response.Root["items"] is JsonArray items ? items.OfType<JsonNode>() : [];

    private static string Required(ParsedResponse response, string name, string what) =>
        response.Root[name]?.GetValue<string>()
        ?? throw new CalendarException(CalendarFailure.Malformed, null, $"Google Calendar answered {what} without a '{name}'.");

    private async Task<ParsedResponse> Send(
        HttpMethod method, string url, JsonNode? body, string what, CancellationToken cancellationToken, params HttpStatusCode[] answers)
    {
        var token = await accessToken();
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpStatusCode status;
        string text;
        try
        {
            using var message = await httpClient.SendAsync(request, cancellationToken);
            status = message.StatusCode;
            text = await message.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new CalendarException(CalendarFailure.Unavailable, null, $"Google Calendar could not be reached while {what}: {exception.Message}", exception);
        }
        catch (OperationCanceledException exception) when (!CancellationVerdict.IsUserCancel(exception, cancellationToken))
        {
            // The client's own timeout fires as a cancellation nobody asked for: the service was too slow.
            throw new CalendarException(CalendarFailure.Unavailable, null, $"Google Calendar did not answer while {what} within {httpClient.Timeout.TotalSeconds:0} s: {exception.Message}", exception);
        }

        var code = (int)status;
        if (code is >= 200 and < 300 || answers.Contains(status))
        {
            return Parse(text, what, status);
        }

        var reason = ErrorReason(text);
        var failure = status switch
        {
            HttpStatusCode.Forbidden when reason is "rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded" or "dailyLimitExceeded" or "calendarUsageLimitsExceeded" => CalendarFailure.Throttled,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => CalendarFailure.Rejected,
            HttpStatusCode.TooManyRequests => CalendarFailure.Throttled,
            _ => CalendarFailure.Unavailable,
        };
        var excerpt = text.Trim();
        throw new CalendarException(failure, code, $"Google Calendar answered {what} with HTTP {code}: {(excerpt.Length <= 200 ? excerpt : excerpt[..200] + "…")}", null, reason);
    }

    /// <summary>The first <c>error.errors[].reason</c> (or <c>error.details[].reason</c>) of an error body; null when there is none or it is not JSON.</summary>
    private static string? ErrorReason(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var error = (JsonNode.Parse(text) as JsonObject)?["error"];
            foreach (var list in new[] { error?["errors"], error?["details"] })
            {
                if (list is JsonArray entries && entries.Select(entry => entry?["reason"]?.GetValue<string>()).FirstOrDefault(reason => !string.IsNullOrEmpty(reason)) is { } found)
                {
                    return found;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            // An error body that is not the documented shape has no reason to give; the status stands alone.
        }

        return null;
    }

    private static ParsedResponse Parse(string text, string what, HttpStatusCode status)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParsedResponse(new JsonObject(), status);
        }

        // A 404 the caller asked to be told about is not a document; its body is Google's error, not data.
        if (status == HttpStatusCode.NotFound)
        {
            return new ParsedResponse(new JsonObject(), status);
        }

        try
        {
            return new ParsedResponse(JsonNode.Parse(text) as JsonObject
                ?? throw new CalendarException(CalendarFailure.Malformed, null, $"Google Calendar answered {what} with something other than a JSON object."), status);
        }
        catch (JsonException exception)
        {
            throw new CalendarException(CalendarFailure.Malformed, null, $"Google Calendar answered {what} with a body that is not JSON: {exception.Message}", exception);
        }
    }

    private sealed record ParsedResponse(JsonObject Root, HttpStatusCode Status) : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
