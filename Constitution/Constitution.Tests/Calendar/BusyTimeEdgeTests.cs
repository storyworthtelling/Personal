using Constitution.Domain.Calendar;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar;

/// <summary>
/// The edge cases behind C-5.3.1: how intervals merge, what a midnight does to them, what an
/// all-day entry covers, what the clock changes do, and what the answer says when a calendar could
/// not be read. These are where a planner goes wrong without anything looking wrong.
/// </summary>
public class BusyTimeEdgeTests
{
    private static BusyTime Day(string person, DateOnly day, HouseholdCalendarSnapshot calendars, PlanningRules? rules = null) =>
        BusyTime.ForDay(person, day, rules ?? Rules(), calendars);

    // ---- merging ----

    [Fact]
    public void OverlappingEntriesMerge()
    {
        var calendars = Calendars(
            sam: [OnMonday("Dentist", 10, 12)],
            family: [OnMonday("Plumber", 11, 13)]);

        var busy = Day(Sam, Monday, calendars).Busy;

        Assert.Equal(
            [new BusyInterval(Edt(10, 26, 10), Edt(10, 26, 13), BusyReason.OwnCalendar | BusyReason.FamilyCalendar)],
            busy);
    }

    [Fact]
    public void TouchingEntriesMerge()
    {
        var calendars = Calendars(sam: [OnMonday("First", 10, 11), OnMonday("Second", 11, 12)]);

        Assert.Equal(
            [new BusyInterval(Edt(10, 26, 10), Edt(10, 26, 12), BusyReason.OwnCalendar)],
            Day(Sam, Monday, calendars).Busy);
    }

    [Fact]
    public void AnEntryInsideAnotherDoesNotShrinkIt()
    {
        var calendars = Calendars(sam: [OnMonday("Workshop", 9, 17), OnMonday("Call", 11, 12)]);

        Assert.Equal(
            [new BusyInterval(Edt(10, 26, 9), Edt(10, 26, 17), BusyReason.OwnCalendar)],
            Day(Sam, Monday, calendars).Busy);
    }

    [Fact]
    public void EntriesWithAGapStaySeparate()
    {
        var calendars = Calendars(sam: [OnMonday("First", 10, 11), OnMonday("Second", 12, 13)]);

        Assert.Equal(2, Day(Sam, Monday, calendars).Busy.Count);
    }

    [Fact]
    public void TheSameEventOnTwoCalendarsIsOneInterval()
    {
        var calendars = Calendars(sam: [OnMonday("Recital", 15, 16)], family: [OnMonday("Recital", 15, 16)]);

        var busy = Day(Sam, Monday, calendars).Busy;

        Assert.Equal(
            [new BusyInterval(Edt(10, 26, 15), Edt(10, 26, 16), BusyReason.OwnCalendar | BusyReason.FamilyCalendar)],
            busy);
    }

    [Fact]
    public void EntriesComeBackInTimeOrder()
    {
        var calendars = Calendars(sam: [OnMonday("Late", 14, 15), OnMonday("Early", 9, 10), OnMonday("Middle", 11, 12)]);

        var starts = Day(Sam, Monday, calendars).Busy.Select(interval => interval.Start).ToList();

        Assert.Equal([Edt(10, 26, 9), Edt(10, 26, 11), Edt(10, 26, 14)], starts);
    }

    // ---- midnight ----

    [Fact]
    public void AMidnightCrosserIsOneIntervalInAWeek()
    {
        var overnight = CalendarEntry.Timed("Night shift swap", Edt(10, 25, 22), Edt(10, 26, 2));
        var week = BusyTime.For(Sam, new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26), Rules(), Calendars(sam: [overnight]));

        Assert.Equal(
            [new BusyInterval(Edt(10, 25, 22), Edt(10, 26, 2), BusyReason.OwnCalendar)],
            week.Busy);
    }

    [Fact]
    public void ADayQueryClipsAtMidnight()
    {
        var overnight = CalendarEntry.Timed("Night shift swap", Edt(10, 25, 22), Edt(10, 26, 2));
        var calendars = Calendars(sam: [overnight]);

        // Sunday sees the evening part, Monday the morning part; neither sees the other's.
        Assert.Equal(
            [new BusyInterval(Edt(10, 25, 22), Edt(10, 26, 0), BusyReason.OwnCalendar)],
            Day(Sam, new DateOnly(2026, 10, 25), calendars).Busy);
        Assert.Equal(
            [new BusyInterval(Edt(10, 26, 0), Edt(10, 26, 2), BusyReason.OwnCalendar)],
            Day(Sam, Monday, calendars).Busy);
    }

    [Fact]
    public void AnEntryEndingAtMidnightStaysInItsDay()
    {
        var calendars = Calendars(sam: [CalendarEntry.Timed("Late film", Edt(10, 25, 22), Edt(10, 26, 0))]);

        Assert.Empty(Day(Sam, Monday, calendars).Busy);
    }

    [Fact]
    public void AnEntryOutsideTheWindowIsLeftOut()
    {
        var calendars = Calendars(sam: [CalendarEntry.Timed("Last week", Edt(10, 19, 10), Edt(10, 19, 11)), OnMonday("Today", 10, 11)]);

        Assert.Single(Day(Sam, Monday, calendars).Busy);
    }

    // ---- all-day entries ----

    [Fact]
    public void AnAllDayEntryBlocksTheWholeLocalDay()
    {
        var calendars = Calendars(sam: [CalendarEntry.AllDay("Conference", Monday, Monday.AddDays(1))]);

        Assert.Equal(
            [new BusyInterval(Edt(10, 26, 0), Edt(10, 27, 0), BusyReason.OwnCalendar)],
            Day(Sam, Monday, calendars).Busy);
    }

    [Fact]
    public void AnAllDayEntryStartsAtLocalMidnightMidWeek()
    {
        // In a week, so its start is inside the window and is not hidden by clipping: it opens at
        // midnight on the clock in the household's zone, not at midnight UTC.
        var wednesday = Monday.AddDays(2);
        var calendars = Calendars(sam: [CalendarEntry.AllDay("Conference", wednesday, wednesday.AddDays(1))]);

        var week = BusyTime.For(Sam, Monday, Monday.AddDays(6), Rules(), calendars);

        Assert.Equal(
            [new BusyInterval(Edt(10, 28, 0), Edt(10, 29, 0), BusyReason.OwnCalendar)],
            week.Busy);
    }

    [Fact]
    public void AllDayEndIsExclusive()
    {
        // Google writes an all-day Monday-to-Wednesday event as ending on Thursday. Thursday is
        // not blocked.
        var calendars = Calendars(sam: [CalendarEntry.AllDay("Trip", Monday, Monday.AddDays(3))]);
        var thursday = Monday.AddDays(3);

        Assert.Equal(
            [new BusyInterval(Edt(10, 26, 0), Edt(10, 29, 0), BusyReason.OwnCalendar)],
            BusyTime.For(Sam, Monday, thursday, Rules(), calendars).Busy);
        Assert.Empty(Day(Sam, thursday, calendars).Busy);
    }

    [Fact]
    public void AFreeAllDayEntryBlocksNothing()
    {
        var calendars = Calendars(family: [CalendarEntry.AllDay("Teacher workday", Monday, Monday.AddDays(1), Availability.Free)]);

        Assert.Empty(Day(Sam, Monday, calendars).Busy);
    }

    [Fact]
    public void AllDaySpringForwardIsTwentyThreeHours()
    {
        var sunday = new DateOnly(2026, 3, 8);
        var calendars = Calendars(sam: [CalendarEntry.AllDay("Open house", sunday, sunday.AddDays(1))]);

        var busy = Assert.Single(Day(Sam, sunday, calendars).Busy);

        Assert.Equal(Est(3, 8, 0), busy.Start);
        Assert.Equal(Edt(3, 9, 0), busy.End);
        Assert.Equal(TimeSpan.FromHours(23), busy.Length);
    }

    [Fact]
    public void AllDayOnTheFallBackDayIsTwentyFiveHours()
    {
        var sunday = new DateOnly(2026, 11, 1);
        var calendars = Calendars(sam: [CalendarEntry.AllDay("Open house", sunday, sunday.AddDays(1))]);

        var busy = Assert.Single(Day(Sam, sunday, calendars).Busy);

        Assert.Equal(Edt(11, 1, 0), busy.Start);
        Assert.Equal(Est(11, 2, 0), busy.End);
        Assert.Equal(TimeSpan.FromHours(25), busy.Length);
    }

    // ---- zero-length entries ----

    [Fact]
    public void AZeroLengthEntryStillBlocksAMinute()
    {
        // A point in time has nothing to overlap, but a workout that spans it is on top of it.
        var calendars = Calendars(sam: [CalendarEntry.Timed("Pickup call", Edt(10, 26, 15), Edt(10, 26, 15))]);

        var sam = Day(Sam, Monday, calendars);

        Assert.Equal([new BusyInterval(Edt(10, 26, 15), Edt(10, 26, 15, 1), BusyReason.OwnCalendar)], sam.Busy);
        Assert.False(sam.IsFree(Edt(10, 26, 14, 30), Edt(10, 26, 15, 30)));
    }

    // ---- the clock changes ----

    [Fact]
    public void RulesKeepTheirClockAcrossSpringForward()
    {
        // Dinner is 6 pm on the clock on both days; the instants are an hour apart in UTC terms.
        var saturday = new DateOnly(2026, 3, 7);
        var week = BusyTime.For(Sam, saturday, saturday.AddDays(1), Rules(), Calendars());

        var dinners = week.NeverPlaced.Where(interval => interval.Reasons.HasFlag(BusyReason.FamilyDinner)).ToList();

        Assert.Equal(
            [
                new BusyInterval(Est(3, 7, 18), Est(3, 7, 19), BusyReason.FamilyDinner),
                new BusyInterval(Edt(3, 8, 18), Edt(3, 8, 19), BusyReason.FamilyDinner),
            ],
            dinners);
        Assert.Equal(TimeSpan.FromHours(-5), dinners[0].Start.Offset);
        Assert.Equal(TimeSpan.FromHours(-4), dinners[1].Start.Offset);
    }

    [Fact]
    public void WorkHoursKeepTheirClockAcrossFallBack()
    {
        var friday = new DateOnly(2026, 10, 30);
        var monday = new DateOnly(2026, 11, 2);
        var calendars = Calendars();

        var before = BusyTime.ForDay(Sam, friday, Rules(), calendars).NeverPlaced.First(interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
        var after = BusyTime.ForDay(Sam, monday, Rules(), calendars).NeverPlaced.First(interval => interval.Reasons.HasFlag(BusyReason.WorkHours));

        Assert.Equal(new BusyInterval(Edt(10, 30, 8), Edt(10, 30, 16), BusyReason.WorkHours), before);
        Assert.Equal(new BusyInterval(Est(11, 2, 8), Est(11, 2, 16), BusyReason.WorkHours), after);
    }

    [Fact]
    public void ASpanAcrossTheChangeKeepsItsLength()
    {
        // 11 pm Saturday to 4 am Sunday across the fall-back hour is five hours of clock but six of time.
        var entry = CalendarEntry.Timed("Overnight camp", Edt(10, 31, 23), Est(11, 1, 4));
        var calendars = Calendars(sam: [entry]);

        var week = BusyTime.For(Sam, new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 1), Rules(), calendars);

        var busy = Assert.Single(week.Busy);
        Assert.Equal(TimeSpan.FromHours(6), busy.Length);
    }

    [Fact]
    public void OutputInstantsCarryTheZonesOwnOffset()
    {
        var calendars = Calendars(sam: [CalendarEntry.Timed("Call", new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero))]);

        var busy = Assert.Single(Day(Sam, Monday, calendars).Busy);

        Assert.Equal(TimeSpan.FromHours(-4), busy.Start.Offset);
        Assert.Equal(10, busy.Start.Hour);
    }

    // ---- an overnight shift ----

    [Fact]
    public void ANightShiftRunsPastMidnight()
    {
        var nights = WorkSchedule.Of([new WorkShift(Sam, new TimeOnly(22, 0), new TimeOnly(6, 0))]);
        var rules = Rules(work: nights, dinner: new FamilyDinner(new TimeOnly(18, 0), TimeSpan.FromMinutes(60)));

        var week = BusyTime.For(Sam, Monday, Monday.AddDays(1), rules, Calendars());

        var work = week.NeverPlaced.Where(interval => interval.Reasons.HasFlag(BusyReason.WorkHours)).ToList();

        // Sunday had no shift, so Monday opens clear. Monday's shift runs to Tuesday at 6 as one
        // interval; Tuesday's begins at 10 pm and is cut by the end of the window.
        Assert.Equal(
            [
                new BusyInterval(Edt(10, 26, 22), Edt(10, 27, 6), BusyReason.WorkHours),
                new BusyInterval(Edt(10, 27, 22), Edt(10, 28, 0), BusyReason.WorkHours),
            ],
            work);
    }

    [Fact]
    public void YesterdaysShiftStillCountsThisMorning()
    {
        // Saturday has no shift of its own, but Friday night's runs into Saturday morning.
        var nights = WorkSchedule.Of([new WorkShift(Sam, new TimeOnly(22, 0), new TimeOnly(6, 0))]);
        var saturday = new DateOnly(2026, 10, 31);

        var sam = BusyTime.ForDay(Sam, saturday, Rules(work: nights), Calendars());

        Assert.Contains(new BusyInterval(Edt(10, 31, 0), Edt(10, 31, 6), BusyReason.WorkHours), sam.NeverPlaced);
    }

    // ---- an unreadable calendar fails closed ----

    [Fact]
    public void AnUnreadableOwnCalendarFailsClosed()
    {
        // Lee's calendar could not be read (the share is not done yet). That is not "Lee is free":
        // nothing may be placed for her, and the answer says why.
        var calendars = CalendarsWith(lee: CalendarRead.Unreadable("The calendar is not shared with this account."));

        var lee = Day(Lee, Monday, calendars);

        Assert.False(lee.IsComplete);
        Assert.False(lee.IsFree(Edt(10, 26, 21), Edt(10, 26, 22)));
        Assert.Empty(lee.FreeGaps(Edt(10, 26, 0), Edt(10, 27, 0), TimeSpan.FromMinutes(30)));
        var unreadable = Assert.Single(lee.Unreadable);
        Assert.Equal(CalendarSource.Own, unreadable.Source);
        Assert.Equal("The calendar is not shared with this account.", unreadable.Reason);
    }

    [Fact]
    public void APartnersUnreadableCalendarDoesNotStopMe()
    {
        var calendars = CalendarsWith(lee: CalendarRead.Unreadable("The calendar is not shared with this account."));

        var sam = Day(Sam, Monday, calendars);

        Assert.True(sam.IsComplete);
        Assert.Empty(sam.Unreadable);
        Assert.True(sam.IsFree(Edt(10, 26, 21), Edt(10, 26, 22)));
    }

    [Fact]
    public void AnUnreadableFamilyCalendarStopsBoth()
    {
        var calendars = CalendarsWith(family: CalendarRead.Unreadable("Google Calendar would not answer."));

        foreach (var person in new[] { Sam, Lee })
        {
            var busy = Day(person, Monday, calendars);
            Assert.False(busy.IsComplete);
            Assert.Equal(CalendarSource.Family, Assert.Single(busy.Unreadable).Source);
        }
    }

    [Fact]
    public void AnUnreadableChildrensCalendarStopsBoth()
    {
        var calendars = CalendarsWith(children: CalendarRead.Unreadable("Google Calendar would not answer."));

        foreach (var person in new[] { Sam, Lee })
        {
            var busy = Day(person, Monday, calendars);
            Assert.False(busy.IsComplete);
            Assert.Equal(CalendarSource.Children, Assert.Single(busy.Unreadable).Source);
        }
    }

    [Fact]
    public void SeveralUnreadableCalendarsAreAllReported()
    {
        var calendars = CalendarsWith(
            sam: CalendarRead.Unreadable("a"),
            family: CalendarRead.Unreadable("b"),
            children: CalendarRead.Unreadable("c"));

        var sources = Day(Sam, Monday, calendars).Unreadable.Select(item => item.Source).ToList();

        Assert.Equal([CalendarSource.Own, CalendarSource.Family, CalendarSource.Children], sources);
    }

    [Fact]
    public void UnreadableStillShowsTheRules()
    {
        // The result is incomplete, not empty: the never-placed hours are still there to read.
        var calendars = CalendarsWith(sam: CalendarRead.Unreadable("a"));

        Assert.NotEmpty(Day(Sam, Monday, calendars).NeverPlaced);
    }

    [Fact]
    public void ACalendarThatWasNeverLoadedFailsClosed()
    {
        var snapshot = new HouseholdCalendarSnapshot(
            new Dictionary<string, CalendarRead>(StringComparer.OrdinalIgnoreCase),
            CalendarRead.Of([]),
            CalendarRead.Of([]));

        var sam = Day(Sam, Monday, snapshot);

        Assert.False(sam.IsComplete);
        Assert.Equal(CalendarSource.Own, Assert.Single(sam.Unreadable).Source);
    }

    // ---- asking whether a time is free ----

    [Fact]
    public void ATimeBetweenEntriesIsFree()
    {
        var sam = Day(Sam, Monday, Calendars(sam: [OnMonday("Early", 5, 6), OnMonday("Late", 7, 8)]));

        Assert.True(sam.IsFree(Edt(10, 26, 6), Edt(10, 26, 7)));
    }

    [Fact]
    public void ATimeTouchingAnEntryIsStillFree()
    {
        var sam = Day(Sam, Monday, Calendars(sam: [OnMonday("Early", 5, 6)]));

        Assert.True(sam.IsFree(Edt(10, 26, 6), Edt(10, 26, 7)));
        Assert.True(sam.IsFree(Edt(10, 26, 4), Edt(10, 26, 5)));
    }

    [Fact]
    public void AMinuteOfOverlapIsNotFree()
    {
        var sam = Day(Sam, Monday, Calendars(sam: [OnMonday("Early", 5, 6)]));

        Assert.False(sam.IsFree(Edt(10, 26, 5, 59), Edt(10, 26, 7)));
        Assert.False(sam.IsFree(Edt(10, 26, 4), Edt(10, 26, 5, 1)));
    }

    [Fact]
    public void ATimeInsideWorkHoursIsNotFree()
    {
        Assert.False(Day(Sam, Monday, Calendars()).IsFree(Edt(10, 26, 10), Edt(10, 26, 11)));
    }

    [Fact]
    public void ATimeOutsideTheWindowIsNotKnownFree()
    {
        // Nothing was read for Tuesday, so Tuesday cannot be called free.
        var sam = Day(Sam, Monday, Calendars());

        Assert.False(sam.IsFree(Edt(10, 27, 21), Edt(10, 27, 22)));
        Assert.False(sam.IsFree(Edt(10, 26, 23), Edt(10, 27, 1)));
    }

    [Fact]
    public void AnEmptyOrBackwardsQuestionIsRefused()
    {
        var sam = Day(Sam, Monday, Calendars());

        Assert.Throws<ArgumentException>(() => sam.IsFree(Edt(10, 26, 21), Edt(10, 26, 21)));
        Assert.Throws<ArgumentException>(() => sam.IsFree(Edt(10, 26, 22), Edt(10, 26, 21)));
    }

    [Fact]
    public void FreeGapsAreWhatBlockedLeaves()
    {
        var sam = Day(Sam, Monday, Calendars(sam: [OnMonday("Call", 6, 7)]));

        var gaps = sam.FreeGaps(Edt(10, 26, 5), Edt(10, 26, 21), TimeSpan.FromMinutes(60));

        // 5-6 free, 6-7 call, 7-8 free, 8-16 work, 16-18 free, 18-19 dinner, 19:15-20 routine, 20-21 free.
        Assert.Equal(
            [
                new FreeGap(Edt(10, 26, 5), Edt(10, 26, 6)),
                new FreeGap(Edt(10, 26, 7), Edt(10, 26, 8)),
                new FreeGap(Edt(10, 26, 16), Edt(10, 26, 18)),
                new FreeGap(Edt(10, 26, 20), Edt(10, 26, 21)),
            ],
            gaps);
    }

    [Fact]
    public void FreeGapsDropTheOnesShorterThanAsked()
    {
        var sam = Day(Sam, Monday, Calendars());

        // The quarter hour between dinner (ends 7) and the routine (starts 7:15) is not a half hour.
        var gaps = sam.FreeGaps(Edt(10, 26, 19), Edt(10, 26, 20), TimeSpan.FromMinutes(30));

        Assert.Empty(gaps);
    }

    [Fact]
    public void FreeGapsAreClippedToTheAskedRange()
    {
        var sam = Day(Sam, Monday, Calendars());

        var gaps = sam.FreeGaps(Edt(10, 26, 16, 30), Edt(10, 26, 17, 30), TimeSpan.FromMinutes(30));

        Assert.Equal([new FreeGap(Edt(10, 26, 16, 30), Edt(10, 26, 17, 30))], gaps);
    }

    [Fact]
    public void FreeGapsNeverReachOutsideTheWindow()
    {
        var sam = Day(Sam, Monday, Calendars());

        var gaps = sam.FreeGaps(Edt(10, 25, 0), Edt(10, 28, 0), TimeSpan.FromMinutes(30));

        Assert.All(gaps, gap =>
        {
            Assert.True(gap.Start >= sam.WindowStart);
            Assert.True(gap.End <= sam.WindowEnd);
        });
    }

    // ---- arguments ----

    [Fact]
    public void AnUnknownPersonIsRefused()
    {
        var failure = Assert.Throws<ArgumentException>(() => Day("Pat", Monday, Calendars()));

        Assert.Equal("person", failure.ParamName);
    }

    [Fact]
    public void ANameIsMatchedWithoutCase()
    {
        var sam = Day("sam", Monday, Calendars());

        Assert.Equal(Sam, sam.Person);
    }

    [Fact]
    public void ABackwardsRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BusyTime.For(Sam, Monday, Monday.AddDays(-1), Rules(), Calendars()));
    }

    [Fact]
    public void AnAbsurdlyLongRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BusyTime.For(Sam, Monday, Monday.AddDays(400), Rules(), Calendars()));
    }

    [Fact]
    public void RulesWithNoAdultsAreRefused()
    {
        var rules = Rules() with { Adults = [] };

        Assert.Throws<ArgumentException>(() => Day(Sam, Monday, Calendars(), rules));
    }

    [Fact]
    public void RulesWithTwoAdultsOfOneNameAreRefused()
    {
        var rules = Rules() with { Adults = [SamAdult(), new Adult("SAM", true)] };

        Assert.Throws<ArgumentException>(() => Day(Sam, Monday, Calendars(), rules));
    }

    // ---- a busy week, end to end ----

    [Fact]
    public void AWholeWeekHoldsTogether()
    {
        var calendars = Calendars(
            sam: [OnMonday("Dentist", 10, 11)],
            lee: [CalendarEntry.Timed("Review", Edt(10, 27, 14), Edt(10, 27, 15))],
            family: [CalendarEntry.Timed("Piano recital", Edt(10, 28, 17), Edt(10, 28, 18))],
            children: [CalendarEntry.Timed("Robin soccer", Edt(10, 29, 16), Edt(10, 29, 17))]);

        var sam = BusyTime.For(Sam, Monday, Monday.AddDays(6), Rules(), calendars);
        var lee = BusyTime.For(Lee, Monday, Monday.AddDays(6), Rules(), calendars);

        Assert.Equal(
            [BusyReason.OwnCalendar, BusyReason.FamilyCalendar, BusyReason.ChildrensEvent],
            sam.Busy.Select(interval => interval.Reasons).ToList());
        Assert.Equal(
            [BusyReason.OwnCalendar, BusyReason.FamilyCalendar, BusyReason.ChildrensEvent],
            lee.Busy.Select(interval => interval.Reasons).ToList());
        Assert.DoesNotContain(sam.Busy, interval => interval.Start == Edt(10, 27, 14));
    }
}
