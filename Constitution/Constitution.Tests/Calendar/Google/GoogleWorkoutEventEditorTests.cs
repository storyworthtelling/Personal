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
/// The workout event editor over the real Google client and a fake transport. It names an event by id, changes only its times and the private
/// key property, and tells no one; removing an event that is already gone is done.
/// </summary>
public class GoogleWorkoutEventEditorTests
{
    private static readonly PlacementKey Walk = PlacementKey.ForWorkout("Sam", SessionKind.OutdoorCardio, 2951, 0);

    private readonly FakeCalendarHttp http = new();
    private HouseholdSetupRead setup = new HouseholdSetupRead.Configured(HouseholdData.Setup());

    private GoogleWorkoutEventEditor Editor() => new(_ => Task.FromResult<CalendarService>(http.Service()), () => setup);

    private static JsonElement Body(SentRequest request) => JsonDocument.Parse(request.Body).RootElement;

    [Fact]
    public async Task AMovePatchesTimesAndKeyById()
    {
        http.Reply = _ => FakeCalendarHttp.Json("{\"id\":\"abc\"}");

        var edit = await Editor().MoveAsync("Sam", "abc", Walk, Est(11, 5, 7, 0), Est(11, 5, 7, 35), CancellationToken.None);

        Assert.True(edit.Succeeded);
        var sent = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Patch, sent.Method);
        Assert.Equal("/calendar/v3/calendars/sam@example.com/events/abc", Uri.UnescapeDataString(sent.Path));
        Assert.Contains("sendUpdates=none", sent.Query, StringComparison.OrdinalIgnoreCase);
        var body = Body(sent);
        Assert.Equal("2026-11-05T07:00:00-05:00", body.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("2026-11-05T07:35:00-05:00", body.GetProperty("end").GetProperty("dateTime").GetString());
        Assert.Equal(Walk.Value, body.GetProperty("extendedProperties").GetProperty("private").GetProperty("constitutionKey").GetString());
        Assert.False(body.TryGetProperty("summary", out _));
        Assert.False(body.TryGetProperty("attendees", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "A calendar was not found; it may not be shared.")]
    [InlineData(HttpStatusCode.Forbidden, "Google refused access to a calendar; it may not be shared.")]
    public async Task AMoveGoogleRefusesIsAFailure(HttpStatusCode status, string reason)
    {
        http.Reply = _ => FakeCalendarHttp.Error(status);

        var edit = await Editor().MoveAsync("Sam", "abc", Walk, Est(11, 5, 7, 0), Est(11, 5, 7, 35), CancellationToken.None);

        Assert.False(edit.Succeeded);
        Assert.Equal(reason, edit.FailedBecause);
    }

    [Fact]
    public async Task ARemoveDeletesByIdAndTellsNoOne()
    {
        http.Reply = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var edit = await Editor().RemoveAsync("Sam", "abc", CancellationToken.None);

        Assert.True(edit.Succeeded);
        var sent = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Delete, sent.Method);
        Assert.Equal("/calendar/v3/calendars/sam@example.com/events/abc", Uri.UnescapeDataString(sent.Path));
        Assert.Contains("sendUpdates=none", sent.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task RemovingWhatIsGoneIsDone(HttpStatusCode status)
    {
        http.Reply = _ => FakeCalendarHttp.Error(status);

        Assert.True((await Editor().RemoveAsync("Sam", "abc", CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task ARemoveGoogleRefusesIsAFailure()
    {
        http.Reply = _ => FakeCalendarHttp.Error(HttpStatusCode.Forbidden);

        var edit = await Editor().RemoveAsync("Sam", "abc", CancellationToken.None);

        Assert.False(edit.Succeeded);
        Assert.Equal("Google refused access to a calendar; it may not be shared.", edit.FailedBecause);
    }

    [Fact]
    public async Task NoCalendarFailsAndAsksNothing()
    {
        var edits = new[]
        {
            await Editor().MoveAsync("Robin", "abc", Walk, Est(11, 5, 7, 0), Est(11, 5, 7, 35), CancellationToken.None),
            await Editor().RemoveAsync("Robin", "abc", CancellationToken.None),
        };

        Assert.All(edits, edit =>
        {
            Assert.False(edit.Succeeded);
            Assert.Equal("This person's calendar is not set.", edit.FailedBecause);
        });
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ABlankIdIsRefusedUnsent()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Editor().RemoveAsync("Sam", " ", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Editor().MoveAsync("Sam", "", Walk, Est(11, 5, 7, 0), Est(11, 5, 7, 35), CancellationToken.None));
        Assert.Empty(http.Requests);
    }
}
