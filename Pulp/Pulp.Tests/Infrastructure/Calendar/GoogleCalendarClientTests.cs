using System.Net;
using Pulp.Domain.Calendar;
using Pulp.Infrastructure.Calendar;

namespace Pulp.Tests.Infrastructure.Calendar;

/// <summary>Fake-HTTP tests of the Calendar v3 calls, with an invented household and invented events.</summary>
public class GoogleCalendarClientTests
{
    private static readonly TimeZoneInfo Harbor =
        TimeZoneInfo.CreateCustomTimeZone("Invented/Harbor", TimeSpan.FromHours(-5), "Invented Harbor", "Invented Harbor");

    private static readonly DateOnly Day = new(2026, 10, 14);

    private static (GoogleCalendarClient Client, RecordingHandler Handler) Make()
    {
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return (new GoogleCalendarClient(http, () => Task.FromResult("token-for-tests")), handler);
    }

    private static CalendarEventDraft AllDay(string key) =>
        new(key, "New episode — Harbor Lights (Sam)", "S1 E4", CalendarWhen.AllDay(Day), TimeSpan.FromHours(1));

    private static CalendarEventDraft Timed(string key) =>
        new(key, "Rovers v Harriers (Sam)", null, new CalendarWhen(Day, new TimeOnly(19, 30)), TimeSpan.FromHours(3));

    [Fact]
    public async Task TheScopesAreTheNarrowestThatCanCreateAndShare()
    {
        Assert.Equal(
            ["https://www.googleapis.com/auth/calendar.app.created", "https://www.googleapis.com/auth/calendar.acls"],
            GoogleCalendarClient.Scopes);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CreateMakesASecondaryCalendarInTheHouseholdZoneAndReturnsItsId()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"cal-abc@group.calendar.google.com"}""");

        var id = await client.CreateAsync("Pulp", Harbor, CancellationToken.None);

        Assert.Equal("cal-abc@group.calendar.google.com", id);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/calendar/v3/calendars", sent.Uri.AbsolutePath);
        Assert.Equal("Bearer token-for-tests", sent.Authorization);
        Assert.Equal("Pulp", sent.Json!["summary"]!.GetValue<string>());
        Assert.Equal("Invented/Harbor", sent.Json!["timeZone"]!.GetValue<string>());
    }

    [Fact]
    public async Task ShareAsReaderPostsAReaderRuleForTheUser()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK);

        await client.ShareAsReaderAsync("cal-1", "partner@example.com", CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/calendar/v3/calendars/cal-1/acl", sent.Uri.AbsolutePath);
        Assert.Equal("reader", sent.Json!["role"]!.GetValue<string>());
        Assert.Equal("user", sent.Json!["scope"]!["type"]!.GetValue<string>());
        Assert.Equal("partner@example.com", sent.Json!["scope"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task SharedWithListsOnlyUserRules()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, """
            {"items":[
              {"role":"owner","scope":{"type":"user","value":"owner@example.com"}},
              {"role":"reader","scope":{"type":"user","value":"partner@example.com"}},
              {"role":"reader","scope":{"type":"default"}}]}
            """);

        var shared = await client.SharedWithAsync("cal-1", CancellationToken.None);

        Assert.Equal(["owner@example.com", "partner@example.com"], shared);
    }

    [Fact]
    public async Task ListReadsKeyedAndHandMadeEventsAndFollowsPages()
    {
        var (client, handler) = Make();
        handler
            .Enqueue(HttpStatusCode.OK, """
                {"nextPageToken":"p2","items":[
                  {"id":"e1","summary":"New episode — Harbor Lights (Sam)","description":"S1 E4","start":{"date":"2026-10-14"},"end":{"date":"2026-10-15"},
                   "extendedProperties":{"private":{"pulpKey":"episode:harbor-lights:1:4"}}},
                  {"id":"e2","summary":"Dentist","start":{"dateTime":"2026-10-14T09:00:00-05:00"},"end":{"dateTime":"2026-10-14T10:00:00-05:00"}}]}
                """)
            .Enqueue(HttpStatusCode.OK, """
                {"items":[
                  {"id":"e3","summary":"Rovers v Harriers (Sam)","start":{"dateTime":"2026-10-14T19:30:00-05:00"},"end":{"dateTime":"2026-10-14T22:30:00-05:00"},
                   "extendedProperties":{"private":{"pulpKey":"game:g1"}}},
                  {"id":"e4","status":"cancelled","summary":"Gone"}]}
                """);

        var events = await client.ListAsync("cal-1", Day, Day.AddDays(120), Harbor, CancellationToken.None);

        Assert.Equal(3, events.Count);
        Assert.Equal(("e1", "episode:harbor-lights:1:4"), (events[0].EventId, events[0].Key));
        Assert.True(events[0].When.IsAllDay);
        Assert.Equal(Day, events[0].When.Date);
        Assert.Null(events[1].Key);
        Assert.Equal(new TimeOnly(9, 0), events[1].When.Time);
        Assert.Equal(new CalendarWhen(Day, new TimeOnly(19, 30)), events[2].When);
        Assert.Equal("p2", handler.Requests[1].Query("pageToken"));
        Assert.Equal("true", handler.Requests[0].Query("singleEvents"));
        Assert.Equal("Invented/Harbor", handler.Requests[0].Query("timeZone"));
    }

    [Fact]
    public async Task InsertWritesAnAllDayEventWithTheKeyInPrivateProperties()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"new"}""");

        await client.InsertAsync("cal-1", AllDay("episode:harbor-lights:1:4"), Harbor, CancellationToken.None);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal("/calendar/v3/calendars/cal-1/events", sent.Uri.AbsolutePath);
        Assert.Equal("2026-10-14", sent.Json!["start"]!["date"]!.GetValue<string>());
        Assert.Equal("2026-10-15", sent.Json!["end"]!["date"]!.GetValue<string>());
        Assert.Null(sent.Json!["start"]!["dateTime"]);
        Assert.Equal("episode:harbor-lights:1:4", sent.Json!["extendedProperties"]!["private"]!["pulpKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task InsertWritesATimedEventWithItsOffsetAndZone()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"new"}""");

        await client.InsertAsync("cal-1", Timed("game:g1"), Harbor, CancellationToken.None);

        var start = Assert.Single(handler.Requests).Json!["start"]!;
        Assert.Equal("2026-10-14T19:30:00-05:00", start["dateTime"]!.GetValue<string>());
        Assert.Equal("Invented/Harbor", start["timeZone"]!.GetValue<string>());
        Assert.Equal("2026-10-14T22:30:00-05:00", handler.Requests[0].Json!["end"]!["dateTime"]!.GetValue<string>());
    }

    /// <summary>The bug this holds: a move that emails the guests, or a title announcing itself again.</summary>
    [Fact]
    public async Task EveryWriteSaysSendUpdatesNone()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"n"}""").Enqueue(HttpStatusCode.OK).Enqueue(HttpStatusCode.NoContent);

        await client.InsertAsync("cal-1", AllDay("episode:a:1:1"), Harbor, CancellationToken.None);
        await client.PatchAsync("cal-1", "e1", Timed("game:g1"), Harbor, CancellationToken.None);
        await client.DeleteAsync("cal-1", "e2", CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("none", request.Query("sendUpdates")));
        Assert.Equal(HttpMethod.Patch, handler.Requests[1].Method);
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
    }

    [Fact]
    public async Task PatchToAllDayClearsTheTimedFields()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK);

        await client.PatchAsync("cal-1", "e1", AllDay("episode:a:1:1"), Harbor, CancellationToken.None);

        var start = Assert.Single(handler.Requests).Json!["start"]!.AsObject();
        Assert.Equal("2026-10-14", start["date"]!.GetValue<string>());
        Assert.True(start.ContainsKey("dateTime"));
        Assert.Null(start["dateTime"]);
    }

    [Fact]
    public async Task DeletingAnEventAlreadyGoneIsDone()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.Gone);
        await client.DeleteAsync("cal-1", "e9", CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CalendarFailure.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, CalendarFailure.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, CalendarFailure.Throttled)]
    [InlineData(HttpStatusCode.InternalServerError, CalendarFailure.Unavailable)]
    public async Task AFailedResponseIsClassified(HttpStatusCode status, CalendarFailure expected)
    {
        var (client, handler) = Make();
        handler.Enqueue(status, """{"error":{"message":"nope"}}""");

        var failure = await Assert.ThrowsAsync<CalendarException>(() => client.CreateAsync("Pulp", Harbor, CancellationToken.None));

        Assert.Equal(expected, failure.Failure);
        Assert.Equal((int)status, failure.StatusCode);
        Assert.DoesNotContain("token-for-tests", failure.Message);
    }

    [Fact]
    public async Task ATimeoutIsUnavailable()
    {
        var (client, handler) = Make();
        handler.EnqueueFailure(new TaskCanceledException("timed out"));

        var failure = await Assert.ThrowsAsync<CalendarException>(() => client.CreateAsync("Pulp", Harbor, CancellationToken.None));

        Assert.Equal(CalendarFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task ACallersCancelStillPropagates()
    {
        var (client, handler) = Make();
        handler.EnqueueFailure(new TaskCanceledException("cancelled"));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateAsync("Pulp", Harbor, source.Token));
    }

    [Fact]
    public async Task AMalformedBodyIsMalformed()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, "<html>not json</html>");

        var failure = await Assert.ThrowsAsync<CalendarException>(() => client.CreateAsync("Pulp", Harbor, CancellationToken.None));

        Assert.Equal(CalendarFailure.Malformed, failure.Failure);
    }
}
