using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Infrastructure.Calendar;
using Constitution.Tests.Calendar.Google;
using Constitution.Tests.Setups;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.CrossRun;

/// <summary>
/// What a run's calendar read does with the events that carry the app's private key. A run drops its own earlier events (read back
/// as busy time, its week would never settle) and keeps the other run's, which are somebody's cook block or workout and are busy like
/// any other. A key it cannot place in either family is kept too: unknown means somebody's time.
/// </summary>
public class FamilyReadTests
{
    private const string CalendarId = "sam@example.com";
    private const string CookKey = "c1:sam:household:5:cook-tue";
    private const string SlowKey = "c1:sam:household:5:slowcooker-tue";
    private const string TripKey = "c1:sam:household:5:groceries";
    private const string WorkoutKey = "c1:sam:strength:5:0";

    private static Event Keyed(string title, string? key)
    {
        var made = new Event
        {
            Id = "e1",
            Summary = title,
            Status = "confirmed",
            Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 10, 27, 16, 0, 0, TimeSpan.FromHours(-4)) },
            End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.FromHours(-4)) },
        };
        if (key is not null)
        {
            made.ExtendedProperties = new Event.ExtendedPropertiesData { Private__ = new Dictionary<string, string> { [PlacementKey.Property] = key } };
        }

        return made;
    }

    private static CalendarEntry Kept(Event googleEvent, KeyFamily? family) =>
        Assert.IsType<EventMapping.Kept>(GoogleEventMapper.Map(googleEvent, CalendarId, family)).Entry;

    private static EventLeftOut LeftOut(Event googleEvent, KeyFamily? family) =>
        Assert.IsType<EventMapping.LeftOut>(GoogleEventMapper.Map(googleEvent, CalendarId, family)).Why;

    // ---- the mapper ----

    [Theory]
    [InlineData(CookKey)]
    [InlineData(SlowKey)]
    [InlineData(TripKey)]
    public void WorkoutRunKeepsMealEventsAsBusy(string key)
    {
        var entry = Kept(Keyed("Cook: chili", key), KeyFamily.Workout);

        Assert.Equal("Cook: chili", entry.Title);
        Assert.Equal(Availability.Busy, entry.Availability);
        Assert.False(entry.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 20, 0, 0, TimeSpan.Zero), entry.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 21, 0, 0, TimeSpan.Zero), entry.End);
    }

    [Fact]
    public void MealRunKeepsWorkoutsAsBusy()
    {
        var entry = Kept(Keyed("Workout · 40 min", WorkoutKey), KeyFamily.HouseholdMeal);

        Assert.Equal("Workout · 40 min", entry.Title);
        Assert.Equal(Availability.Busy, entry.Availability);
    }

    [Theory]
    [InlineData(WorkoutKey, KeyFamily.Workout)]
    [InlineData(CookKey, KeyFamily.HouseholdMeal)]
    [InlineData(SlowKey, KeyFamily.HouseholdMeal)]
    [InlineData(TripKey, KeyFamily.HouseholdMeal)]
    public void ARunStillDropsItsOwnEvents(string key, KeyFamily family)
    {
        Assert.Equal(EventLeftOut.OurOwn, LeftOut(Keyed("Whatever it is", key), family));
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData("c9:sam:strength:5:0")]
    [InlineData("c1:sam:household:5:errand")]
    [InlineData("c1:sam:swimming:5:0")]
    public void AKeyNeitherRunOwnsIsKeptByBothAsBusyTime(string key)
    {
        foreach (var family in new[] { KeyFamily.Workout, KeyFamily.HouseholdMeal })
        {
            var entry = Kept(Keyed("Somebody's hour", key), family);

            Assert.Equal("Somebody's hour", entry.Title);
            Assert.Equal(Availability.Busy, entry.Availability);
        }
    }

    [Fact]
    public void NoFamilyDropsEveryKeyedEvent()
    {
        foreach (var key in new[] { WorkoutKey, CookKey, SlowKey, TripKey, "nonsense" })
        {
            Assert.Equal(EventLeftOut.OurOwn, LeftOut(Keyed("Any", key), null));
        }
    }

    [Fact]
    public void AnEventWithNoKeyIsKeptWhateverTheFamily()
    {
        foreach (var family in new KeyFamily?[] { null, KeyFamily.Workout, KeyFamily.HouseholdMeal })
        {
            Assert.Equal("Dentist", Kept(Keyed("Dentist", null), family).Title);
        }
    }

    [Fact]
    public void AnotherRunsEventTheOwnerMarkedFreeIsFree()
    {
        var cook = Keyed("Cook: chili", CookKey);
        cook.Transparency = "transparent";

        Assert.Equal(Availability.Free, Kept(cook, KeyFamily.Workout).Availability);
    }

    [Fact]
    public void ACancelledOrDeclinedEventIsNotTime()
    {
        var cancelled = Keyed("Cook: chili", CookKey);
        cancelled.Status = "cancelled";
        var declined = Keyed("Workout", WorkoutKey);
        declined.Attendees = [new EventAttendee { Email = CalendarId, Self = true, ResponseStatus = "declined" }];

        Assert.Equal(EventLeftOut.Cancelled, LeftOut(cancelled, KeyFamily.Workout));
        Assert.Equal(EventLeftOut.Declined, LeftOut(declined, KeyFamily.HouseholdMeal));
    }

    [Fact]
    public void ASharedPropertyIsNeverOurs()
    {
        var other = Keyed("Dentist", null);
        other.ExtendedProperties = new Event.ExtendedPropertiesData { Shared = new Dictionary<string, string> { [PlacementKey.Property] = CookKey } };

        Assert.Equal("Dentist", Kept(other, KeyFamily.Workout).Title);
        Assert.Equal("Dentist", Kept(other, KeyFamily.HouseholdMeal).Title);
    }

    [Fact]
    public void NobodyCanAskToDropTheUnknown()
    {
        // Dropping "unknown" would be dropping somebody's time; the mapper refuses the question rather than answer it.
        Assert.Throws<ArgumentOutOfRangeException>(() => GoogleEventMapper.Map(Keyed("Any", WorkoutKey), CalendarId, KeyFamily.Unknown));
    }

    // ---- the source, over the real client and a fake transport ----

    private static string Page(params string[] events) => "{\"kind\":\"calendar#events\",\"items\":[" + string.Join(",", events) + "]}";

    private static string EventJson(string id, string title, string hour, string? key) =>
        "{\"id\":\"" + id + "\",\"status\":\"confirmed\",\"summary\":\"" + title + "\",\"start\":{\"dateTime\":\"2026-10-27T" + hour + ":00:00-04:00\"},\"end\":{\"dateTime\":\"2026-10-27T" + hour + ":50:00-04:00\"}"
        + (key is null ? string.Empty : ",\"extendedProperties\":{\"private\":{\"constitutionKey\":\"" + key + "\"}}") + "}";

    private static readonly DateTimeOffset From = Edt(10, 25, 0);
    private static readonly DateTimeOffset To = Edt(11, 1, 0);

    private static async Task<List<string>> TitlesAsync(KeyFamily? family)
    {
        var http = new FakeCalendarHttp
        {
            Reply = request => Uri.UnescapeDataString(request.Path).Contains("sam@example.com", StringComparison.Ordinal)
                ? FakeCalendarHttp.Json(Page(
                    EventJson("1", "Workout", "09", WorkoutKey),
                    EventJson("2", "Cook: chili", "10", CookKey),
                    EventJson("3", "Start the slow cooker", "11", SlowKey),
                    EventJson("4", "Groceries", "12", TripKey),
                    EventJson("5", "Dentist", "13", null),
                    EventJson("6", "Strange", "14", "c1:sam:household:5:errand")))
                : null,
        };
        var source = new GoogleHouseholdCalendars(_ => Task.FromResult<CalendarService>(http.Service()), () => new HouseholdSetupRead.Configured(HouseholdData.Setup()), family);

        var snapshot = await source.ReadAsync(From, To, CancellationToken.None);

        return snapshot.OwnCalendar(Sam, From, To).Entries.Select(entry => entry.Title).ToList();
    }

    [Fact]
    public async Task WorkoutReadSeesMealsNotItsOwn()
    {
        Assert.Equal(["Cook: chili", "Start the slow cooker", "Groceries", "Dentist", "Strange"], await TitlesAsync(KeyFamily.Workout));
    }

    [Fact]
    public async Task MealReadSeesWorkoutsNotItsOwn()
    {
        Assert.Equal(["Workout", "Dentist", "Strange"], await TitlesAsync(KeyFamily.HouseholdMeal));
    }

    [Fact]
    public async Task WithNoFamilyTheReadIsWhatItWas()
    {
        Assert.Equal(["Dentist"], await TitlesAsync(null));
    }

    [Fact]
    public void ASourceCannotBeToldToDropTheUnknown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GoogleHouseholdCalendars(_ => throw new InvalidOperationException("Not reached."), () => new HouseholdSetupRead.Configured(HouseholdData.Setup()), KeyFamily.Unknown));
    }
}
