using System.Net;
using Constitution.Domain.Calendar;
using Constitution.Infrastructure.Calendar;
using Constitution.Tests.Setups;
using Google.Apis.Calendar.v3;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar.Google;

/// <summary>
/// Google Calendar read into the snapshot the weekly run plans from: every calendar of the household, recurring events already expanded, each
/// event by <c>GoogleEventMapper</c>'s rules, and a calendar that cannot be read said to be unreadable and never read as empty. The tests run the
/// real client library over a fake transport, so the URL parameters and the JSON are the real ones.
/// </summary>
public class GoogleHouseholdCalendarsTests
{
    private static readonly DateTimeOffset From = Est(11, 2, 0);
    private static readonly DateTimeOffset To = Est(11, 9, 0);

    private readonly FakeCalendarHttp http = new();
    private HouseholdSetupRead setup = new HouseholdSetupRead.Configured(HouseholdData.Setup());
    private Exception? signInFails;

    private GoogleHouseholdCalendars Source() => new(
        _ => signInFails is null ? Task.FromResult<CalendarService>(http.Service()) : Task.FromException<CalendarService>(signInFails),
        () => setup);

    private Task<HouseholdCalendarSnapshot> ReadAsync(CancellationToken cancellation = default) => Source().ReadAsync(From, To, cancellation);

    private static string Event(string id, string title, string start, string end, string extra = "") =>
        "{\"id\":\"" + id + "\",\"status\":\"confirmed\",\"summary\":\"" + title + "\",\"start\":{\"dateTime\":\"" + start + "\"},\"end\":{\"dateTime\":\"" + end + "\"}" + extra + "}";

    private static string Page(params string[] events) => "{\"kind\":\"calendar#events\",\"items\":[" + string.Join(",", events) + "]}";

    /// <summary>Answers each calendar by the id in the request path.</summary>
    private void Answer(Dictionary<string, string> pagesByCalendar, HttpStatusCode? otherwise = null)
    {
        http.Reply = request =>
        {
            var calendar = Uri.UnescapeDataString(request.Path).Split("/calendars/")[1].Split("/events")[0];
            if (pagesByCalendar.TryGetValue(calendar, out var page))
            {
                return FakeCalendarHttp.Json(page);
            }

            return otherwise is { } status ? FakeCalendarHttp.Error(status) : null;
        };
    }

    // ---- what is asked ----

    [Fact]
    public async Task EachCalendarIsAskedForTheWindowInUtcWithRecurringEventsExpanded()
    {
        await ReadAsync();

        var asked = http.Requests.Select(request => Uri.UnescapeDataString(request.Path).Split("/calendars/")[1].Split("/events")[0]).ToList();
        Assert.Equal(["sam@example.com", "lee@example.com", "family@example.com", "robin@example.com", "juniper@example.com"], asked);
        foreach (var request in http.Requests)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("singleEvents=True", request.Query, StringComparison.OrdinalIgnoreCase);
            Assert.True(request.Query.Contains("timeMin=2026-11-02T05:00:00.000Z", StringComparison.Ordinal), request.Query);
            Assert.True(request.Query.Contains("timeMax=2026-11-09T05:00:00.000Z", StringComparison.Ordinal), request.Query);
            Assert.DoesNotContain("showDeleted=True", request.Query, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ANextPageIsFollowedUntilThereIsNone()
    {
        http.Reply = request => request.Query.Contains("pageToken=second", StringComparison.Ordinal)
            ? FakeCalendarHttp.Json(Page(Event("b", "Second", "2026-11-04T09:00:00-05:00", "2026-11-04T10:00:00-05:00")))
            : FakeCalendarHttp.Json("{\"items\":[" + Event("a", "First", "2026-11-03T09:00:00-05:00", "2026-11-03T10:00:00-05:00") + "],\"nextPageToken\":\"second\"}");
        setup = new HouseholdSetupRead.Configured(new PlanningSetup([new HouseholdAdult("Sam", "sam@example.com")]) { NoFamilyCalendar = true, NoChildren = true });

        var snapshot = await ReadAsync();

        Assert.Equal(["First", "Second"], snapshot.OwnCalendar(Sam, From, To).Entries.Select(entry => entry.Title));
        Assert.Equal(2, http.Requests.Count);
    }

    // ---- what comes back ----

    [Fact]
    public async Task EveryCalendarLandsInItsOwnPlace()
    {
        Answer(new Dictionary<string, string>
        {
            ["sam@example.com"] = Page(Event("1", "Sam's dentist", "2026-11-03T09:00:00-05:00", "2026-11-03T10:00:00-05:00")),
            ["lee@example.com"] = Page(Event("2", "Lee's meeting", "2026-11-03T13:00:00-05:00", "2026-11-03T14:00:00-05:00")),
            ["family@example.com"] = Page(Event("3", "Piano lesson", "2026-11-04T16:00:00-05:00", "2026-11-04T17:00:00-05:00")),
            ["robin@example.com"] = Page(Event("4", "Robin's practice", "2026-11-05T15:00:00-05:00", "2026-11-05T16:00:00-05:00")),
            ["juniper@example.com"] = Page(Event("5", "Juniper's party", "2026-11-06T11:00:00-05:00", "2026-11-06T12:00:00-05:00")),
        });

        var snapshot = await ReadAsync();

        Assert.Equal(["Sam's dentist"], snapshot.OwnCalendar(Sam, From, To).Entries.Select(entry => entry.Title));
        Assert.Equal(["Lee's meeting"], snapshot.OwnCalendar(Lee, From, To).Entries.Select(entry => entry.Title));
        Assert.Equal(["Piano lesson"], snapshot.FamilyCalendar(From, To).Entries.Select(entry => entry.Title));
        Assert.Equal(["Robin's practice", "Juniper's party"], snapshot.ChildrensCalendars(From, To).Entries.Select(entry => entry.Title));
    }

    [Fact]
    public async Task TheMappersRulesApplyToWhatIsRead()
    {
        var ours = Event("w", "Workout · 40 min", "2026-11-03T06:00:00-05:00", "2026-11-03T06:40:00-05:00",
            ",\"extendedProperties\":{\"private\":{\"constitutionKey\":\"c1:sam:strength:2951:0\"}}");
        var declined = Event("d", "Conference", "2026-11-04T09:00:00-05:00", "2026-11-04T17:00:00-05:00",
            ",\"attendees\":[{\"email\":\"sam@example.com\",\"self\":true,\"responseStatus\":\"declined\"}]");
        var free = Event("f", "Birthday", "2026-11-05T09:00:00-05:00", "2026-11-05T10:00:00-05:00", ",\"transparency\":\"transparent\"");
        var cancelled = "{\"id\":\"c\",\"status\":\"cancelled\"}";
        var allDay = "{\"id\":\"a\",\"status\":\"confirmed\",\"summary\":\"Holiday\",\"start\":{\"date\":\"2026-11-06\"},\"end\":{\"date\":\"2026-11-08\"}}";
        Answer(new Dictionary<string, string> { ["sam@example.com"] = Page(ours, declined, free, cancelled, allDay) });

        var entries = (await ReadAsync()).OwnCalendar(Sam, From, To).Entries;

        Assert.Equal(["Birthday", "Holiday"], entries.Select(entry => entry.Title));
        Assert.Equal(Availability.Free, entries[0].Availability);
        Assert.True(entries[1].IsAllDay);
        Assert.Equal(new DateOnly(2026, 11, 8), entries[1].EndDayExclusive);
    }

    // ---- fail closed ----

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Google refused access to a calendar; it may not be shared.")]
    [InlineData(HttpStatusCode.NotFound, "A calendar was not found; it may not be shared.")]
    [InlineData(HttpStatusCode.Unauthorized, "Google did not accept the sign-in for a calendar.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Google could not answer.")]
    public async Task ACalendarGoogleWillNotGiveIsUnreadableNotEmpty(HttpStatusCode status, string reason)
    {
        Answer(new Dictionary<string, string>(), otherwise: null);
        http.Reply = request => request.Path.Contains("lee@example.com", StringComparison.Ordinal) || Uri.UnescapeDataString(request.Path).Contains("lee@example.com", StringComparison.Ordinal)
            ? FakeCalendarHttp.Error(status)
            : null;

        var snapshot = await ReadAsync();

        var lee = snapshot.OwnCalendar(Lee, From, To);
        Assert.False(lee.IsReadable);
        Assert.Empty(lee.Entries);
        Assert.Equal(reason, lee.UnreadableBecause);
        Assert.True(snapshot.OwnCalendar(Sam, From, To).IsReadable);
        Assert.True(snapshot.FamilyCalendar(From, To).IsReadable);
    }

    [Fact]
    public async Task ACalendarThatCannotBeReachedIsUnreadable()
    {
        http.Throws = new HttpRequestException("The platform's text, which may name a host.");

        var snapshot = await ReadAsync();

        Assert.All(new[] { Sam, Lee }, person =>
        {
            var read = snapshot.OwnCalendar(person, From, To);
            Assert.Equal("A calendar could not be reached.", read.UnreadableBecause);
        });
        Assert.False(snapshot.FamilyCalendar(From, To).IsReadable);
        Assert.False(snapshot.ChildrensCalendars(From, To).IsReadable);
    }

    [Fact]
    public async Task OneUnreadableChildrensCalendarMakesThemAllUnreadable()
    {
        http.Reply = request => Uri.UnescapeDataString(request.Path).Contains("juniper@example.com", StringComparison.Ordinal)
            ? FakeCalendarHttp.Error(HttpStatusCode.Forbidden)
            : FakeCalendarHttp.Json(Page(Event("r", "Robin's practice", "2026-11-05T15:00:00-05:00", "2026-11-05T16:00:00-05:00")));

        var children = (await ReadAsync()).ChildrensCalendars(From, To);

        Assert.False(children.IsReadable);
        Assert.Empty(children.Entries);
    }

    [Fact]
    public async Task AnEventThatCannotBeUnderstoodMakesItsCalendarUnreadable()
    {
        Answer(new Dictionary<string, string> { ["sam@example.com"] = Page("{\"id\":\"x\",\"status\":\"confirmed\",\"summary\":\"?\"}") });

        var sam = (await ReadAsync()).OwnCalendar(Sam, From, To);

        Assert.False(sam.IsReadable);
        Assert.Equal("An event on a calendar could not be understood.", sam.UnreadableBecause);
    }

    [Fact]
    public async Task ASignInProblemLeavesEveryCalendarUnreadableWithItsSentence()
    {
        signInFails = new CalendarAccessException("Google sign-in is needed to read calendars.");

        var snapshot = await ReadAsync();

        Assert.Equal("Google sign-in is needed to read calendars.", snapshot.OwnCalendar(Sam, From, To).UnreadableBecause);
        Assert.Equal("Google sign-in is needed to read calendars.", snapshot.OwnCalendar(Lee, From, To).UnreadableBecause);
        Assert.Equal("Google sign-in is needed to read calendars.", snapshot.FamilyCalendar(From, To).UnreadableBecause);
        Assert.Equal("Google sign-in is needed to read calendars.", snapshot.ChildrensCalendars(From, To).UnreadableBecause);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ABugIsNotTurnedIntoAnEmptyCalendar()
    {
        signInFails = new InvalidOperationException("A bug.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync());
    }

    [Fact]
    public async Task TheCallersCancellationIsNotAnUnreadableCalendar()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        http.Throws = new TaskCanceledException("Cancelled.");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAsync(source.Token));
    }

    // ---- what is not set ----

    [Fact]
    public async Task AnAdultWithNoCalendarIdIsUnreadableWithASpecificReason()
    {
        setup = new HouseholdSetupRead.Configured(new PlanningSetup([new HouseholdAdult("Sam", "sam@example.com"), new HouseholdAdult("Lee", null)])
        {
            FamilyCalendarId = "family@example.com",
            NoChildren = true,
        });

        var snapshot = await ReadAsync();

        Assert.Equal("This person's calendar is not set.", snapshot.OwnCalendar(Lee, From, To).UnreadableBecause);
        Assert.True(snapshot.OwnCalendar(Sam, From, To).IsReadable);
        Assert.DoesNotContain(http.Requests, request => Uri.UnescapeDataString(request.Path).Contains("/calendars//", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFamilyCalendarNotSetIsUnreadableAndOneThereIsNoneOfIsEmpty()
    {
        var notSet = new PlanningSetup([new HouseholdAdult("Sam", "sam@example.com")]) { NoChildren = true };
        setup = new HouseholdSetupRead.Configured(notSet);
        var unset = await ReadAsync();

        setup = new HouseholdSetupRead.Configured(notSet with { NoFamilyCalendar = true });
        var none = await ReadAsync();

        Assert.Equal("The family calendar is not set.", unset.FamilyCalendar(From, To).UnreadableBecause);
        Assert.True(none.FamilyCalendar(From, To).IsReadable);
        Assert.Empty(none.FamilyCalendar(From, To).Entries);
    }

    [Fact]
    public async Task TheChildrensCalendarsNotSetAreUnreadableAndNoChildrenIsEmpty()
    {
        var notSet = new PlanningSetup([new HouseholdAdult("Sam", "sam@example.com")]) { NoFamilyCalendar = true };
        setup = new HouseholdSetupRead.Configured(notSet);
        var unset = await ReadAsync();

        setup = new HouseholdSetupRead.Configured(notSet with { NoChildren = true });
        var none = await ReadAsync();

        Assert.Equal("The children's calendars are not set.", unset.ChildrensCalendars(From, To).UnreadableBecause);
        Assert.True(none.ChildrensCalendars(From, To).IsReadable);
        Assert.Empty(none.ChildrensCalendars(From, To).Entries);
    }

    [Fact]
    public async Task NoHouseholdMeansEveryCalendarIsUnreadableForThatReasonAndNothingIsAsked()
    {
        setup = new HouseholdSetupRead.NotSet("No household has been set up on this phone.");

        var snapshot = await ReadAsync();

        Assert.Equal("No household has been set up on this phone.", snapshot.FamilyCalendar(From, To).UnreadableBecause);
        Assert.Equal("No household has been set up on this phone.", snapshot.ChildrensCalendars(From, To).UnreadableBecause);
        Assert.False(snapshot.OwnCalendar(Sam, From, To).IsReadable);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task APersonNotInTheHouseholdHasNoCalendarLoaded()
    {
        var snapshot = await ReadAsync();

        Assert.False(snapshot.OwnCalendar("Robin", From, To).IsReadable);
    }
}
