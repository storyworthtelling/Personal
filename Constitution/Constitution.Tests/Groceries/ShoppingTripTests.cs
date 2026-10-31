using Constitution.Domain.Calendar;
using Constitution.Domain.Groceries;
using Constitution.Domain.Placement;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Groceries;

/// <summary>
/// C-6.4.2, the "Groceries" trip, through the real placement engine on the invented household's calendars. The week opening on
/// Sunday 25 October 2026 is an even week number, so the trip is Sam's; the week opening on Sunday 1 November is odd, so Lee's.
/// The trip goes after approval and before the week opens, so these calendars start three days ahead of the Sunday and the
/// trip's days are the Thursday, Friday and Saturday before it.
/// </summary>
public class ShoppingTripTests
{
    private static readonly DateOnly NextSunday = new(2026, 11, 1);

    private static readonly DateTimeOffset SaturdayMorning = Edt(10, 24, 8);

    private static List<BusyTime> People(HouseholdCalendarSnapshot calendars, DateOnly weekStart) =>
        Rules().Adults.Select(adult => BusyTime.For(adult.Name, weekStart.AddDays(-3), weekStart.AddDays(6), Rules(), calendars)).ToList();

    private static CalendarEntry Away(int month, int day, int fromHour, int toHour) =>
        CalendarEntry.Timed("Away", month == 10 ? Edt(month, day, fromHour) : Est(month, day, fromHour), month == 10 ? Edt(month, day, toHour) : Est(month, day, toHour));

    private static HouseholdEventRequest Trip(HouseholdCalendarSnapshot calendars, DateTimeOffset approvedAt, DateOnly? weekStart = null, GroceryTripOptions? options = null)
    {
        var week = weekStart ?? Sunday;
        return GroceryPlacement.Request(week, Rules(), People(calendars, week), approvedAt, options)!;
    }

    private static PlacementPlan Place(HouseholdEventRequest trip, HouseholdCalendarSnapshot calendars, DateOnly weekStart) =>
        PlacementEngine.Plan(new PlacementRequest(weekStart, People(calendars, weekStart), [trip], null), PlacementFixture.Fresh);

    // ---- who shops ----

    [Fact]
    public void AlternatesUnlessBusy()
    {
        var calendars = Calendars();

        var thisWeek = Trip(calendars, SaturdayMorning);
        var nextWeek = Trip(calendars, Edt(10, 31, 8), NextSunday);

        Assert.Equal([Sam], thisWeek.People);
        Assert.Equal([Lee], nextWeek.People);
    }

    [Fact]
    public void ABusyShopperHandsItToThePartner()
    {
        var calendars = Calendars(sam: [Away(10, 24, 8, 18)]);

        var trip = Trip(calendars, SaturdayMorning);

        Assert.Equal([Lee], trip.People);
    }

    [Fact]
    public void ABusyPartnerHandsItBackAndForth()
    {
        var calendars = Calendars(lee: [Away(10, 31, 8, 18)]);

        var trip = Trip(calendars, Edt(10, 31, 8), NextSunday);

        Assert.Equal([Sam], trip.People);
    }

    [Fact]
    public void BothBusyStaysWithTheOneWhoseWeekItIs()
    {
        var calendars = Calendars(sam: [Away(10, 24, 8, 18)], lee: [Away(10, 24, 8, 18)]);

        var trip = Trip(calendars, SaturdayMorning);
        var plan = Place(trip, calendars, Sunday);

        Assert.Equal([Sam], trip.People);
        Assert.Empty(plan.ToCreate);
        Assert.Equal(UnplacedReason.NoRoom, Assert.Single(plan.Unplaced).Reason);
    }

    [Fact]
    public void AnUnreadableCalendarIsNotFreeTime()
    {
        var calendars = CalendarsWith(sam: CalendarRead.Unreadable("Sam's calendar did not answer."));

        var trip = Trip(calendars, SaturdayMorning);

        Assert.Equal([Lee], trip.People);
    }

    [Fact]
    public void AnotherDayBeforeTheWeekBeatsSwitching()
    {
        var calendars = Calendars(sam: [Away(10, 24, 8, 18)]);

        var trip = Trip(calendars, Edt(10, 22, 12));

        Assert.Equal([Sam], trip.People);
        var plan = Place(trip, calendars, Sunday);
        var made = Assert.Single(plan.ToCreate);
        // Friday is a work day for Sam, so the first free hour of it is the one after his shift ends at four.
        Assert.Equal((Edt(10, 23, 16), Edt(10, 23, 17)), (made.Start, made.End));
    }

    // ---- when ----

    [Fact]
    public void TheTripGoesAfterApprovalAndBeforeTheWeek()
    {
        var trip = Trip(Calendars(), Edt(10, 22, 12));

        Assert.All(trip.Windows, window =>
        {
            Assert.True(window.Start >= Edt(10, 22, 12));
            Assert.True(window.End <= Edt(10, 25, 0));
        });
        Assert.Equal(
            [(Edt(10, 24, 9), Edt(10, 24, 17)), (Edt(10, 23, 9), Edt(10, 23, 17)), (Edt(10, 22, 12), Edt(10, 22, 17))],
            trip.Windows.Select(window => (window.Start, window.End)).ToList());
    }

    [Fact]
    public void ALateApprovalLeavesOnlyWhatIsStillAhead()
    {
        var trip = Trip(Calendars(), Edt(10, 24, 11));

        var only = Assert.Single(trip.Windows);
        Assert.Equal((Edt(10, 24, 11), Edt(10, 24, 17)), (only.Start, only.End));
    }

    [Fact]
    public void ATripNeverReachesBackMoreThanThreeDays()
    {
        var trip = Trip(Calendars(), Edt(10, 15, 8));

        Assert.Equal(Edt(10, 22, 9), trip.Windows.Min(window => window.Start));
    }

    [Fact]
    public void ApprovalWithNoDaytimeLeftAsksForNothing()
    {
        Assert.Null(GroceryPlacement.Request(Sunday, Rules(), People(Calendars(), Sunday), Edt(10, 24, 17, 30)));
        Assert.Null(GroceryPlacement.Request(Sunday, Rules(), People(Calendars(), Sunday), Edt(10, 25, 7)));
    }

    [Fact]
    public void TheEngineLandsItAtNineOnTheSaturday()
    {
        var calendars = Calendars();

        var plan = Place(Trip(calendars, SaturdayMorning), calendars, Sunday);

        var made = Assert.Single(plan.ToCreate);
        Assert.Equal(("Groceries", Sam), (made.Title, made.Person));
        Assert.Equal((Edt(10, 24, 9), Edt(10, 24, 10)), (made.Start, made.End));
        Assert.Empty(plan.Unplaced);
    }

    [Fact]
    public void TheEngineWorksAroundWhatIsOnTheDay()
    {
        var calendars = Calendars(sam: [Away(10, 24, 9, 12)]);

        var plan = Place(Trip(calendars, SaturdayMorning), calendars, Sunday);

        var made = Assert.Single(plan.ToCreate);
        Assert.Equal(Edt(10, 24, 12), made.Start);
    }

    [Fact]
    public void TheTripTakesTheLengthItIsGiven()
    {
        var calendars = Calendars();

        var plan = Place(Trip(calendars, SaturdayMorning, options: new GroceryTripOptions { Length = TimeSpan.FromMinutes(90) }), calendars, Sunday);

        var made = Assert.Single(plan.ToCreate);
        Assert.Equal(TimeSpan.FromMinutes(90), made.End - made.Start);
    }

    // ---- what it is called ----

    [Fact]
    public void TheTitleIsPlainGroceries()
    {
        var trip = Trip(Calendars(), SaturdayMorning);

        Assert.Equal("Groceries", trip.Title);
    }

    [Fact]
    public void TheKeyHoldsNoDate()
    {
        var trip = Trip(Calendars(), SaturdayMorning);

        var key = PlacementKey.ForHousehold(Sam, PlacementKey.WeekIndexOf(Sunday), trip.Identity);

        Assert.DoesNotContain("2026", key.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("10-2", trip.Identity, StringComparison.Ordinal);
        Assert.Equal(key, PlacementKey.ForHousehold(Sam, PlacementKey.WeekIndexOf(Sunday), Trip(Calendars(), Edt(10, 23, 9)).Identity));
    }

    [Fact]
    public void TheIdentityIsFrozen()
    {
        Assert.Equal("groceries", Trip(Calendars(), SaturdayMorning).Identity);
    }

    [Fact]
    public void AMovedTripIsTheSameEventNextRun()
    {
        var first = Trip(Calendars(), Edt(10, 23, 8));
        var second = Trip(Calendars(sam: [Away(10, 24, 8, 18)]), Edt(10, 24, 8));

        Assert.Equal(first.Identity, second.Identity);
    }
}
