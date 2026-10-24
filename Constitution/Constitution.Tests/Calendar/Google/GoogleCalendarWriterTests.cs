using System.Net;
using System.Text.Json;
using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Calendar;
using Constitution.Tests.Setups;
using Google.Apis.Calendar.v3;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar.Google;

/// <summary>
/// The calendar writer over the real Google client and a fake transport. Finding looks events up by the private extended property across all
/// time and says "unavailable" whenever it cannot give a complete, trustworthy answer (a partial or empty list read as "the owner deleted it" would
/// make the planner skip a session for good). Creating makes one event with no guests and no notice to anyone; editing changes only an event named by id.
/// </summary>
public class GoogleCalendarWriterTests
{
    private static readonly PlacementKey Strength = PlacementKey.ForWorkout("Sam", SessionKind.Strength, 2951, 0);
    private static readonly PlacementKey Walk = PlacementKey.ForWorkout("Sam", SessionKind.OutdoorCardio, 2951, 0);

    private readonly FakeCalendarHttp http = new();
    private HouseholdSetupRead setup = new HouseholdSetupRead.Configured(HouseholdData.Setup());
    private Exception? signInFails;

    private GoogleCalendarWriter Writer() => new(
        _ => signInFails is null ? Task.FromResult<CalendarService>(http.Service()) : Task.FromException<CalendarService>(signInFails),
        () => setup,
        Zone);

    private static string HeldEvent(string id, PlacementKey key, string start, string end, string status = "confirmed") =>
        "{\"id\":\"" + id + "\",\"status\":\"" + status + "\",\"summary\":\"Workout · 40 min\",\"start\":{\"dateTime\":\"" + start + "\"},\"end\":{\"dateTime\":\"" + end + "\"}," +
        "\"extendedProperties\":{\"private\":{\"constitutionKey\":\"" + key.Value + "\"}}}";

    private static string Page(params string[] events) => "{\"items\":[" + string.Join(",", events) + "]}";

    private static EventToCreate ToCreate(PlacementKey? key = null) =>
        new(key ?? Strength, "Sam", "Workout · 40 min", Est(11, 3, 6, 0), Est(11, 3, 6, 40));

    private static JsonElement Body(SentRequest request) => JsonDocument.Parse(request.Body).RootElement;

    // ---- find ----

    [Fact]
    public async Task EachKeyIsLookedUpOnThePersonsCalendarByItsPrivateProperty()
    {
        var lookup = await Writer().FindAsync("Sam", [Strength, Walk], CancellationToken.None);

        Assert.True(lookup.IsAvailable);
        Assert.Equal(2, http.Requests.Count);
        Assert.All(http.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/calendar/v3/calendars/sam@example.com/events", Uri.UnescapeDataString(request.Path));
        });
        Assert.Contains("privateExtendedProperty=constitutionKey=" + Strength.Value, http.Requests[0].Query, StringComparison.Ordinal);
        Assert.Contains("privateExtendedProperty=constitutionKey=" + Walk.Value, http.Requests[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLookUpIsAcrossAllTimeAndNeverAsksForDeletedEvents()
    {
        await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        var query = Assert.Single(http.Requests).Query;
        Assert.DoesNotContain("timeMin", query, StringComparison.Ordinal);
        Assert.DoesNotContain("timeMax", query, StringComparison.Ordinal);
        Assert.DoesNotContain("showDeleted=true", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhatIsFoundIsHeldAtTheTimeTheCalendarSaysAnywhereInTime()
    {
        // The owner dragged it to another week: it is found, and its time is where the owner put it.
        http.Reply = _ => FakeCalendarHttp.Json(Page(HeldEvent("abc", Strength, "2026-12-14T17:30:00-05:00", "2026-12-14T18:10:00-05:00")));

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        var held = Assert.Single(lookup.Held);
        Assert.Equal("abc", held.EventId);
        Assert.Equal(Strength, held.Key);
        Assert.Equal(Est(12, 14, 17, 30), held.Start);
        Assert.Equal(Est(12, 14, 18, 10), held.End);
    }

    [Fact]
    public async Task AllTheKeysAnswersAreCombined()
    {
        http.Reply = request => request.Query.Contains(Walk.Value, StringComparison.Ordinal)
            ? FakeCalendarHttp.Json(Page(HeldEvent("w", Walk, "2026-11-05T07:00:00-05:00", "2026-11-05T07:35:00-05:00")))
            : FakeCalendarHttp.Json(Page(HeldEvent("s", Strength, "2026-11-03T06:00:00-05:00", "2026-11-03T06:40:00-05:00")));

        var lookup = await Writer().FindAsync("Sam", [Strength, Walk], CancellationToken.None);

        Assert.Equal(["s", "w"], lookup.Held.Select(held => held.EventId));
    }

    [Fact]
    public async Task NothingFoundIsAnAnswerThatTheyAreNotThere()
    {
        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.True(lookup.IsAvailable);
        Assert.Empty(lookup.Held);
    }

    [Fact]
    public async Task NoKeysAskNothing()
    {
        var lookup = await Writer().FindAsync("Sam", [], CancellationToken.None);

        Assert.True(lookup.IsAvailable);
        Assert.Empty(lookup.Held);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ANextPageIsFollowed()
    {
        http.Reply = request => request.Query.Contains("pageToken=more", StringComparison.Ordinal)
            ? FakeCalendarHttp.Json(Page(HeldEvent("two", Strength, "2026-11-10T06:00:00-05:00", "2026-11-10T06:40:00-05:00")))
            : FakeCalendarHttp.Json("{\"items\":[" + HeldEvent("one", Strength, "2026-11-03T06:00:00-05:00", "2026-11-03T06:40:00-05:00") + "],\"nextPageToken\":\"more\"}");

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.Equal(["one", "two"], lookup.Held.Select(held => held.EventId));
    }

    [Fact]
    public async Task AnEventGoogleStillSendsAsCancelledIsDeleted()
    {
        http.Reply = _ => FakeCalendarHttp.Json(Page(HeldEvent("gone", Strength, "2026-11-03T06:00:00-05:00", "2026-11-03T06:40:00-05:00", status: "cancelled")));

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.True(lookup.IsAvailable);
        Assert.Empty(lookup.Held);
    }

    [Fact]
    public async Task AnAllDayEventTheOwnerMadeOfItIsHeldFromMidnightToMidnightOnTheHouseholdsClock()
    {
        http.Reply = _ => FakeCalendarHttp.Json(Page(
            "{\"id\":\"day\",\"status\":\"confirmed\",\"start\":{\"date\":\"2026-11-05\"},\"end\":{\"date\":\"2026-11-06\"}," +
            "\"extendedProperties\":{\"private\":{\"constitutionKey\":\"" + Strength.Value + "\"}}}"));

        var held = Assert.Single((await Writer().FindAsync("Sam", [Strength], CancellationToken.None)).Held);

        Assert.Equal(Est(11, 5, 0), held.Start);
        Assert.Equal(Est(11, 6, 0), held.End);
    }

    [Fact]
    public async Task AnAnswerThatIsNotAboutTheKeyIsNotTrusted()
    {
        // The filter was ignored and Google gave the whole calendar: reading that as "found" would hold every event under this key.
        http.Reply = _ => FakeCalendarHttp.Json(Page(
            "{\"id\":\"other\",\"status\":\"confirmed\",\"summary\":\"Dentist\",\"start\":{\"dateTime\":\"2026-11-03T09:00:00-05:00\"},\"end\":{\"dateTime\":\"2026-11-03T10:00:00-05:00\"}}"));

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.False(lookup.IsAvailable);
        Assert.Equal("The calendar's answer could not be trusted.", lookup.UnavailableBecause);
        Assert.Empty(lookup.Held);
    }

    [Fact]
    public async Task AnAnswerForAnotherKeyIsNotTrusted()
    {
        http.Reply = _ => FakeCalendarHttp.Json(Page(HeldEvent("x", Walk, "2026-11-03T06:00:00-05:00", "2026-11-03T06:40:00-05:00")));

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.False(lookup.IsAvailable);
    }

    [Fact]
    public async Task AnEventWithNoTimesIsADoubtNotAMiss()
    {
        http.Reply = _ => FakeCalendarHttp.Json(Page(
            "{\"id\":\"x\",\"status\":\"confirmed\",\"extendedProperties\":{\"private\":{\"constitutionKey\":\"" + Strength.Value + "\"}}}"));

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.False(lookup.IsAvailable);
        Assert.Equal("The calendar's answer could not be trusted.", lookup.UnavailableBecause);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Google refused access to a calendar; it may not be shared.")]
    [InlineData(HttpStatusCode.NotFound, "A calendar was not found; it may not be shared.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Google could not answer.")]
    public async Task ACalendarThatWillNotAnswerIsUnavailableNeverEmpty(HttpStatusCode status, string reason)
    {
        http.Reply = _ => FakeCalendarHttp.Error(status);

        var lookup = await Writer().FindAsync("Sam", [Strength, Walk], CancellationToken.None);

        Assert.False(lookup.IsAvailable);
        Assert.Equal(reason, lookup.UnavailableBecause);
        Assert.Empty(lookup.Held);
    }

    [Fact]
    public async Task AFailureOnTheSecondKeyMakesTheWholeAnswerUnavailable()
    {
        // The first key's event was found; a partial list would have the second read as deleted.
        http.Reply = request => request.Query.Contains(Walk.Value, StringComparison.Ordinal)
            ? FakeCalendarHttp.Error(HttpStatusCode.ServiceUnavailable)
            : FakeCalendarHttp.Json(Page(HeldEvent("s", Strength, "2026-11-03T06:00:00-05:00", "2026-11-03T06:40:00-05:00")));

        var lookup = await Writer().FindAsync("Sam", [Strength, Walk], CancellationToken.None);

        Assert.False(lookup.IsAvailable);
        Assert.Empty(lookup.Held);
    }

    [Fact]
    public async Task AFailureOnALaterPageMakesTheAnswerUnavailable()
    {
        http.Reply = request => request.Query.Contains("pageToken=more", StringComparison.Ordinal)
            ? FakeCalendarHttp.Error(HttpStatusCode.InternalServerError)
            : FakeCalendarHttp.Json("{\"items\":[" + HeldEvent("one", Strength, "2026-11-03T06:00:00-05:00", "2026-11-03T06:40:00-05:00") + "],\"nextPageToken\":\"more\"}");

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.False(lookup.IsAvailable);
        Assert.Empty(lookup.Held);
    }

    [Fact]
    public async Task APersonWithNoCalendarSetIsUnavailableAndNothingIsAsked()
    {
        setup = new HouseholdSetupRead.Configured(new PlanningSetup([new HouseholdAdult("Sam", null)]));

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.False(lookup.IsAvailable);
        Assert.Equal("This person's calendar is not set.", lookup.UnavailableBecause);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task AStrangerIsUnavailableToo()
    {
        var lookup = await Writer().FindAsync("Robin", [Strength], CancellationToken.None);

        Assert.Equal("This person's calendar is not set.", lookup.UnavailableBecause);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task NoHouseholdIsUnavailableForThatReason()
    {
        setup = new HouseholdSetupRead.NotSet("No household has been set up on this phone.");

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.Equal("No household has been set up on this phone.", lookup.UnavailableBecause);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ASignInProblemIsUnavailableWithItsSentence()
    {
        signInFails = new CalendarAccessException("Google sign-in is needed to read calendars.");

        var lookup = await Writer().FindAsync("Sam", [Strength], CancellationToken.None);

        Assert.Equal("Google sign-in is needed to read calendars.", lookup.UnavailableBecause);
    }

    [Fact]
    public async Task ABugPropagates()
    {
        signInFails = new InvalidOperationException("A bug.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer().FindAsync("Sam", [Strength], CancellationToken.None));
    }

    [Fact]
    public async Task TheCallersCancellationPropagates()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        http.Throws = new TaskCanceledException("Cancelled.");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Writer().FindAsync("Sam", [Strength], source.Token));
    }

    // ---- create ----

    [Fact]
    public async Task AnEventIsMadeOnThePersonsCalendarWithTheKeyInAPrivateProperty()
    {
        http.Reply = _ => FakeCalendarHttp.Json("{\"id\":\"made-1\"}");

        var made = await Writer().CreateAsync(ToCreate(), CancellationToken.None);

        Assert.True(made.Succeeded);
        Assert.Equal("made-1", made.EventId);
        var sent = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/calendar/v3/calendars/sam@example.com/events", Uri.UnescapeDataString(sent.Path));
        var body = Body(sent);
        Assert.Equal("Workout · 40 min", body.GetProperty("summary").GetString());
        Assert.Equal(Strength.Value, body.GetProperty("extendedProperties").GetProperty("private").GetProperty("constitutionKey").GetString());
    }

    [Fact]
    public async Task TimesCarryTheOffset()
    {
        http.Reply = _ => FakeCalendarHttp.Json("{\"id\":\"made-1\"}");

        await Writer().CreateAsync(ToCreate(), CancellationToken.None);

        var body = Body(Assert.Single(http.Requests));
        Assert.Equal("2026-11-03T06:00:00-05:00", body.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("2026-11-03T06:40:00-05:00", body.GetProperty("end").GetProperty("dateTime").GetString());
    }

    [Fact]
    public async Task NoGuestsNoDescriptionAndNoNoticeToAnyone()
    {
        http.Reply = _ => FakeCalendarHttp.Json("{\"id\":\"made-1\"}");

        await Writer().CreateAsync(ToCreate(), CancellationToken.None);

        var sent = Assert.Single(http.Requests);
        var body = Body(sent);
        Assert.False(body.TryGetProperty("attendees", out _));
        Assert.False(body.TryGetProperty("description", out _));
        Assert.False(body.TryGetProperty("location", out _));
        Assert.Contains("sendUpdates=none", sent.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sendNotifications=true", sent.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheKeyIsNeverInTheTitleOrAnywhereButTheProperty()
    {
        http.Reply = _ => FakeCalendarHttp.Json("{\"id\":\"made-1\"}");

        await Writer().CreateAsync(ToCreate(), CancellationToken.None);

        var body = Body(Assert.Single(http.Requests));
        Assert.DoesNotContain(Strength.Value, body.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(Assert.Single(http.Requests).Body, System.Text.RegularExpressions.Regex.Escape(Strength.Value)).Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Google refused access to a calendar; it may not be shared.")]
    [InlineData(HttpStatusCode.NotFound, "A calendar was not found; it may not be shared.")]
    [InlineData(HttpStatusCode.TooManyRequests, "Google asked Constitution to slow down.")]
    public async Task AnEventGoogleWillNotTakeIsAFailureWithItsReason(HttpStatusCode status, string reason)
    {
        http.Reply = _ => FakeCalendarHttp.Error(status);

        var made = await Writer().CreateAsync(ToCreate(), CancellationToken.None);

        Assert.False(made.Succeeded);
        Assert.Equal(reason, made.FailedBecause);
    }

    [Fact]
    public async Task ANetworkFailureOnCreateIsAFailureToRetry()
    {
        http.Throws = new HttpRequestException("The platform's text.");

        var made = await Writer().CreateAsync(ToCreate(), CancellationToken.None);

        Assert.False(made.Succeeded);
        Assert.Equal("A calendar could not be reached.", made.FailedBecause);
    }

    [Fact]
    public async Task AReplyWithNoIdIsAFailureNotASuccess()
    {
        http.Reply = _ => FakeCalendarHttp.Json("{}");

        var made = await Writer().CreateAsync(ToCreate(), CancellationToken.None);

        Assert.False(made.Succeeded);
        Assert.Equal("Google gave no id for the new event.", made.FailedBecause);
    }

    [Fact]
    public async Task APersonWithNoCalendarSetCannotBeWrittenTo()
    {
        setup = new HouseholdSetupRead.Configured(new PlanningSetup([new HouseholdAdult("Sam", null)]));

        var made = await Writer().CreateAsync(ToCreate(), CancellationToken.None);

        Assert.Equal("This person's calendar is not set.", made.FailedBecause);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ABugOnCreatePropagates()
    {
        signInFails = new InvalidOperationException("A bug.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Writer().CreateAsync(ToCreate(), CancellationToken.None));
    }
}
