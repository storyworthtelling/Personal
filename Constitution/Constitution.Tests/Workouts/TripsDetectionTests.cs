using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// Reading trips out of the calendars: which entries are trips, whose, what kind, and which hotel they name. Only a stay of
/// whole days is a trip; a title decides the kind; who is asked is a matter of whose calendar it is on and who it names.
/// </summary>
public class TripDetectionTests
{
    private static TripRead Detect(HouseholdCalendarSnapshot calendars) =>
        TripDetector.Detect(Rules(), calendars, WeekStart, WeekStart.AddDays(6));

    private static CalendarEntry Away(string title, int first = 2, int last = 4, Availability availability = Availability.Busy, IReadOnlyList<string>? guests = null) =>
        TripsFixtures.AllDay(title, first, last, availability, guests);

    // ---- the kind ----

    public static TheoryData<string, TripKind> OwnCalendarTitles => new()
    {
        { "Conference trip", TripKind.Work },
        { "Business trip", TripKind.Work },
        { "Sales offsite", TripKind.Work },
        { "Client visit travel", TripKind.Work },
        { "Trip to Denver", TripKind.Work },
        { "Travel", TripKind.Work },
        { "Family holiday", TripKind.FamilyHoliday },
        { "Beach vacation", TripKind.FamilyHoliday },
        { "Camping", TripKind.FamilyHoliday },
        { "Cruise", TripKind.FamilyHoliday },
        { "Getaway weekend", TripKind.FamilyHoliday },
    };

    [Theory]
    [MemberData(nameof(OwnCalendarTitles))]
    public void TheTitleDecidesTheKind(string title, TripKind expected)
    {
        var read = Detect(Calendars(sam: [Away(title)]));

        var trip = Assert.Single(read.For(Sam));
        Assert.Equal(expected, trip.Kind);
        Assert.Equal(Day(2), trip.FirstDay);
        Assert.Equal(Day(4), trip.LastDay);
    }

    [Fact]
    public void ATripOnOnePersonsCalendarIsOnlyTheirs()
    {
        var read = Detect(Calendars(sam: [Away("Conference trip")]));

        Assert.Single(read.For(Sam));
        Assert.Empty(read.For(Lee));
    }

    [Fact]
    public void AnEntryOnTheFamilyCalendarIsEveryonesHoliday()
    {
        var read = Detect(Calendars(family: [Away("Trip to the lake")]));

        Assert.Equal(TripKind.FamilyHoliday, Assert.Single(read.For(Sam)).Kind);
        Assert.Equal(TripKind.FamilyHoliday, Assert.Single(read.For(Lee)).Kind);
    }

    [Fact]
    public void AFamilyEntryNamingOnePersonIsTheirWorkTrip()
    {
        var read = Detect(Calendars(family: [Away("Sam at the conference")]));

        Assert.Equal(TripKind.Work, Assert.Single(read.For(Sam)).Kind);
        Assert.Empty(read.For(Lee));
    }

    [Fact]
    public void AGenericTripNamingBothIsAFamilyOne()
    {
        var read = Detect(Calendars(sam: [Away("Trip with Lee")]));

        Assert.Equal(TripKind.FamilyHoliday, Assert.Single(read.For(Sam)).Kind);
        Assert.Equal(TripKind.FamilyHoliday, Assert.Single(read.For(Lee)).Kind);
    }

    [Fact]
    public void AnInvitedAddressNamesWhoGoes()
    {
        var read = Detect(Calendars(sam: [Away("Trip", guests: [LeeAddress])]));

        Assert.Equal(TripKind.FamilyHoliday, Assert.Single(read.For(Lee)).Kind);
    }

    [Fact]
    public void AHolidayWordOnOnePersonsCalendarPausesThem()
    {
        var read = Detect(Calendars(sam: [Away("Vacation")]));

        Assert.Equal(TripKind.FamilyHoliday, Assert.Single(read.For(Sam)).Kind);
        Assert.Empty(read.For(Lee));
    }

    [Fact]
    public void AHolidayWordBeatsABusinessOne()
    {
        Assert.Equal(TripKind.Work, Assert.Single(Detect(Calendars(sam: [Away("Conference trip")])).For(Sam)).Kind);
        Assert.Equal(TripKind.FamilyHoliday, Assert.Single(Detect(Calendars(sam: [Away("Conference holiday")])).For(Sam)).Kind);
    }

    // ---- what is not a trip ----

    [Theory]
    [InlineData("Dentist")]
    [InlineData("Tripod delivery")]
    [InlineData("Strip mall errands")]
    [InlineData("Birthday party")]
    [InlineData("")]
    public void OtherTitlesAreNotTrips(string title) =>
        Assert.Empty(Detect(Calendars(sam: [Away(title)])).For(Sam));

    [Fact]
    public void ATimedEntryIsNotATrip()
    {
        var timed = CalendarEntry.Timed("Conference trip", At(2, 9), At(4, 17));

        Assert.Empty(Detect(Calendars(sam: [timed])).For(Sam));
    }

    [Fact]
    public void AHotelBookingIsNotAHolidayBecauseItsNameSaysSo()
    {
        var read = Detect(Calendars(sam: [Away("Holiday Inn Express")]));

        Assert.Empty(read.For(Sam));
    }

    [Fact]
    public void ADayOffOnItsOwnIsNotATrip() =>
        Assert.Empty(Detect(Calendars(sam: [Away("PTO"), Away("Day off")])).For(Sam));

    [Fact]
    public void ADeclinedTripIsTheHeadsToLeaveOut()
    {
        // Free entries count: Google's all-day events default to free, and a holiday marked free is still a holiday.
        var read = Detect(Calendars(sam: [Away("Conference trip", availability: Availability.Free)]));

        Assert.Single(read.For(Sam));
    }

    // ---- the days ----

    [Fact]
    public void ALastDayIsTheLastDayItself()
    {
        var one = Assert.Single(Detect(Calendars(sam: [Away("Conference", 3, 3)])).For(Sam));

        Assert.Equal(Day(3), one.FirstDay);
        Assert.Equal(Day(3), one.LastDay);
        Assert.True(one.Covers(Day(3)));
        Assert.False(one.Covers(Day(2)));
        Assert.False(one.Covers(Day(4)));
    }

    [Fact]
    public void ATripBeginningBeforeTheWeekIsKeptWhole()
    {
        var trip = Assert.Single(Detect(Calendars(sam: [Away("Conference trip", -2, 1)])).For(Sam));

        Assert.Equal(Day(-2), trip.FirstDay);
        Assert.Equal(Day(1), trip.LastDay);
    }

    [Fact]
    public void ATripAfterTheWeekIsNotThisWeeks()
    {
        Assert.Empty(Detect(Calendars(sam: [Away("Conference trip", 8, 10)])).For(Sam));
    }

    [Fact]
    public void TheSameTripOnTwoCalendarsIsOneTrip()
    {
        var read = Detect(Calendars(sam: [Away("Family holiday")], lee: [Away("Family holiday")], family: [Away("Family holiday")]));

        Assert.Single(read.For(Sam));
        Assert.Single(read.For(Lee));
    }

    [Fact]
    public void TripsComeEarliestFirst()
    {
        var read = Detect(Calendars(sam: [Away("Conference trip", 4, 4), Away("Business trip", 0, 1)]));

        Assert.Equal([Day(0), Day(4)], read.For(Sam).Select(trip => trip.FirstDay).ToArray());
    }

    // ---- hotels ----

    [Fact]
    public void ACalendarThatNamesAHotelGivesItsName()
    {
        var read = Detect(Calendars(sam: [Away("Conference trip"), Away("Juniper Hotel")]));

        Assert.Equal("Juniper Hotel", Assert.Single(read.For(Sam)).Hotel?.Name);
    }

    [Theory]
    [InlineData("Juniper Inn")]
    [InlineData("Lakeside Resort")]
    [InlineData("Robin Lodge")]
    [InlineData("Hotel Example")]
    [InlineData("Motel stay")]
    public void TheHotelWordsAreFound(string title) =>
        Assert.NotNull(Assert.Single(Detect(Calendars(sam: [Away("Conference trip"), Away(title)])).For(Sam)).Hotel);

    [Fact]
    public void ATimedBookingOnATripDayNamesAHotelToo()
    {
        var booking = CalendarEntry.Timed("Check in: Juniper Hotel", At(2, 15), At(2, 16));

        var trip = Assert.Single(Detect(Calendars(sam: [Away("Conference trip"), booking])).For(Sam));

        // Only the hotel phrase leaves for the places lookup, not the booking title (review finding N1).
        Assert.Equal("Juniper Hotel", trip.Hotel?.Name);
    }

    [Fact]
    public void AHotelNamedOutsideTheTripDaysIsNotTheTripsHotel()
    {
        var read = Detect(Calendars(sam: [Away("Conference trip", 2, 3), Away("Juniper Hotel", 5, 5)]));

        Assert.Null(Assert.Single(read.For(Sam)).Hotel);
    }

    [Fact]
    public void AHotelOnTheFamilyCalendarCounts()
    {
        var read = Detect(Calendars(sam: [Away("Conference trip")], family: [Away("Juniper Hotel")]));

        Assert.NotNull(Assert.Single(read.For(Sam)).Hotel);
    }

    [Fact]
    public void WordsAreWholeWords()
    {
        var read = Detect(Calendars(sam: [Away("Conference trip"), Away("Dinner with Lee")]));

        Assert.Null(Assert.Single(read.For(Sam)).Hotel);
    }

    [Fact]
    public void NoHotelMeansNoHotel() =>
        Assert.Null(Assert.Single(Detect(Calendars(sam: [Away("Conference trip")])).For(Sam)).Hotel);

    [Fact]
    public void AHolidayHasNoNeedOfOne() =>
        Assert.Null(Assert.Single(Detect(Calendars(sam: [Away("Beach vacation"), Away("Juniper Hotel")])).For(Sam)).Hotel);

    // ---- unreadable calendars ----

    [Fact]
    public void AnUnreadableCalendarIsSaidAndTheRestIsStillRead()
    {
        var calendars = CalendarsWith(
            sam: CalendarRead.Of([Away("Conference trip")]),
            family: CalendarRead.Unreadable("The family calendar did not answer."));

        var read = Detect(calendars);

        Assert.False(read.IsComplete);
        Assert.Equal(CalendarSource.Family, Assert.Single(read.Unreadable).Source);
        Assert.Single(read.For(Sam));
    }

    [Fact]
    public void ReadableCalendarsMakeACompleteRead() =>
        Assert.True(Detect(Calendars()).IsComplete);

    [Fact]
    public void AnAdultNobodyKnowsHasNoTrips() =>
        Assert.Empty(Detect(Calendars(sam: [Away("Conference trip")])).For("Nobody"));

    [Fact]
    public void ThePersonIsMatchedWithoutCase() =>
        Assert.Single(Detect(Calendars(sam: [Away("Conference trip")])).For("  sam "));

    [Fact]
    public void ARunBackwardsIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TripDetector.Detect(Rules(), Calendars(), WeekStart.AddDays(6), WeekStart));
}

/// <summary>
/// The calendars the planner is shown on a trip: a work trip's all-day entry stops taking the whole day (a real meeting
/// on it still does), a holiday's entry takes it even if the calendar marked it free.
/// </summary>
public class TripCalendarsTests
{
    private static IReadOnlyList<CalendarEntry> Own(HouseholdCalendarSnapshot inner, string person = Sam) =>
        new TripAwareCalendars(inner, Rules()).OwnCalendar(person, At(0, 0), At(7, 0)).Entries;

    private static IReadOnlyList<CalendarEntry> Family(HouseholdCalendarSnapshot inner) =>
        new TripAwareCalendars(inner, Rules()).FamilyCalendar(At(0, 0), At(7, 0)).Entries;

    [Fact]
    public void AWorkTripStopsTakingTheDay()
    {
        var entries = Own(Calendars(sam: [TripsFixtures.AllDay("Conference trip", 2, 4)]));

        var trip = Assert.Single(entries);
        Assert.Equal(Availability.Free, trip.Availability);
        Assert.True(trip.IsAllDay);
        Assert.Equal(Day(2), trip.FirstDay);
        Assert.Equal(Day(5), trip.EndDayExclusive);
        Assert.Equal("Conference trip", trip.Title);
    }

    [Fact]
    public void AMeetingStaysBusy()
    {
        var meeting = CalendarEntry.Timed("Client dinner", At(3, 16), At(3, 18));
        var allDay = TripsFixtures.AllDay("Dentist", 3, 3);

        var entries = Own(Calendars(sam: [meeting, allDay]));

        Assert.All(entries, entry => Assert.Equal(Availability.Busy, entry.Availability));
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void AHolidayOnAnOwnCalendarTakesTheDayEvenIfMarkedFree()
    {
        var entries = Own(Calendars(sam: [TripsFixtures.AllDay("Beach vacation", 2, 4, Availability.Free)]));

        Assert.Equal(Availability.Busy, Assert.Single(entries).Availability);
    }

    [Fact]
    public void AHolidayForEveryoneOnTheFamilyCalendarTakesTheDay()
    {
        var entries = Family(Calendars(family: [TripsFixtures.AllDay("Family holiday", 2, 4, Availability.Free)]));

        Assert.Equal(Availability.Busy, Assert.Single(entries).Availability);
    }

    [Fact]
    public void AWorkTripOnTheFamilyCalendarKeepsTakingTheDay()
    {
        // Corrected by review finding N1: the family calendar is read for both adults, so it frees nothing (see TripsFreeingTests).
        var entries = Family(Calendars(family: [TripsFixtures.AllDay("Sam at the conference", 2, 4)]));

        Assert.Equal(Availability.Busy, Assert.Single(entries).Availability);
    }

    [Fact]
    public void AFamilyEntryNamingOnePersonIsLeftAsItWasWhenAHoliday()
    {
        var entries = Family(Calendars(family: [TripsFixtures.AllDay("Lee's beach vacation", 2, 4, Availability.Free)]));

        Assert.Equal(Availability.Free, Assert.Single(entries).Availability);
    }

    [Fact]
    public void GuestsSurviveTheChange()
    {
        var entry = TripsFixtures.AllDay("Conference trip", 2, 4, guests: [LeeAddress]);

        var changed = Assert.Single(Own(Calendars(sam: [entry])));

        Assert.Equal([LeeAddress], changed.Guests.ToArray());
    }

    [Fact]
    public void AnUnreadableCalendarStaysUnreadable()
    {
        var inner = CalendarsWith(sam: CalendarRead.Unreadable("Not shared yet."));
        var aware = new TripAwareCalendars(inner, Rules());

        var read = aware.OwnCalendar(Sam, At(0, 0), At(7, 0));

        Assert.False(read.IsReadable);
        Assert.Equal("Not shared yet.", read.UnreadableBecause);
    }

    [Fact]
    public void TheChildrensCalendarsAreNotTouched()
    {
        var entry = TripsFixtures.AllDay("Camping", 2, 4);
        var inner = Calendars(children: [entry]);
        var aware = new TripAwareCalendars(inner, Rules());

        Assert.Same(entry, Assert.Single(aware.ChildrensCalendars(At(0, 0), At(7, 0)).Entries));
    }

    [Fact]
    public void TheDecoratorNeedsWhatItWraps()
    {
        Assert.Throws<ArgumentNullException>(() => new TripAwareCalendars(null!, Rules()));
        Assert.Throws<ArgumentNullException>(() => new TripAwareCalendars(Calendars(), null!));
    }
}

/// <summary>The trips with their gyms: who is away on a day, which trip wins, and what the lookup is asked.</summary>
public class TravelPlanTests
{
    private static TripRead ReadOf(params (string Person, Trip Trip)[] trips) =>
        new(
            trips.GroupBy(item => item.Person).ToDictionary(group => group.Key, group => (IReadOnlyList<Trip>)group.Select(item => item.Trip).ToList()),
            []);

    private static Trip Work(int first, int last, string? hotel = null) =>
        new(TripKind.Work, Day(first), Day(last), hotel is null ? null : new HotelStay(hotel));

    private static Trip Holiday(int first, int last) => new(TripKind.FamilyHoliday, Day(first), Day(last), null);

    [Fact]
    public void AnAdultIsAwayOnTheTripDaysOnly()
    {
        var plan = TravelPlan.Of(ReadOf((Sam, Work(2, 4))));

        Assert.True(plan.IsAway(Sam, Day(2)));
        Assert.True(plan.IsAway(Sam, Day(4)));
        Assert.False(plan.IsAway(Sam, Day(1)));
        Assert.False(plan.IsAway(Sam, Day(5)));
        Assert.False(plan.IsAway(Lee, Day(3)));
    }

    [Fact]
    public void AHolidayOutranksATripOnTheSameDay()
    {
        var plan = TravelPlan.Of(ReadOf((Sam, Work(2, 4)), (Sam, Holiday(3, 3))));

        Assert.Equal(TripKind.Work, plan.On(Sam, Day(2))!.Kind);
        Assert.Equal(TripKind.FamilyHoliday, plan.On(Sam, Day(3))!.Kind);
    }

    [Fact]
    public void NoTravelMeansNobodyIsAway()
    {
        Assert.False(TravelPlan.None.IsAway(Sam, Day(0)));
        Assert.Null(TravelPlan.None.On(Lee, Day(0)));
    }

    [Fact]
    public async Task OnlyAWorkTripsHotelIsLookedUp()
    {
        var lookup = TripsFixtures.GymFound();
        var read = ReadOf((Sam, Work(2, 4, "Juniper Hotel")), (Lee, new Trip(TripKind.FamilyHoliday, Day(2), Day(4), new HotelStay("Lakeside Resort"))));

        await TravelPlan.ResolveAsync(read, lookup, CancellationToken.None);

        Assert.Equal(["Juniper Hotel"], lookup.Asked);
    }

    [Fact]
    public async Task OneHotelIsAskedAboutOnce()
    {
        var lookup = TripsFixtures.GymFound();
        var read = ReadOf((Sam, Work(2, 4, "Juniper Hotel")), (Lee, Work(3, 5, "juniper hotel")));

        await TravelPlan.ResolveAsync(read, lookup, CancellationToken.None);

        Assert.Single(lookup.Asked);
    }

    [Fact]
    public async Task OnlyAFoundGymIsAGym()
    {
        var trip = Work(2, 4, "Juniper Hotel");
        var read = ReadOf((Sam, trip));

        var found = await TravelPlan.ResolveAsync(read, TripsFixtures.GymFound(), CancellationToken.None);
        var none = await TravelPlan.ResolveAsync(read, TripsFixtures.NoGym(), CancellationToken.None);
        var unknown = await TravelPlan.ResolveAsync(read, new FakeHotelLookup(hotel => new HotelGymResult.Unavailable()), CancellationToken.None);

        Assert.True(found.GymFor(trip)?.Has(EquipmentCatalogue.TreadmillId));
        Assert.Null(none.GymFor(trip));
        Assert.Null(unknown.GymFor(trip));
    }

    [Fact]
    public async Task ATripWithNoHotelHasNoGym()
    {
        var trip = Work(2, 4);

        var plan = await TravelPlan.ResolveAsync(ReadOf((Sam, trip)), TripsFixtures.GymFound(), CancellationToken.None);

        Assert.Null(plan.GymFor(trip));
    }

    private sealed class Broken : IHotelGymLookup
    {
        public Task<HotelGymResult> FindAsync(HotelStay hotel, CancellationToken cancellation) =>
            throw new InvalidOperationException("The lookup is broken.");
    }

    [Fact]
    public async Task ALookupThatBreaksIsNotHidden()
    {
        var read = ReadOf((Sam, Work(2, 4, "Juniper Hotel")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => TravelPlan.ResolveAsync(read, new Broken(), CancellationToken.None));
    }
}
