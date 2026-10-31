using Constitution.Domain.Calendar;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// Freeing time is the unsafe direction (R7): a day shown free that is not is a workout on top of someone's life (review finding
/// N1). Only a person's own calendar, with a clear trip word standing alone, may free anything; the family calendar never does,
/// and a hotel's name is all that is ever sent to the places lookup.
/// </summary>
public class TripsFreeingTests
{
    private static TripRead Detect(HouseholdCalendarSnapshot calendars) =>
        TripDetector.Detect(Rules(), calendars, WeekStart, WeekStart.AddDays(6));

    private static IReadOnlyList<CalendarEntry> Own(HouseholdCalendarSnapshot inner, string person = Sam) =>
        new TripAwareCalendars(inner, Rules()).OwnCalendar(person, At(0, 0), At(7, 0)).Entries;

    private static IReadOnlyList<CalendarEntry> Family(HouseholdCalendarSnapshot inner) =>
        new TripAwareCalendars(inner, Rules()).FamilyCalendar(At(0, 0), At(7, 0)).Entries;

    // ---- the family calendar frees nothing ----

    [Fact]
    public void ASummitSoccerCampIsNotATrip()
    {
        var calendars = Calendars(family: [TripsFixtures.AllDay("Summit Soccer Camp", 2, 4)]);

        Assert.Empty(Detect(calendars).For(Sam));
        Assert.Empty(Detect(calendars).For(Lee));
        Assert.Equal(Availability.Busy, Assert.Single(Family(calendars)).Availability);
    }

    [Fact]
    public void ASummitOnAnOwnCalendarIsNotFreedEither()
    {
        var calendars = Calendars(sam: [TripsFixtures.AllDay("Summit", 2, 4)]);

        Assert.Equal(Availability.Busy, Assert.Single(Own(calendars)).Availability);
    }

    [Fact]
    public void AWorkTripOnTheFamilyCalendarIsNeverFreed()
    {
        // Everyone reads the family calendar, so what it frees it frees for both adults.
        var calendars = Calendars(family: [TripsFixtures.AllDay("Sam at the conference", 2, 4)]);

        Assert.Equal(Availability.Busy, Assert.Single(Family(calendars)).Availability);
        Assert.Equal(TripKind.Work, Assert.Single(Detect(calendars).For(Sam)).Kind);
    }

    [Fact]
    public void AnUnnamedWorkWordOnTheFamilyCalendarIsNotOneAdultsWorkTrip()
    {
        var calendars = Calendars(family: [TripsFixtures.AllDay("Conference", 2, 4)]);

        var read = Detect(calendars);

        Assert.Equal(TripKind.FamilyHoliday, Assert.Single(read.For(Sam)).Kind);
        Assert.Equal(TripKind.FamilyHoliday, Assert.Single(read.For(Lee)).Kind);
        Assert.Equal(Availability.Busy, Assert.Single(Family(calendars)).Availability);
    }

    [Fact]
    public void AnAllDayHotelBookingOnTheFamilyCalendarIsNotFreed()
    {
        var calendars = Calendars(family: [TripsFixtures.AllDay("Juniper Hotel", 2, 4)]);

        Assert.Equal(Availability.Busy, Assert.Single(Family(calendars)).Availability);
    }

    [Fact]
    public void AnAllDayHotelBookingOnAnOwnCalendarIsStillFreed()
    {
        var calendars = Calendars(sam: [TripsFixtures.AllDay("Juniper Hotel", 2, 4)]);

        Assert.Equal(Availability.Free, Assert.Single(Own(calendars)).Availability);
    }

    // ---- a clear word, on an own calendar ----

    [Fact]
    public void ABareTripWordIsATripButNotFreed()
    {
        // "Trip to Denver" may be a school outing the owner chaperones: the plan treats it as a trip, but the day stays taken.
        var calendars = Calendars(sam: [TripsFixtures.AllDay("Trip to Denver", 2, 4)]);

        Assert.Equal(TripKind.Work, Assert.Single(Detect(calendars).For(Sam)).Kind);
        Assert.Equal(Availability.Busy, Assert.Single(Own(calendars)).Availability);
    }

    [Theory]
    [InlineData("Conference trip")]
    [InlineData("Business trip")]
    [InlineData("Sales offsite")]
    [InlineData("Work travel")]
    [InlineData("Client visit")]
    public void AClearWorkWordOnAnOwnCalendarIsFreed(string title)
    {
        var calendars = Calendars(sam: [TripsFixtures.AllDay(title, 2, 4)]);

        Assert.Equal(Availability.Free, Assert.Single(Own(calendars)).Availability);
    }

    [Fact]
    public void AWorkWordInsideAnotherWordIsNotAWorkWord()
    {
        var calendars = Calendars(sam: [TripsFixtures.AllDay("Conferences list", 2, 4)]);

        Assert.Empty(Detect(calendars).For(Sam));
        Assert.Equal(Availability.Busy, Assert.Single(Own(calendars)).Availability);
    }

    // ---- what the places lookup is asked ----

    private static async Task<List<string>> AskedWith(params CalendarEntry[] sam)
    {
        var read = Detect(Calendars(sam: [TripsFixtures.AllDay("Conference trip", 2, 4), .. sam]));
        var lookup = TripsFixtures.NoGym();
        await TravelPlan.ResolveAsync(read, lookup, CancellationToken.None);
        return lookup.Asked;
    }

    [Theory]
    [InlineData("Check in: Juniper Hotel", "Juniper Hotel")]
    [InlineData("Juniper Hotel, confirmation 88123", "Juniper Hotel")]
    [InlineData("Juniper Hotel (room 214)", "Juniper Hotel")]
    [InlineData("Sam and Lee dinner at Juniper Hotel", "Juniper Hotel")]
    [InlineData("Stay at the Lakeside Resort", "Lakeside Resort")]
    [InlineData("Holiday Inn Express", "Holiday Inn Express")]
    [InlineData("Hotel Example", "Hotel Example")]
    [InlineData("Sam Juniper Hotel", "Juniper Hotel")]
    [InlineData("Robin Lodge", "Robin Lodge")]
    public async Task TheLookupIsAskedAboutTheHotelPhraseOnly(string title, string phrase)
    {
        var asked = await AskedWith(TripsFixtures.AllDay(title, 2, 4));

        Assert.Equal([phrase], asked);
    }

    [Fact]
    public async Task NothingElseInTheTitleReachesTheLookup()
    {
        var asked = await AskedWith(CalendarEntry.Timed("Lee's birthday dinner: Juniper Hotel, table for 8, ref 88123", At(3, 18), At(3, 20)));

        var sent = Assert.Single(asked);
        Assert.DoesNotContain("birthday", sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("88123", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("Lee", sent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTripCarriesThePhraseAndTheGymIsFoundByIt()
    {
        var read = Detect(Calendars(sam: [TripsFixtures.AllDay("Conference trip", 2, 4), TripsFixtures.AllDay("Check in: Juniper Hotel", 2, 4)]));

        var trip = Assert.Single(read.For(Sam));

        Assert.Equal("Juniper Hotel", trip.Hotel?.Name);
    }
}
