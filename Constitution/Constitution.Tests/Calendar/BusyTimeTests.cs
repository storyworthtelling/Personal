using Constitution.Domain.Calendar;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar;

/// <summary>
/// C-5.3.1 · What counts as busy, one test per rule in the acceptance, over the invented household
/// (Sam, Lee, Robin, Juniper). A wrong answer here is silent: a workout lands on top of someone's
/// life and nothing says so until it happens (R7), so each rule is pinned from both sides — what
/// it blocks and what it must leave alone.
///
/// <para>The rules, as the acceptance words them: <b>busy</b> is the person's own calendar, the
/// family calendar, and the children's events when that person is on pickup (an event naming a
/// parent blocks that parent, one naming nobody blocks both); <b>never-placed</b> time is work
/// hours from Foreword's schedule (a free WFH lunch gap allowed), family dinner and the children's
/// bedtime routine. The edge cases (merging, midnight, all-day, daylight-saving) are in
/// <see cref="BusyTimeEdgeTests"/>.</para>
/// </summary>
public class BusyTimeTests
{
    private static BusyTime ForSam(HouseholdCalendarSnapshot calendars, PlanningRules? rules = null) =>
        BusyTime.ForDay(Sam, Monday, rules ?? Rules(), calendars);

    private static BusyTime ForLee(HouseholdCalendarSnapshot calendars, PlanningRules? rules = null) =>
        BusyTime.ForDay(Lee, Monday, rules ?? Rules(), calendars);

    private static BusyInterval Own(int fromHour, int toHour) =>
        new(Edt(10, 26, fromHour), Edt(10, 26, toHour), BusyReason.OwnCalendar);

    private static BusyInterval Family(int fromHour, int toHour) =>
        new(Edt(10, 26, fromHour), Edt(10, 26, toHour), BusyReason.FamilyCalendar);

    private static BusyInterval Child(int fromHour, int toHour) =>
        new(Edt(10, 26, fromHour), Edt(10, 26, toHour), BusyReason.ChildrensEvent);

    // ---- busy: the person's own calendar ----

    [Fact]
    public void OwnCalendarIsBusy()
    {
        var sam = ForSam(Calendars(sam: [OnMonday("Dentist", 10, 11)]));

        Assert.Equal([Own(10, 11)], sam.Busy);
    }

    [Fact]
    public void AnotherPersonsCalendarIsNotMine()
    {
        var calendars = Calendars(lee: [OnMonday("Dentist", 10, 11)]);

        Assert.Empty(ForSam(calendars).Busy);
        Assert.Equal([Own(10, 11)], ForLee(calendars).Busy);
    }

    // ---- busy: the family calendar ----

    [Fact]
    public void FamilyCalendarBlocksBothAdults()
    {
        var calendars = Calendars(family: [OnMonday("Piano recital", 15, 16)]);

        Assert.Equal([Family(15, 16)], ForSam(calendars).Busy);
        Assert.Equal([Family(15, 16)], ForLee(calendars).Busy);
    }

    [Fact]
    public void FamilyEventNamingOneAdultStillBlocksBoth()
    {
        // Blocking is broad on purpose: a name in a title is a heuristic, and "Dinner with Lee's
        // parents" is Sam's evening too. Only freeing time is narrow (the off-day tests).
        var calendars = Calendars(family: [OnMonday("Lee's parents visit", 15, 16)]);

        Assert.Equal([Family(15, 16)], ForSam(calendars).Busy);
    }

    // ---- busy: the children's events, when on pickup ----

    [Fact]
    public void ChildEventNamingAParentBlocksThatParent()
    {
        var calendars = Calendars(children: [OnMonday("Robin soccer, Lee drives", 16, 17)]);

        Assert.Equal([Child(16, 17)], ForLee(calendars).Busy);
        Assert.Empty(ForSam(calendars).Busy);
    }

    [Fact]
    public void ChildEventNamingNobodyBlocksBoth()
    {
        var calendars = Calendars(children: [OnMonday("Juniper swimming", 16, 17)]);

        Assert.Equal([Child(16, 17)], ForSam(calendars).Busy);
        Assert.Equal([Child(16, 17)], ForLee(calendars).Busy);
    }

    [Fact]
    public void ChildEventNamingBothParentsBlocksBoth()
    {
        var calendars = Calendars(children: [OnMonday("Parent-teacher night: Sam and Lee", 17, 18)]);

        Assert.Equal([Child(17, 18)], ForSam(calendars).Busy);
        Assert.Equal([Child(17, 18)], ForLee(calendars).Busy);
    }

    [Fact]
    public void ChildEventNamingTheOtherParentSparesMe()
    {
        // Sam is on pickup and the event names Lee: it is Lee's, not Sam's.
        var calendars = Calendars(children: [OnMonday("Robin soccer, Lee drives", 16, 17)]);

        Assert.Empty(ForSam(calendars, Rules(samOnPickup: true)).Busy);
    }

    [Fact]
    public void UnnamedChildEventSparesOffPickup()
    {
        // The pickup flag is per person: Sam is on it, Lee is not.
        var calendars = Calendars(children: [OnMonday("Juniper swimming", 16, 17)]);
        var rules = Rules(samOnPickup: true, leeOnPickup: false);

        Assert.Equal([Child(16, 17)], ForSam(calendars, rules).Busy);
        Assert.Empty(ForLee(calendars, rules).Busy);
    }

    [Fact]
    public void NobodyOnPickupBlocksNoUnnamedEvent()
    {
        var calendars = Calendars(children: [OnMonday("Juniper swimming", 16, 17)]);
        var rules = Rules(samOnPickup: false, leeOnPickup: false);

        Assert.Empty(ForSam(calendars, rules).Busy);
        Assert.Empty(ForLee(calendars, rules).Busy);
    }

    [Fact]
    public void ANamedParentIsBlockedEvenOffPickup()
    {
        // The one place this departs from the acceptance's literal wording ("when that person is on
        // pickup"). An event that names Lee puts Lee at it whatever the pickup rota says, and the
        // wrong answer here is a workout over an event with her name on it. The pickup flag decides
        // who an event naming nobody belongs to, not whether a named parent is free.
        var calendars = Calendars(children: [OnMonday("Robin soccer, Lee drives", 16, 17)]);
        var rules = Rules(leeOnPickup: false);

        Assert.Equal([Child(16, 17)], ForLee(calendars, rules).Busy);
    }

    [Fact]
    public void AGuestAddressNamesAParent()
    {
        // An invited address is a fact where a name in a title is a heuristic.
        var calendars = Calendars(children: [OnMonday("Juniper dance", 16, 17, guests: [LeeAddress])]);

        Assert.Equal([Child(16, 17)], ForLee(calendars).Busy);
        Assert.Empty(ForSam(calendars).Busy);
    }

    [Fact]
    public void GuestMatchingIgnoresCase()
    {
        var calendars = Calendars(children: [OnMonday("Juniper dance", 16, 17, guests: ["LEE@Example.com"])]);

        Assert.Equal([Child(16, 17)], ForLee(calendars).Busy);
        Assert.Empty(ForSam(calendars).Busy);
    }

    [Fact]
    public void AGuestWhoIsNobodyOfOursNamesNobody()
    {
        var calendars = Calendars(children: [OnMonday("Juniper dance", 16, 17, guests: ["coach@example.com"])]);

        Assert.Equal([Child(16, 17)], ForSam(calendars).Busy);
        Assert.Equal([Child(16, 17)], ForLee(calendars).Busy);
    }

    [Fact]
    public void ATitleAndAGuestBothCount()
    {
        // The guest list says Lee and the title says Sam: both are named, so both are blocked.
        // Reading only the guests would leave Sam free over an event with his name on it.
        var calendars = Calendars(children: [OnMonday("Robin soccer with Sam", 16, 17, guests: [LeeAddress])]);

        Assert.Equal([Child(16, 17)], ForSam(calendars).Busy);
        Assert.Equal([Child(16, 17)], ForLee(calendars).Busy);
    }

    [Theory]
    [InlineData("Samba class")]
    [InlineData("Samuel's party")]
    [InlineData("Sam-Lee wedding")]
    [InlineData("Robin at the Leeds museum")]
    public void ANameInsideAWordNamesNobody(string title)
    {
        // "Samba" is not Sam. Mistaking it for him would name him alone and leave Lee free of an
        // event that really names nobody: the unsafe direction.
        var calendars = Calendars(children: [OnMonday(title, 16, 17)]);

        Assert.Equal([Child(16, 17)], ForSam(calendars).Busy);
        Assert.Equal([Child(16, 17)], ForLee(calendars).Busy);
    }

    [Theory]
    [InlineData("Sam's turn to drive")]
    [InlineData("SAM drives")]
    [InlineData("Robin, Sam")]
    [InlineData("(Sam)")]
    public void ANameAsAWholeWordIsNamed(string title)
    {
        var calendars = Calendars(children: [OnMonday(title, 16, 17)]);

        Assert.Equal([Child(16, 17)], ForSam(calendars).Busy);
        Assert.Empty(ForLee(calendars).Busy);
    }

    [Fact]
    public void AnotherNameForAParentIsNamed()
    {
        var rules = new PlanningRules(
            Zone,
            [new Adult(Sam, true, [SamAddress], ["Dad"]), LeeAdult()],
            WorkSchedule.Read(ForewordSchedule),
            new FamilyDinner(new TimeOnly(18, 0), TimeSpan.FromMinutes(60)),
            BedtimeRoutine.None,
            WorkFromHomeLunch.None);
        var calendars = Calendars(children: [OnMonday("Robin soccer, Dad drives", 16, 17)]);

        Assert.Equal([Child(16, 17)], BusyTime.ForDay(Sam, Monday, rules, calendars).Busy);
        Assert.Empty(BusyTime.ForDay(Lee, Monday, rules, calendars).Busy);
    }

    // ---- an entry that is "free" blocks nothing ----

    [Fact]
    public void FreeEntriesBlockNothingOnAnyCalendar()
    {
        var calendars = Calendars(
            sam: [OnMonday("Birthday reminder", 10, 11, Availability.Free)],
            family: [OnMonday("School holiday", 10, 11, Availability.Free)],
            children: [OnMonday("Juniper art show", 10, 11, Availability.Free)]);

        Assert.Empty(ForSam(calendars).Busy);
        Assert.Empty(ForLee(calendars).Busy);
    }

    // ---- never-placed: work hours from Foreword's schedule ----

    [Fact]
    public void WorkHoursAreNeverPlaced()
    {
        var sam = ForSam(Calendars());

        Assert.Contains(new BusyInterval(Edt(10, 26, 8), Edt(10, 26, 16), BusyReason.WorkHours), sam.NeverPlaced);
        Assert.Empty(sam.Busy);
    }

    [Fact]
    public void EachPersonKeepsTheirOwnWorkHours()
    {
        Assert.Contains(new BusyInterval(Edt(10, 26, 8), Edt(10, 26, 15, 30), BusyReason.WorkHours), ForLee(Calendars()).NeverPlaced);
        Assert.DoesNotContain(ForSam(Calendars()).NeverPlaced, interval => interval.End == Edt(10, 26, 15, 30));
    }

    [Fact]
    public void WorkHoursAreWeekdaysOnly()
    {
        var week = BusyTime.For(Sam, Monday, Monday.AddDays(6), Rules(), Calendars());

        var workDays = week.NeverPlaced
            .Where(interval => interval.Reasons.HasFlag(BusyReason.WorkHours))
            .Select(interval => interval.Start.DayOfWeek)
            .ToList();

        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            workDays);
    }

    [Fact]
    public void AWfhDayFreesTheLunchGap()
    {
        // Working from home: the lunch hour inside the shift is free to use. It is marked by an
        // all-day entry on the person's own calendar saying so (Foreword's schedule carries no WFH).
        var calendars = Calendars(sam: [CalendarEntry.AllDay("WFH", Monday, Monday.AddDays(1), Availability.Free)]);

        var work = ForSam(calendars).NeverPlaced.Where(interval => interval.Reasons.HasFlag(BusyReason.WorkHours)).ToList();

        Assert.Equal(
            [
                new BusyInterval(Edt(10, 26, 8), Edt(10, 26, 12), BusyReason.WorkHours),
                new BusyInterval(Edt(10, 26, 13), Edt(10, 26, 16), BusyReason.WorkHours),
            ],
            work);
    }

    [Fact]
    public void TheLunchGapIsOnlyForWhoIsAtHome()
    {
        var calendars = Calendars(sam: [CalendarEntry.AllDay("WFH", Monday, Monday.AddDays(1), Availability.Free)]);

        var lee = ForLee(calendars).NeverPlaced.Where(interval => interval.Reasons.HasFlag(BusyReason.WorkHours)).ToList();

        Assert.Equal([new BusyInterval(Edt(10, 26, 8), Edt(10, 26, 15, 30), BusyReason.WorkHours)], lee);
    }

    [Fact]
    public void NoLunchGapAtTheOffice()
    {
        var work = ForSam(Calendars()).NeverPlaced.Where(interval => interval.Reasons.HasFlag(BusyReason.WorkHours)).ToList();

        Assert.Equal([new BusyInterval(Edt(10, 26, 8), Edt(10, 26, 16), BusyReason.WorkHours)], work);
    }

    [Fact]
    public void ALunchGapDoesNotFreeAMeetingInIt()
    {
        // The gap is free of the work rule, not of the calendar: a meeting at noon is still busy.
        var calendars = Calendars(sam: [
            CalendarEntry.AllDay("Work from home", Monday, Monday.AddDays(1), Availability.Free),
            OnMonday("Lunch with the team", 12, 13)]);

        var sam = ForSam(calendars);

        Assert.Equal([Own(12, 13)], sam.Busy);
        Assert.False(sam.IsFree(Edt(10, 26, 12), Edt(10, 26, 13)));
    }

    [Fact]
    public void AnOffDayFreesTheWorkHours()
    {
        var calendars = Calendars(sam: [CalendarEntry.AllDay("PTO", Monday, Monday.AddDays(1), Availability.Free)]);

        var sam = ForSam(calendars);

        Assert.DoesNotContain(sam.NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
        Assert.True(sam.IsFree(Edt(10, 26, 10), Edt(10, 26, 11)));
    }

    [Fact]
    public void FamilyOffDaysFreeOnlyWhoTheyName()
    {
        var named = Calendars(family: [CalendarEntry.AllDay("Lee off", Monday, Monday.AddDays(1), Availability.Free)]);
        var unnamed = Calendars(family: [CalendarEntry.AllDay("Vacation", Monday, Monday.AddDays(1), Availability.Free)]);

        // "Lee off" frees Lee's hours and leaves Sam's. Freeing time is the unsafe direction, so it
        // is narrow where blocking is broad.
        Assert.Contains(ForSam(named).NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
        Assert.DoesNotContain(ForLee(named).NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));

        // A bare "Vacation" on the family calendar is everyone's.
        Assert.DoesNotContain(ForSam(unnamed).NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
        Assert.DoesNotContain(ForLee(unnamed).NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
    }

    [Theory]
    [InlineData("Offsite planning")]
    [InlineData("Off-site retreat")]
    [InlineData("Kickoff")]
    [InlineData("Vacationland trip planning")]
    public void ANeighbouringWordIsNotAnOffDay(string title)
    {
        // "Off" inside "Offsite" is not a day off. Treating it as one would free a whole working
        // day on a guess.
        var calendars = Calendars(sam: [CalendarEntry.AllDay(title, Monday, Monday.AddDays(1), Availability.Free)]);

        Assert.Contains(ForSam(calendars).NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
    }

    [Theory]
    [InlineData("Day off")]
    [InlineData("PTO")]
    [InlineData("pto - dentist")]
    [InlineData("Vacation: the lake")]
    public void AnOffKeywordAsAWholeWordIsAnOffDay(string title)
    {
        var calendars = Calendars(sam: [CalendarEntry.AllDay(title, Monday, Monday.AddDays(1), Availability.Free)]);

        Assert.DoesNotContain(ForSam(calendars).NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
    }

    [Fact]
    public void ATimedEntryIsNeverAnOffDay()
    {
        // Foreword only reads an all-day entry as a day off; a one-hour "PTO" appointment is not one.
        var calendars = Calendars(sam: [OnMonday("PTO paperwork", 10, 11)]);

        Assert.Contains(ForSam(calendars).NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
    }

    [Fact]
    public void NoShiftForAPersonMeansNoWorkHours()
    {
        var onlyLee = WorkSchedule.Of([new WorkShift(Lee, new TimeOnly(9, 0), new TimeOnly(17, 0))]);

        var sam = ForSam(Calendars(), Rules(work: onlyLee));

        Assert.DoesNotContain(sam.NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
        Assert.False(sam.WorkHoursAssumed);
    }

    [Fact]
    public void AnUnknownScheduleAssumesHours()
    {
        // No saved schedule (a fresh install, a vault not yet restored) must not read as "no work":
        // that would put a workout over someone's day without a word. Weekday 8 to 4 is assumed,
        // and the result says it was assumed so the head can ask for the real one.
        var unknown = WorkSchedule.Read(string.Empty);

        var sam = ForSam(Calendars(), Rules(work: unknown));

        Assert.True(sam.WorkHoursAssumed);
        Assert.Contains(new BusyInterval(Edt(10, 26, 8), Edt(10, 26, 16), BusyReason.WorkHours), sam.NeverPlaced);
    }

    [Fact]
    public void AnUnknownScheduleFreesNothingOnAnOffDay()
    {
        // Without the real schedule there is no list of off-day words to trust either.
        var calendars = Calendars(sam: [CalendarEntry.AllDay("PTO", Monday, Monday.AddDays(1), Availability.Free)]);

        var sam = ForSam(calendars, Rules(work: WorkSchedule.Read(string.Empty)));

        Assert.Contains(sam.NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.WorkHours));
    }

    [Fact]
    public void AKnownScheduleIsNotMarkedAssumed()
    {
        Assert.False(ForSam(Calendars()).WorkHoursAssumed);
    }

    // ---- never-placed: family dinner ----

    [Fact]
    public void FamilyDinnerIsNeverPlacedForBoth()
    {
        Assert.Contains(new BusyInterval(Edt(10, 26, 18), Edt(10, 26, 19), BusyReason.FamilyDinner), ForSam(Calendars()).NeverPlaced);
        Assert.Contains(new BusyInterval(Edt(10, 26, 18), Edt(10, 26, 19), BusyReason.FamilyDinner), ForLee(Calendars()).NeverPlaced);
    }

    [Fact]
    public void FamilyDinnerIsEveryDayNotJustWorkDays()
    {
        var weekend = BusyTime.For(Sam, new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 1), Rules(), Calendars());

        var dinners = weekend.NeverPlaced.Where(interval => interval.Reasons.HasFlag(BusyReason.FamilyDinner)).ToList();

        Assert.Equal(
            [
                new BusyInterval(Edt(10, 31, 18), Edt(10, 31, 19), BusyReason.FamilyDinner),
                new BusyInterval(Est(11, 1, 18), Est(11, 1, 19), BusyReason.FamilyDinner),
            ],
            dinners);
    }

    [Fact]
    public void DinnerComesFromTheRulesNotACopy()
    {
        var late = Rules(dinner: new FamilyDinner(new TimeOnly(19, 30), TimeSpan.FromMinutes(90)), bedtime: BedtimeRoutine.None);

        Assert.Contains(
            new BusyInterval(Edt(10, 26, 19, 30), Edt(10, 26, 21), BusyReason.FamilyDinner),
            ForSam(Calendars(), late).NeverPlaced);
    }

    // ---- never-placed: the children's bedtime routine ----

    [Fact]
    public void BedtimeRoutineIsNeverPlacedForBoth()
    {
        var expected = new BusyInterval(Edt(10, 26, 19, 15), Edt(10, 26, 20), BusyReason.BedtimeRoutine);

        Assert.Contains(expected, ForSam(Calendars()).NeverPlaced);
        Assert.Contains(expected, ForLee(Calendars()).NeverPlaced);
    }

    [Fact]
    public void TheRoutineEndsAtBedtime()
    {
        // After the children are in bed the evening is the adults' (a joint session may go there).
        var sam = ForSam(Calendars());

        Assert.True(sam.IsFree(Edt(10, 26, 20), Edt(10, 26, 21)));
    }

    [Fact]
    public void NoRoutineWhenThereAreNoChildren()
    {
        var sam = ForSam(Calendars(), Rules(bedtime: BedtimeRoutine.None));

        Assert.DoesNotContain(sam.NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.BedtimeRoutine));
    }

    [Fact]
    public void TouchingDinnerAndRoutineMerge()
    {
        var rules = Rules(
            dinner: new FamilyDinner(new TimeOnly(18, 0), TimeSpan.FromMinutes(60)),
            bedtime: new BedtimeRoutine(new TimeOnly(20, 0), TimeSpan.FromMinutes(60)));

        var evening = ForSam(Calendars(), rules).NeverPlaced.Single(interval => interval.Start == Edt(10, 26, 18));

        Assert.Equal(new BusyInterval(Edt(10, 26, 18), Edt(10, 26, 20), BusyReason.FamilyDinner | BusyReason.BedtimeRoutine), evening);
    }

    // ---- busy and never-placed stay distinguishable ----

    [Fact]
    public void BusyAndNeverPlacedStayInSeparateLists()
    {
        // A 9 to 10 meeting inside Sam's working day: the meeting is busy, the day is never-placed,
        // and neither list shows the other's reason.
        var sam = ForSam(Calendars(sam: [OnMonday("Standup", 9, 10)]));

        Assert.Equal([Own(9, 10)], sam.Busy);
        Assert.All(sam.Busy, interval => Assert.True(BusyReasons.IsBusy(interval.Reasons)));
        Assert.All(sam.NeverPlaced, interval => Assert.True(BusyReasons.IsNeverPlaced(interval.Reasons)));
        Assert.DoesNotContain(sam.NeverPlaced, interval => interval.Reasons.HasFlag(BusyReason.OwnCalendar));
    }

    [Fact]
    public void BlockedIsTheUnionOfBothWithEveryReason()
    {
        var sam = ForSam(Calendars(sam: [OnMonday("Standup", 9, 10), OnMonday("Late call", 16, 17)]));

        // 8-16 work, with the standup inside it, merges into the late call that touches it.
        var day = sam.Blocked.First();
        Assert.Equal(Edt(10, 26, 8), day.Start);
        Assert.Equal(Edt(10, 26, 17), day.End);
        Assert.Equal(BusyReason.WorkHours | BusyReason.OwnCalendar, day.Reasons);
    }

    [Fact]
    public void ThePersonAndWindowAreReported()
    {
        var sam = ForSam(Calendars());

        Assert.Equal(Sam, sam.Person);
        Assert.Equal(Edt(10, 26, 0), sam.WindowStart);
        Assert.Equal(Edt(10, 27, 0), sam.WindowEnd);
        Assert.True(sam.IsComplete);
    }
}
