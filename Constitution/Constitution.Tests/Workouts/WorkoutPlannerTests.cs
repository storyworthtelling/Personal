using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// C-5.1.1 · Sessions and the weekly plan. The planner is a rules engine over a week of free gaps:
/// the four named tests below are the acceptance, and the rest pin what each one leaves open (the
/// boundary of a gap, the days that are not fixed, the waking hours, failing closed).
///
/// <para>Every calendar here is invented (Sam, Lee, Robin, Juniper) and every expected instant is
/// written out on the clock, not computed with the planner's own helpers.</para>
/// </summary>
public class WorkoutPlannerTests
{
    private static readonly TimeSpan Thirty = TimeSpan.FromMinutes(30);

    // ---- acceptance 1: sessions fit gaps of at least thirty minutes ----

    [Fact]
    public void SessionsFitGapsOfThirtyMinutes()
    {
        // Sam is free only 17:30 to 18:00 on Monday (exactly thirty minutes) and 17:31 to 18:00 on
        // Tuesday (twenty-nine); every other day is spoken for.
        var calendars = Calendars(sam:
        [
            .. OnlyFree(0, (17, 30), (18, 0)),
            .. OnlyFree(1, (17, 31), (18, 0)),
            .. BlockDays(2, 3, 4, 5, 6),
        ]);

        var sessions = SamOf(Plan(calendars, bothAdults: false)).Sessions;

        var monday = Assert.Single(sessions);
        Assert.Equal(Day(0), monday.Day);
        Assert.Equal(At(0, 17, 30), monday.Gap.Start);
        Assert.Equal(At(0, 18, 0), monday.Gap.End);
        Assert.Equal(At(0, 17, 30), monday.Start);
        Assert.Equal(Thirty, monday.Session.Duration);
        Assert.Equal(At(0, 18, 0), monday.End);
        Assert.DoesNotContain(sessions, session => session.Day == Day(1));
    }

    [Fact]
    public void EverySessionSitsInsideAFreeGapOfThirtyMinutes()
    {
        var calendars = Calendars(
            sam: [OnMonday("Dentist", 16, 17), .. OnlyFree(2, (16, 0), (17, 0)), .. OnlyFree(4, (6, 30), (8, 0))],
            lee: [OnMonday("Parent evening", 16, 18), .. BlockDay(3)],
            family: [CalendarEntry.Timed("Piano", At(1, 16), At(1, 17, 30))]);
        var request = Request(calendars);

        var plan = WorkoutPlanner.Plan(request);

        foreach (var week in plan.Plans)
        {
            var busy = request.People.Single(person => person.Busy.Person == week.Person).Busy;
            Assert.NotEmpty(week.Sessions);
            foreach (var session in week.Sessions)
            {
                Assert.True(session.Gap.Length >= Thirty, $"{week.Person} {session.Day}: gap shorter than thirty minutes");
                Assert.True(session.Start >= session.Gap.Start && session.End <= session.Gap.End, $"{week.Person} {session.Day}: session leaves its gap");
                Assert.True(busy.IsFree(session.Start, session.End), $"{week.Person} {session.Day}: session overlaps busy or never-placed time");
                Assert.True(session.Session.Duration <= session.Gap.Length);
            }
        }
    }

    [Fact]
    public void ASessionIsSizedToItsGapNotTheOtherWayRound()
    {
        // 35 free minutes: the 40-minute strength session shrinks to 35 rather than overrunning.
        var calendars = Calendars(sam: [.. OnlyFree(0, (17, 25), (18, 0)), .. BlockDays(1, 2, 3, 4, 5, 6)]);

        var session = Assert.Single(SamOf(Plan(calendars, bothAdults: false)).Sessions);

        Assert.Equal(TimeSpan.FromMinutes(35), session.Session.Duration);
        Assert.Equal(At(0, 18, 0), session.End);
    }

    [Fact]
    public void AGapOfWholeHoursStillGetsABeginnerSizedSession()
    {
        // Saturday is the only free day, free from 6:30 to 6 pm: eleven hours does not become an eleven-hour workout.
        var session = SamOf(Plan(Calendars(sam: BlockDays(0, 1, 2, 3, 4, 6)), bothAdults: false)).Sessions.Single();

        Assert.True(session.Session.Duration <= TimeSpan.FromMinutes(45));
    }

    [Fact]
    public void FrequencyFollowsTheCalendar()
    {
        var free = SamOf(Plan(Calendars(), bothAdults: false)).Sessions.Where(item => item.Day.DayOfWeek != DayOfWeek.Sunday).ToList();
        var squeezed = SamOf(Plan(Calendars(sam: BlockDays(0, 1, 2, 5, 6)), bothAdults: false)).Sessions;

        Assert.Equal(3, free.Count);
        // Thursday and Friday are all that is left, and a hard day cannot follow a hard day.
        Assert.Equal(2, squeezed.Count);
        Assert.Equal(1, squeezed.Count(item => SessionKinds.IsHard(item.Session.Kind)));
        Assert.Equal(1, squeezed.Count(item => item.Session.Kind == SessionKind.Mobility));
    }

    [Fact]
    public void FrequencyFollowsRecovery()
    {
        var eased = SamOf(Plan(Calendars(), bothAdults: false, sam: new RecoveryInputs { SessionsDoneLastWeek = 1, SessionsSkippedLastWeek = 2 }));
        var earned = SamOf(Plan(Calendars(), bothAdults: false, sam: new RecoveryInputs { SessionsDoneLastWeek = 3, SessionsSkippedLastWeek = 0 }));

        Assert.Equal(2, eased.Sessions.Count(item => item.Day.DayOfWeek != DayOfWeek.Sunday));
        Assert.Equal(4, earned.Sessions.Count(item => item.Day.DayOfWeek != DayOfWeek.Sunday));
    }

    [Fact]
    public void NoDayOfTheWeekIsFixed()
    {
        var withoutOddDays = SamOf(Plan(Calendars(sam: BlockDays(0, 2, 4)), bothAdults: false));
        var withoutEvenDays = SamOf(Plan(Calendars(sam: BlockDays(1, 3, 5)), bothAdults: false));

        var first = withoutOddDays.Sessions.Where(item => item.Day.DayOfWeek != DayOfWeek.Sunday).Select(DayIndex).ToHashSet();
        var second = withoutEvenDays.Sessions.Where(item => item.Day.DayOfWeek != DayOfWeek.Sunday).Select(DayIndex).ToHashSet();

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.Empty(first.Intersect([0, 2, 4]));
        Assert.Empty(second.Intersect([1, 3, 5]));
        Assert.NotEqual(first.Order(), second.Order());
    }

    [Fact]
    public void NothingIsPlannedBeforeNow()
    {
        var plan = SamOf(Plan(Calendars(), bothAdults: false, now: At(2, 12)));

        Assert.NotEmpty(plan.Sessions);
        Assert.All(plan.Sessions, session => Assert.True(session.Start >= At(2, 12), "a session was planned in the past"));
    }

    // ---- waking hours: sleep is not in the never-placed time ----

    [Fact]
    public void NothingFallsOutsideWakingHours()
    {
        // Saturday is the only free day and is free all day; the planner must not offer the small hours.
        var saturday = SamOf(Plan(Calendars(sam: BlockDays(0, 1, 2, 3, 4, 6)), bothAdults: false)).SessionsOn(Day(5)).Single();

        Assert.Equal(At(5, 6, 30), saturday.Gap.Start);
        Assert.Equal(At(5, 18, 0), saturday.Gap.End);
    }

    [Fact]
    public void TheWakingHoursAreTheCallersToChange()
    {
        var calendars = Calendars(sam: BlockDays(0, 1, 2, 3, 4, 6));
        var request = Request(calendars, bothAdults: false) with { Settings = new WorkoutSettings { WakingStart = new TimeOnly(9, 0), WakingEnd = new TimeOnly(12, 0) } };

        var saturday = WorkoutPlanner.Plan(request).PlanFor(Sam)!.SessionsOn(Day(5)).Single();

        Assert.Equal(At(5, 9, 0), saturday.Gap.Start);
        Assert.Equal(At(5, 12, 0), saturday.Gap.End);
    }

    [Fact]
    public void AWeekWithAClockChangeKeepsWakingHoursOnTheWallClock()
    {
        // The week of 26 October ends with the clocks going back (Sunday 1 November, a 25-hour day):
        // the family walk still opens at 6:30 and ends at dinner, 6 pm, both on standard time (UTC-5).
        var request = Request(Calendars(), weekShift: -7, now: Est(10, 20, 12));

        var walk = Assert.Single(WorkoutPlanner.Plan(request).JointSessions);

        Assert.Equal(DayOfWeek.Sunday, walk.Day.DayOfWeek);
        Assert.Equal(Est(11, 1, 6, 30), walk.Gap.Start);
        Assert.Equal(Est(11, 1, 18, 0), walk.Gap.End);
    }

    // ---- failing closed ----

    [Fact]
    public void AnUnreadableCalendarPlansNothingAndSaysWhy()
    {
        var calendars = CalendarsWith(family: CalendarRead.Unreadable("Google refused the read."));

        var plan = Plan(calendars);

        foreach (var week in plan.Plans)
        {
            Assert.Empty(week.Sessions);
            Assert.Contains("family calendar", week.NothingBecause);
            Assert.Contains("Google refused the read.", week.NothingBecause);
        }

        Assert.Empty(plan.JointSessions);
    }

    [Fact]
    public void OnePersonsUnreadableCalendarCostsOnlyTheirOwnWeekAndTheSharedOnes()
    {
        var calendars = CalendarsWith(lee: CalendarRead.Unreadable("Lee's calendar timed out."));

        var plan = Plan(calendars);

        Assert.Empty(LeeOf(plan).Sessions);
        Assert.NotNull(LeeOf(plan).NothingBecause);
        Assert.NotEmpty(SamOf(plan).Sessions);
        Assert.Null(SamOf(plan).NothingBecause);
        // Nothing is shared with a calendar nobody could read, not even a Sunday walk.
        Assert.Empty(plan.JointSessions);
        Assert.All(SamOf(plan).Sessions, session => Assert.False(session.IsJoint));
    }

    [Fact]
    public void AWeekWithNoRoomSaysSo()
    {
        var week = SamOf(Plan(Calendars(sam: BlockDays(0, 1, 2, 3, 4, 5, 6)), bothAdults: false));

        Assert.Empty(week.Sessions);
        Assert.Contains("30 minutes", week.NothingBecause);
    }

    [Fact]
    public void ARequestWithNoOneInItIsRefused()
    {
        var request = Request(Calendars()) with { People = [] };

        Assert.Throws<ArgumentException>(() => WorkoutPlanner.Plan(request));
    }

    [Fact]
    public void TheSamePersonTwiceIsRefused()
    {
        var request = Request(Calendars(), bothAdults: false);
        var twice = request with { People = [request.People[0], request.People[0]] };

        Assert.Throws<ArgumentException>(() => WorkoutPlanner.Plan(twice));
    }

    [Fact]
    public void BackwardsWakingHoursAreRefused()
    {
        var request = Request(Calendars()) with { Settings = new WorkoutSettings { WakingStart = new TimeOnly(22, 0), WakingEnd = new TimeOnly(6, 0) } };

        Assert.Throws<ArgumentException>(() => WorkoutPlanner.Plan(request));
    }

    [Fact]
    public void ANegativeHistoryIsRefused()
    {
        var request = Request(Calendars(), sam: new RecoveryInputs { SessionsSkippedLastWeek = -1 });

        Assert.Throws<ArgumentOutOfRangeException>(() => WorkoutPlanner.Plan(request));
    }

    // ---- acceptance 2: equipment (D72) ----

    [Fact]
    public void NeverPrescribesUntickedEquipment()
    {
        var sets = new[]
        {
            EquipmentSet.Of([]),
            EquipmentSet.Of([EquipmentCatalogue.MatId]),
            EquipmentSet.Of([EquipmentCatalogue.BandsId]),
            EquipmentSet.Of([EquipmentCatalogue.TreadmillId]),
            EquipmentSet.Of([EquipmentCatalogue.DumbbellsId, EquipmentCatalogue.BenchId]),
            EquipmentSet.Everything().With(EquipmentCatalogue.TreadmillId, false),
            EquipmentSet.Everything().With(EquipmentCatalogue.MatId, false).With(EquipmentCatalogue.BandsId, false),
            EquipmentSet.Everything(),
        };

        var prescribed = new HashSet<string>();
        foreach (var set in sets)
        {
            foreach (var shift in new[] { 0, 7 })
            {
                var plan = Plan(Calendars(), set, weekShift: shift);
                Assert.NotEmpty(SamOf(plan).Sessions);
                foreach (var session in plan.Plans.SelectMany(week => week.Sessions))
                {
                    foreach (var id in session.Session.EquipmentNeeded)
                    {
                        Assert.True(set.Has(id), $"{set.Encode()}: a {session.Session.Kind} session needs unticked {id}");
                        prescribed.Add(id);
                    }

                    foreach (var exercise in session.Session.Exercises.Where(item => item.EquipmentId is not null))
                    {
                        Assert.True(set.Has(exercise.EquipmentId!), $"{set.Encode()}: {exercise.Name} needs unticked {exercise.EquipmentId}");
                    }

                    if (session.Session.Kind == SessionKind.Treadmill)
                    {
                        Assert.True(set.Has(EquipmentCatalogue.TreadmillId));
                    }
                }
            }
        }

        // Not vacuous: with everything ticked, every one of the six is used somewhere across the weeks.
        Assert.Equal(EquipmentCatalogue.All.Select(item => item.Id).Order(), prescribed.Order());
    }

    [Fact]
    public void AnEmptyChecklistStillGivesAFullWeek()
    {
        var week = SamOf(Plan(Calendars(), EquipmentSet.Of([])));

        Assert.Equal(3, week.Sessions.Count(item => item.Day.DayOfWeek != DayOfWeek.Sunday));
        Assert.All(week.Sessions, session => Assert.Empty(session.Session.EquipmentNeeded));
    }

    [Fact]
    public void TheTreadmillTakesTurnsWithOutdoorsWhenItIsTicked()
    {
        var kinds = new[] { 0, 7 }
            .Select(shift => SamOf(Plan(Calendars(), weekShift: shift)).Sessions.Select(item => item.Session.Kind).Single(kind => kind is SessionKind.Treadmill or SessionKind.OutdoorCardio))
            .ToList();

        Assert.Contains(SessionKind.Treadmill, kinds);
        Assert.Contains(SessionKind.OutdoorCardio, kinds);
    }

    [Fact]
    public void WithoutATreadmillTheCardioIsAlwaysOutdoors()
    {
        var set = EquipmentSet.Everything().With(EquipmentCatalogue.TreadmillId, false);

        foreach (var shift in new[] { 0, 7, 14 })
        {
            var kinds = SamOf(Plan(Calendars(), set, weekShift: shift)).Sessions.Select(item => item.Session.Kind).ToList();
            Assert.DoesNotContain(SessionKind.Treadmill, kinds);
            Assert.Contains(SessionKind.OutdoorCardio, kinds);
        }
    }

    // ---- the balanced week ----

    [Fact]
    public void AFreeWeekIsStrengthCardioMobilityAndASundayWalk()
    {
        var kinds = SamOf(Plan(Calendars(), bothAdults: false)).Sessions.Select(item => item.Session.Kind).ToList();

        Assert.Contains(SessionKind.Strength, kinds);
        Assert.Contains(kinds, kind => kind is SessionKind.OutdoorCardio or SessionKind.Treadmill);
        Assert.Contains(SessionKind.Mobility, kinds);
        Assert.Contains(SessionKind.GentleWalk, kinds);
        Assert.Equal(4, kinds.Count);
    }

    [Fact]
    public void CardioIsSweatyAndTheRestIsNot()
    {
        var sessions = SamOf(Plan(Calendars(), bothAdults: false)).Sessions;

        Assert.Contains(sessions, session => session.Session.IsSweaty);
        Assert.Contains(sessions, session => !session.Session.IsSweaty);
        Assert.All(sessions, session =>
            Assert.Equal(session.Session.Kind is SessionKind.OutdoorCardio or SessionKind.Treadmill, session.Session.IsSweaty));
    }

    [Fact]
    public void HardSessionsAreNeverOnConsecutiveDays()
    {
        foreach (var recovery in new[] { RecoveryInputs.Fresh, new RecoveryInputs { SessionsDoneLastWeek = 3 } })
        {
            var hard = SamOf(Plan(Calendars(), bothAdults: false, sam: recovery)).Sessions
                .Where(item => SessionKinds.IsHard(item.Session.Kind)).Select(DayIndex).Order().ToList();

            Assert.True(hard.Count >= 2);
            for (var i = 1; i < hard.Count; i++)
            {
                Assert.True(hard[i] - hard[i - 1] >= 2, $"hard sessions on day {hard[i - 1]} and {hard[i]}");
            }
        }
    }

    [Fact]
    public void TheDayAfterARecentHardSessionIsNotHard()
    {
        var recovery = new RecoveryInputs { LastHardSessionDay = Day(-1) };

        var sessions = SamOf(Plan(Calendars(), bothAdults: false, sam: recovery)).Sessions;

        Assert.DoesNotContain(sessions, item => item.Day == Day(0) && SessionKinds.IsHard(item.Session.Kind));
        Assert.Contains(sessions, item => SessionKinds.IsHard(item.Session.Kind));
    }

    [Fact]
    public void MobilityMayFollowAHardDay()
    {
        // Only Thursday and Friday are free: strength on one, mobility the next day.
        var sessions = SamOf(Plan(Calendars(sam: BlockDays(0, 1, 2, 5, 6)), bothAdults: false)).Sessions.OrderBy(item => item.Start).ToList();

        Assert.Equal([3, 4], sessions.Select(DayIndex));
        Assert.True(SessionKinds.IsHard(sessions[0].Session.Kind));
        Assert.Equal(SessionKind.Mobility, sessions[1].Session.Kind);
    }

    [Fact]
    public void NoOneHasTwoSessionsInADay()
    {
        var plan = Plan(Calendars());

        foreach (var week in plan.Plans)
        {
            Assert.True(week.Sessions.Count >= 4);
            Assert.Equal(week.Sessions.Count, week.Sessions.Select(item => item.Day).Distinct().Count());
        }
    }

    [Fact]
    public void ASessionHasAtLeastOneExerciseWithAPlainName()
    {
        var sessions = Plan(Calendars()).Plans.SelectMany(week => week.Sessions).ToList();

        Assert.True(sessions.Count >= 8);
        Assert.All(sessions, session =>
        {
            Assert.NotEmpty(session.Session.Exercises);
            Assert.All(session.Session.Exercises, exercise => Assert.False(string.IsNullOrWhiteSpace(exercise.Name)));
        });
    }

    [Fact]
    public void SessionsAreInTimeOrderAndTheSameInputsGiveTheSamePlan()
    {
        var first = Plan(Calendars(sam: [OnMonday("Dentist", 16, 17)]));
        var second = Plan(Calendars(sam: [OnMonday("Dentist", 16, 17)]));

        Assert.True(first.Plans.All(week => week.Sessions.Count >= 4));

        foreach (var (a, b) in first.Plans.Zip(second.Plans))
        {
            Assert.Equal(a.Sessions.Select(item => item.Start), b.Sessions.Select(item => item.Start));
            Assert.Equal(a.Sessions.Select(item => item.Session.Kind), b.Sessions.Select(item => item.Session.Kind));
            Assert.Equal(a.Sessions.SelectMany(item => item.Session.Exercises.Select(exercise => exercise.Name)), b.Sessions.SelectMany(item => item.Session.Exercises.Select(exercise => exercise.Name)));
            Assert.Equal(a.Sessions.OrderBy(item => item.Start).Select(item => item.Start), a.Sessions.Select(item => item.Start));
        }
    }

    [Fact]
    public void TwoStrengthDaysDoNotRepeatTheSameExercises()
    {
        var strength = SamOf(Plan(Calendars(), bothAdults: false, sam: new RecoveryInputs { SessionsDoneLastWeek = 3 })).Sessions
            .Where(item => item.Session.Kind == SessionKind.Strength).ToList();

        Assert.Equal(2, strength.Count);
        Assert.NotEqual(strength[0].Session.Exercises.Select(item => item.Name), strength[1].Session.Exercises.Select(item => item.Name));
    }

    // ---- acceptance 3: Sundays are gentle walks ----

    [Fact]
    public void SundaysAreGentleWalks()
    {
        var recoveries = new[] { RecoveryInputs.Fresh, new RecoveryInputs { SessionsDoneLastWeek = 6 }, new RecoveryInputs { SessionsSkippedLastWeek = 3 } };
        var sundays = 0;
        foreach (var recovery in recoveries)
        {
            foreach (var shift in new[] { 0, 7 })
            {
                var plan = Plan(Calendars(), sam: recovery, lee: recovery, weekShift: shift);
                foreach (var session in plan.Plans.SelectMany(week => week.Sessions).Where(item => item.Day.DayOfWeek == DayOfWeek.Sunday))
                {
                    sundays++;
                    Assert.Equal(SessionKind.GentleWalk, session.Session.Kind);
                    Assert.False(session.Session.IsSweaty);
                    Assert.Empty(session.Session.EquipmentNeeded);
                }
            }
        }

        Assert.True(sundays >= 12, "Sundays were not planned at all, so the test proved nothing.");
    }

    [Fact]
    public void ASundayCarriesNoMobilityEvenWhenTheWeekIsOtherwiseFull()
    {
        // Every weekday is spoken for: the only free day is Sunday.
        var week = SamOf(Plan(Calendars(sam: BlockDays(0, 1, 2, 3, 4, 5)), bothAdults: false));

        var sunday = Assert.Single(week.Sessions);
        Assert.Equal(Day(6), sunday.Day);
        Assert.Equal(SessionKind.GentleWalk, sunday.Session.Kind);
    }

    [Fact]
    public void ASundayWalkIsThirtyMinutesAlone()
    {
        var calendars = Calendars(lee: BlockDay(6));

        var sunday = SamOf(Plan(calendars)).SessionsOn(Day(6)).Single();

        Assert.False(sunday.IsJoint);
        Assert.Equal([Sam], sunday.Participants);
        Assert.Equal(Thirty, sunday.Session.Duration);
    }

    // ---- acceptance 4: joint sessions ----

    [Fact]
    public void AJointSessionReplacesBothOwn()
    {
        // Sam can train only on Tuesday. Lee is free on Tuesday only after the children are in bed
        // (8 to 9:30 pm). Sunday is spoken for, to keep the family walk out of it.
        CalendarEntry[] lee = [.. OnlyFree(1, (20, 0), (21, 30)), .. BlockDays(0, 2, 3, 4, 5, 6)];
        CalendarEntry[] sam = [.. BlockDays(0, 2, 3, 4, 5, 6)];

        // Baseline: with Lee free on Tuesday, Sam trains alone on Tuesday, in the afternoon.
        var baseline = SamOf(Plan(Calendars(sam: sam))).Sessions;
        var alone = Assert.Single(baseline);
        Assert.False(alone.IsJoint);
        Assert.Equal(At(1, 16, 0), alone.Start);

        var plan = Plan(Calendars(sam: sam, lee: lee));

        var joint = Assert.Single(plan.JointSessions);
        Assert.Equal(Together.AfterBedtime, joint.Together);
        Assert.Equal(Day(1), joint.Day);
        Assert.Equal([Sam, Lee], joint.Participants);
        Assert.Equal(At(1, 20, 0), joint.Gap.Start);
        Assert.Equal(At(1, 21, 30), joint.Gap.End);
        Assert.Equal(At(1, 20, 0), joint.Start);

        // It is the one session each of them has that day, and on both cards.
        Assert.Equal([joint], SamOf(plan).SessionsOn(Day(1)));
        Assert.Equal([joint], LeeOf(plan).SessionsOn(Day(1)));
        Assert.Single(SamOf(plan).Sessions);
        Assert.Single(LeeOf(plan).Sessions);
        Assert.Contains(joint, plan.FamilyCalendarSessions);
        Assert.True(joint.IsJoint);
    }

    [Fact]
    public void AnEveningTogetherEndsBeforeSleep()
    {
        var calendars = Calendars(
            sam: [.. OnlyFree(1, (20, 0), (23, 59)), .. BlockDays(0, 2, 3, 4, 5, 6)],
            lee: [.. OnlyFree(1, (20, 0), (23, 59)), .. BlockDays(0, 2, 3, 4, 5, 6)]);

        var joint = Assert.Single(Plan(calendars).JointSessions);

        Assert.Equal(At(1, 21, 30), joint.Gap.End);
        Assert.True(joint.End <= At(1, 21, 30));
    }

    [Fact]
    public void AnEveningTogetherNeedsBothToBeFree()
    {
        CalendarEntry[] sam = [.. OnlyFree(1, (20, 0), (21, 30)), .. BlockDays(0, 2, 3, 4, 5, 6)];
        CalendarEntry[] lee = [.. OnlyFree(1, (20, 0), (20, 30)), .. BlockDays(0, 2, 3, 4, 5, 6)];

        var plan = Plan(Calendars(sam: sam, lee: lee));

        // Thirty minutes shared is enough; twenty-nine would not be.
        Assert.Single(plan.JointSessions);

        CalendarEntry[] tooShort = [.. OnlyFree(1, (20, 0), (20, 29)), .. BlockDays(0, 2, 3, 4, 5, 6)];
        var none = Plan(Calendars(sam: sam, lee: tooShort));
        Assert.Empty(none.JointSessions);
        Assert.Empty(LeeOf(none).Sessions);
    }

    [Fact]
    public void AfterBedtimeIsOnlyEverJoint()
    {
        // Sam alone is free after bedtime: he does not train alone at nine o'clock.
        CalendarEntry[] sam = [.. OnlyFree(1, (20, 0), (21, 30)), .. BlockDays(0, 2, 3, 4, 5, 6)];
        var lee = BlockDays(0, 1, 2, 3, 4, 5, 6);

        var plan = Plan(Calendars(sam: sam, lee: lee));

        Assert.Empty(SamOf(plan).Sessions);

        // Control: the same evening with Lee free too is a joint session, so the empty week above is the rule at work.
        CalendarEntry[] leeFree = [.. OnlyFree(1, (20, 0), (21, 30)), .. BlockDays(0, 2, 3, 4, 5, 6)];
        Assert.Single(Plan(Calendars(sam: sam, lee: leeFree)).JointSessions);
    }

    [Fact]
    public void ABedtimeMustBeKnownToCountAsAfterIt()
    {
        var rules = Rules(bedtime: BedtimeRoutine.None);
        var calendars = Calendars(
            sam: [.. OnlyFree(1, (20, 0), (21, 30)), .. BlockDays(0, 2, 3, 4, 5, 6)],
            lee: [.. OnlyFree(1, (20, 0), (21, 30)), .. BlockDays(0, 2, 3, 4, 5, 6)]);

        var plan = WorkoutPlanner.Plan(Request(calendars, rules: rules));

        // No known bedtime, so no evening counts as "after the children are in bed": each plans alone.
        Assert.Empty(plan.JointSessions);
        Assert.Equal(At(1, 20, 0), Assert.Single(SamOf(plan).Sessions).Start);
        Assert.Equal(At(1, 20, 0), Assert.Single(LeeOf(plan).Sessions).Start);
        Assert.All(plan.Plans.SelectMany(week => week.Sessions), session => Assert.False(session.IsJoint));
    }

    [Fact]
    public void ASundayFamilyWalkReplacesBothOwnWalks()
    {
        var plan = Plan(Calendars());

        var walk = Assert.Single(plan.JointSessions, item => item.Together == Together.FamilyWalk);
        Assert.Equal(Day(6), walk.Day);
        Assert.Equal(SessionKind.GentleWalk, walk.Session.Kind);
        Assert.Equal([Sam, Lee], walk.Participants);
        Assert.Equal([walk], SamOf(plan).SessionsOn(Day(6)));
        Assert.Equal([walk], LeeOf(plan).SessionsOn(Day(6)));
        // The children are awake: it ends before their routine begins at 7:15 pm.
        Assert.True(walk.Gap.End <= At(6, 19, 15));
        Assert.True(walk.End <= At(6, 19, 15));
        Assert.Contains(walk, plan.FamilyCalendarSessions);
    }

    [Fact]
    public void AFamilyWalkNeedsBothAdultsFreeOnSunday()
    {
        var plan = Plan(Calendars(lee: BlockDay(6)));

        Assert.Empty(plan.JointSessions);
        Assert.Single(SamOf(plan).SessionsOn(Day(6)));
        Assert.Empty(LeeOf(plan).SessionsOn(Day(6)));
    }

    [Fact]
    public void AJointSessionIsOneObjectOnEveryCard()
    {
        var plan = Plan(Calendars());

        var walk = plan.JointSessions.Single();
        Assert.Same(walk, SamOf(plan).SessionsOn(Day(6)).Single());
        Assert.Same(walk, LeeOf(plan).SessionsOn(Day(6)).Single());
    }

    [Fact]
    public void OwnSessionsAreNeverMarkedJoint()
    {
        var plan = Plan(Calendars());

        var own = plan.Plans.SelectMany(week => week.Sessions).Where(item => !plan.JointSessions.Contains(item)).ToList();
        Assert.True(own.Count >= 6);
        foreach (var session in own)
        {
            Assert.Equal(Together.None, session.Together);
            Assert.Single(session.Participants);
        }
    }

    [Fact]
    public void ASingleAdultNeverGetsAJointSession()
    {
        var plan = Plan(Calendars(), bothAdults: false);

        Assert.Empty(plan.JointSessions);
        Assert.NotEmpty(SamOf(plan).Sessions);
    }
}
