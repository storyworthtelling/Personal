using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>The hand-built weeks and forecasts the weather tests stand on. The week is the fixture's: Monday 2 November 2026, standard time.</summary>
internal static class ConditionsFixtures
{
    public const int Tuesday = 1;
    public const int Sunday = 6;

    public static readonly TimeSpan CardioLength = TimeSpan.FromMinutes(35);

    public static Session Cardio() => new(SessionKind.OutdoorCardio, CardioLength, true, [new Exercise("Brisk walk")]);

    public static Session Walk(TimeSpan? length = null) => new(SessionKind.GentleWalk, length ?? TimeSpan.FromMinutes(30), false, [new Exercise("Easy walk")]);

    public static Session Lifting() => new(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [new Exercise("Sit to stand from a chair")]);

    /// <summary>A session on a day offset, starting at <paramref name="start"/> inside a gap from <paramref name="gapFrom"/> to <paramref name="gapTo"/> (hours and minutes on the clock).</summary>
    public static PlannedSession Placed(
        Session session,
        int day,
        (int Hour, int Minute) start,
        (int Hour, int Minute) gapFrom,
        (int Hour, int Minute) gapTo,
        Together together = Together.None,
        params string[] who) =>
        new(
            Day(day),
            At(day, start.Hour, start.Minute),
            new FreeGap(At(day, gapFrom.Hour, gapFrom.Minute), At(day, gapTo.Hour, gapTo.Minute)),
            session,
            who.Length == 0 ? [Sam] : who,
            together);

    /// <summary>A week of exactly these sessions: each person's plan holds the sessions they take part in.</summary>
    public static HouseholdWorkoutPlan Week(params PlannedSession[] sessions)
    {
        var plans = new[] { Sam, Lee }
            .Select(person => new WeeklyPlan(person, WeekStart, sessions.Where(item => item.Participants.Contains(person)).OrderBy(item => item.Start).ToList(), null))
            .ToList();
        return new HouseholdWorkoutPlan(WeekStart, plans, sessions.Where(item => item.IsJoint).ToList());
    }

    public static WorkoutPlanRequest FreeWeek(EquipmentSet? equipment = null, DateTimeOffset? now = null) =>
        Request(Calendars(), equipment, now: now);

    /// <summary>An hour of fine weather: mild, dry, clean air, daylight.</summary>
    public static HourConditions Fine(int day, int hour) =>
        new(At(day, hour), 60, 0, PrecipitationKind.None, 30, true);

    /// <summary>Every hour of the whole week fine, from midnight to midnight.</summary>
    public static IEnumerable<HourConditions> FineWeek() =>
        Enumerable.Range(0, 7).SelectMany(day => Enumerable.Range(0, 24).Select(hour => Fine(day, hour)));

    /// <summary>The fine week with some hours of one day replaced by <paramref name="change"/>(hour).</summary>
    public static ConditionsRead WeekWith(int day, int fromHour, int toHour, Func<HourConditions, HourConditions> change) =>
        ConditionsRead.Of(FineWeek().Select(hour => DayOffset(hour) == day && hour.Start.Hour >= fromHour && hour.Start.Hour < toHour ? change(hour) : hour));

    /// <summary>The day of the week an hour belongs to, counted from the fixture's Monday.</summary>
    public static int DayOffset(HourConditions hour) => DateOnly.FromDateTime(hour.Start.DateTime).DayNumber - WeekStart.DayNumber;

    public static HourConditions Rainy(HourConditions hour) => hour with { PrecipitationChance = 80, Precipitation = PrecipitationKind.Rain };

    public static HourConditions Smoky(HourConditions hour) => hour with { AirQualityIndex = 150 };

    public static HourConditions Hot(HourConditions hour) => hour with { TemperatureFahrenheit = 96 };

    public static IEnumerable<PlannedSession> All(HouseholdWorkoutPlan plan) =>
        plan.Plans.SelectMany(week => week.Sessions).Distinct().ToList();
}

/// <summary>
/// C-5.2.2 · Weather and air. An outdoor session whose slot has a bad hourly forecast or poor air becomes an indoor
/// session or a rest day, and the card says why in one line. Nothing here reads a network: the forecast is a value.
/// </summary>
public class WeatherSwapTests
{
    private static readonly (int, int) Five = (17, 0);
    private static readonly (int, int) GapFrom = (16, 30);
    private static readonly (int, int) GapTo = (19, 0);

    private static PlannedSession Tuesday5() =>
        ConditionsFixtures.Placed(ConditionsFixtures.Cardio(), ConditionsFixtures.Tuesday, Five, GapFrom, GapTo);

    // The whole gap bad, so there is nowhere in the day to move it to.
    private static ConditionsRead BadAllAfternoon(Func<HourConditions, HourConditions> change) =>
        ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 15, 20, change);

    private static ConditionsAdaptation Adapt(PlannedSession session, ConditionsRead conditions, EquipmentSet? equipment = null, ConditionsLimits? limits = null) =>
        ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(equipment), ConditionsFixtures.Week(session), conditions, limits);

    // ---- RainMovesIndoors ----

    [Fact]
    public void RainMovesIndoors()
    {
        var original = Tuesday5();

        var result = Adapt(original, BadAllAfternoon(ConditionsFixtures.Rainy));

        var change = Assert.Single(result.Changes);
        Assert.Equal(ConditionAction.MovedIndoors, change.Action);
        Assert.Equal(ConditionCause.Rain, change.Cause);
        Assert.Same(original, change.Original);
        var replacement = Assert.Single(ConditionsFixtures.All(result.Plan));
        Assert.Same(change.Replacement, replacement);
        Assert.Equal(SessionKind.Treadmill, replacement.Session.Kind);
        Assert.Contains(EquipmentCatalogue.TreadmillId, replacement.Session.EquipmentNeeded);
        Assert.Equal(original.Start, replacement.Start);
        Assert.Equal(original.Gap, replacement.Gap);
        Assert.Equal(original.Session.Duration, replacement.Session.Duration);
        Assert.Equal(original.Day, replacement.Day);
        Assert.Equal([Sam], replacement.Participants);
        Assert.True(result.ConditionsWereRead);
    }

    [Fact]
    public void WithoutATreadmillRainMeansMobility()
    {
        var result = Adapt(Tuesday5(), BadAllAfternoon(ConditionsFixtures.Rainy), EquipmentSet.Of([EquipmentCatalogue.MatId]));

        var replacement = Assert.Single(ConditionsFixtures.All(result.Plan));
        Assert.Equal(SessionKind.Mobility, replacement.Session.Kind);
        Assert.Equal(ConditionAction.MovedIndoors, Assert.Single(result.Changes).Action);
        Assert.DoesNotContain(EquipmentCatalogue.TreadmillId, replacement.Session.EquipmentNeeded);
        Assert.All(replacement.Session.EquipmentNeeded, id => Assert.Equal(EquipmentCatalogue.MatId, id));
        Assert.Equal(Tuesday5().Start, replacement.Start);
    }

    [Fact]
    public void ANothingTickedWeekStillGetsAnIndoorSession()
    {
        var result = Adapt(Tuesday5(), BadAllAfternoon(ConditionsFixtures.Rainy), EquipmentSet.Of([]));

        var replacement = Assert.Single(ConditionsFixtures.All(result.Plan));
        Assert.Equal(SessionKind.Mobility, replacement.Session.Kind);
        Assert.Empty(replacement.Session.EquipmentNeeded);
    }

    // ---- PoorAirMovesIndoors ----

    [Fact]
    public void PoorAirMovesIndoors()
    {
        var result = Adapt(Tuesday5(), BadAllAfternoon(ConditionsFixtures.Smoky));

        var change = Assert.Single(result.Changes);
        Assert.Equal(ConditionAction.MovedIndoors, change.Action);
        Assert.Equal(ConditionCause.PoorAir, change.Cause);
        Assert.Equal(SessionKind.Treadmill, Assert.Single(ConditionsFixtures.All(result.Plan)).Session.Kind);
    }

    [Fact]
    public void AirJustUnderTheLineIsFine()
    {
        var result = Adapt(Tuesday5(), BadAllAfternoon(hour => hour with { AirQualityIndex = 100 }));

        Assert.Empty(result.Changes);
    }

    public static TheoryData<string, ConditionCause> TheOtherCauses => new()
    {
        { "snow", ConditionCause.Snow },
        { "ice", ConditionCause.Ice },
        { "heat", ConditionCause.Heat },
    };

    [Theory]
    [MemberData(nameof(TheOtherCauses))]
    public void SnowIceAndHeatMoveIndoorsToo(string weather, ConditionCause expected)
    {
        Func<HourConditions, HourConditions> change = weather switch
        {
            "snow" => hour => hour with { PrecipitationChance = 70, Precipitation = PrecipitationKind.Snow, TemperatureFahrenheit = 25 },
            "ice" => hour => hour with { PrecipitationChance = 70, Precipitation = PrecipitationKind.Ice, TemperatureFahrenheit = 30 },
            _ => ConditionsFixtures.Hot,
        };

        var result = Adapt(Tuesday5(), BadAllAfternoon(change));

        var found = Assert.Single(result.Changes);
        Assert.Equal(expected, found.Cause);
        Assert.Equal(ConditionAction.MovedIndoors, found.Action);
    }

    // ---- TheReasonIsWritten ----

    public static TheoryData<ConditionAction, ConditionCause, bool, string> TheLines => new()
    {
        { ConditionAction.MovedIndoors, ConditionCause.Rain, true, "Moved indoors: rain forecast" },
        { ConditionAction.MovedIndoors, ConditionCause.Snow, true, "Moved indoors: snow forecast" },
        { ConditionAction.MovedIndoors, ConditionCause.Ice, true, "Moved indoors: ice forecast" },
        { ConditionAction.MovedIndoors, ConditionCause.Heat, true, "Moved indoors: heat forecast" },
        { ConditionAction.MovedIndoors, ConditionCause.PoorAir, true, "Moved indoors: poor air" },
        { ConditionAction.MovedIndoors, ConditionCause.Darkness, true, "Moved indoors: after dark" },
        { ConditionAction.Rested, ConditionCause.Rain, true, "Rest day: rain forecast" },
        { ConditionAction.Rested, ConditionCause.PoorAir, true, "Rest day: poor air" },
        { ConditionAction.MovedInTime, ConditionCause.Rain, true, "Moved later: rain forecast" },
        { ConditionAction.MovedInTime, ConditionCause.Heat, false, "Moved earlier: heat forecast" },
    };

    [Theory]
    [MemberData(nameof(TheLines))]
    public void TheReasonIsWritten(ConditionAction action, ConditionCause cause, bool later, string expected) =>
        Assert.Equal(expected, ConditionsWords.For(action, cause, later));

    [Fact]
    public void TheCardCarriesTheLineTheChangeEarned()
    {
        var rain = Adapt(Tuesday5(), BadAllAfternoon(ConditionsFixtures.Rainy));
        var air = Adapt(Tuesday5(), BadAllAfternoon(ConditionsFixtures.Smoky));

        Assert.Equal("Moved indoors: rain forecast", Assert.Single(rain.Changes).Line);
        Assert.Equal("Moved indoors: poor air", Assert.Single(air.Changes).Line);
    }

    [Fact]
    public void ALineNeverCarriesAForecastNumber()
    {
        var result = Adapt(Tuesday5(), BadAllAfternoon(hour => hour with { PrecipitationChance = 83, Precipitation = PrecipitationKind.Rain, TemperatureFahrenheit = 97 }));

        var line = Assert.Single(result.Changes).Line;

        Assert.DoesNotContain(line, character => char.IsDigit(character));
        Assert.NotEmpty(line);
    }

    // ---- rest days ----

    [Fact]
    public void ASundayWalkInRainIsARestDay()
    {
        var walk = ConditionsFixtures.Placed(ConditionsFixtures.Walk(), ConditionsFixtures.Sunday, (10, 0), (9, 30), (12, 0));
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Sunday, 8, 14, ConditionsFixtures.Rainy);

        var result = Adapt(walk, conditions, EquipmentSet.Of([]));

        var change = Assert.Single(result.Changes);
        Assert.Equal(ConditionAction.Rested, change.Action);
        Assert.Null(change.Replacement);
        Assert.Equal("Rest day: rain forecast", change.Line);
        Assert.Empty(ConditionsFixtures.All(result.Plan));
    }

    [Fact]
    public void ASundayWalkGoesToTheTreadmillWhenOneIsTicked()
    {
        var walk = ConditionsFixtures.Placed(ConditionsFixtures.Walk(), ConditionsFixtures.Sunday, (10, 0), (9, 30), (12, 0));
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Sunday, 8, 14, ConditionsFixtures.Rainy);

        var result = Adapt(walk, conditions);

        var replacement = Assert.Single(ConditionsFixtures.All(result.Plan));
        Assert.Equal(SessionKind.GentleWalk, replacement.Session.Kind);
        Assert.Contains(EquipmentCatalogue.TreadmillId, replacement.Session.EquipmentNeeded);
        Assert.Equal(ConditionAction.MovedIndoors, Assert.Single(result.Changes).Action);
    }

    [Fact]
    public void ASundayIndoorWalkStaysAGentleWalk()
    {
        var walk = ConditionsFixtures.Placed(ConditionsFixtures.Walk(), ConditionsFixtures.Sunday, (10, 0), (9, 30), (12, 0));
        var request = ConditionsFixtures.FreeWeek();
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Sunday, 8, 14, ConditionsFixtures.Smoky);

        var result = ConditionsAdapter.Apply(request, ConditionsFixtures.Week(walk), conditions);

        Assert.Empty(PlanRules.Violations(request, result.Plan));
    }

    [Fact]
    public void AFamilyWalkInRainIsRestedForEveryone()
    {
        var family = ConditionsFixtures.Placed(ConditionsFixtures.Walk(TimeSpan.FromMinutes(40)), ConditionsFixtures.Sunday, (10, 0), (9, 30), (12, 0), Together.FamilyWalk, Sam, Lee);
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Sunday, 8, 14, ConditionsFixtures.Rainy);

        var result = Adapt(family, conditions);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ConditionAction.Rested, change.Action);
        Assert.Equal([Sam, Lee], change.People);
        Assert.Empty(result.Plan.JointSessions);
        Assert.All(result.Plan.Plans, week => Assert.Empty(week.Sessions));
    }

    [Fact]
    public void ARestedWeekSaysWhy()
    {
        var walk = ConditionsFixtures.Placed(ConditionsFixtures.Walk(), ConditionsFixtures.Sunday, (10, 0), (9, 30), (12, 0));
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Sunday, 8, 14, ConditionsFixtures.Rainy);

        var result = Adapt(walk, conditions, EquipmentSet.Of([]));

        var sam = SamOf(result.Plan);
        Assert.Empty(sam.Sessions);
        Assert.False(string.IsNullOrWhiteSpace(sam.NothingBecause));
        Assert.DoesNotContain(sam.NothingBecause!, character => char.IsDigit(character));
    }

    [Fact]
    public void AppliedTwiceItChangesNothingMore()
    {
        var request = ConditionsFixtures.FreeWeek();
        var sunday = ConditionsFixtures.Placed(ConditionsFixtures.Walk(), ConditionsFixtures.Sunday, (10, 0), (9, 30), (12, 0));
        var plan = ConditionsFixtures.Week(Tuesday5(), sunday);
        var conditions = ConditionsRead.Of(
            ConditionsFixtures.FineWeek().Select(hour => hour.Start.Hour is >= 8 and < 20 && ConditionsFixtures.DayOffset(hour) is 1 or 6 ? ConditionsFixtures.Rainy(hour) : hour));

        var first = ConditionsAdapter.Apply(request, plan, conditions);
        var second = ConditionsAdapter.Apply(request, first.Plan, conditions);

        Assert.Equal(2, first.Changes.Count);
        Assert.Empty(second.Changes);
        Assert.Same(first.Plan, second.Plan);
    }

    // ---- moving within the gap ----

    [Fact]
    public void ARainyHourMovesTheSessionToADryOneLater()
    {
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 17, 18, ConditionsFixtures.Rainy);

        var result = Adapt(Tuesday5(), conditions);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ConditionAction.MovedInTime, change.Action);
        Assert.Equal("Moved later: rain forecast", change.Line);
        var moved = Assert.Single(ConditionsFixtures.All(result.Plan));
        Assert.Equal(At(ConditionsFixtures.Tuesday, 18), moved.Start);
        Assert.Equal(SessionKind.OutdoorCardio, moved.Session.Kind);
        Assert.Equal(Tuesday5().Gap, moved.Gap);
        Assert.Equal(At(ConditionsFixtures.Tuesday, 17), change.Original.Start);
    }

    [Fact]
    public void AHotHourMovesTheSessionEarlier()
    {
        var session = ConditionsFixtures.Placed(ConditionsFixtures.Cardio(), ConditionsFixtures.Tuesday, (17, 0), (16, 0), (19, 0));
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 17, 19, ConditionsFixtures.Hot);

        var result = Adapt(session, conditions);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ConditionAction.MovedInTime, change.Action);
        Assert.Equal("Moved earlier: heat forecast", change.Line);
        Assert.Equal(At(ConditionsFixtures.Tuesday, 16, 15), Assert.Single(ConditionsFixtures.All(result.Plan)).Start);
    }

    [Fact]
    public void ANearerDryTimeBeatsAFurtherOne()
    {
        var session = ConditionsFixtures.Placed(ConditionsFixtures.Cardio(), ConditionsFixtures.Tuesday, (17, 0), (14, 0), (21, 0));
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 17, 18, ConditionsFixtures.Rainy);

        var moved = Assert.Single(ConditionsFixtures.All(Adapt(session, conditions).Plan));

        // Starts are tried on the quarter hour from the gap's start: 16:15 ends before the rain and is forty-five minutes from 17:00, where 18:00 is an hour.
        Assert.Equal(At(ConditionsFixtures.Tuesday, 16, 15), moved.Start);
    }

    [Fact]
    public void AMovedSessionStaysInsideItsGapWithRoomToShower()
    {
        // Dry only from 18:00; the session would need 18:00 to 18:35 plus fifteen minutes to shower, and the gap ends at 18:40.
        var session = ConditionsFixtures.Placed(ConditionsFixtures.Cardio(), ConditionsFixtures.Tuesday, (17, 0), (16, 30), (18, 40));
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 15, 18, ConditionsFixtures.Rainy);

        var result = Adapt(session, conditions);

        Assert.Equal(ConditionAction.MovedIndoors, Assert.Single(result.Changes).Action);
    }

    [Fact]
    public void ADrySpellOutsideTheGapIsNotUsed()
    {
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 16, 20, ConditionsFixtures.Rainy);

        var result = Adapt(Tuesday5(), conditions);

        Assert.Equal(ConditionAction.MovedIndoors, Assert.Single(result.Changes).Action);
    }

    [Fact]
    public void MovingCanBeSwitchedOff()
    {
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 17, 18, ConditionsFixtures.Rainy);

        var result = Adapt(Tuesday5(), conditions, limits: new ConditionsLimits { MayMoveWithinGap = false });

        Assert.Equal(ConditionAction.MovedIndoors, Assert.Single(result.Changes).Action);
    }

    [Fact]
    public void AMovedSessionNeverStartsBeforeNow()
    {
        // 16:00 and 16:15 would both finish before the hot hour; neither is allowed once it is 16:20.
        var now = At(ConditionsFixtures.Tuesday, 16, 20);
        var session = ConditionsFixtures.Placed(ConditionsFixtures.Cardio(), ConditionsFixtures.Tuesday, (17, 0), (16, 0), (19, 0));
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 17, 19, ConditionsFixtures.Hot);

        var result = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(now: now), ConditionsFixtures.Week(session), conditions);

        Assert.Equal(ConditionAction.MovedIndoors, Assert.Single(result.Changes).Action);
    }

    // ---- what is left alone ----

    [Fact]
    public void FineWeatherChangesNothing()
    {
        var plan = ConditionsFixtures.Week(Tuesday5());

        var result = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, ConditionsRead.Of(ConditionsFixtures.FineWeek()));

        Assert.Empty(result.Changes);
        Assert.Same(plan, result.Plan);
        Assert.True(result.ConditionsWereRead);
    }

    [Fact]
    public void IndoorSessionsAreNeverTouched()
    {
        var lifting = ConditionsFixtures.Placed(ConditionsFixtures.Lifting(), ConditionsFixtures.Tuesday, (17, 0), (16, 30), (19, 0));
        var plan = ConditionsFixtures.Week(lifting);
        var conditions = BadAllAfternoon(ConditionsFixtures.Rainy);

        var result = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, conditions);

        Assert.Empty(result.Changes);
        Assert.Same(plan, result.Plan);
    }

    [Fact]
    public void AnEveningTogetherIsIndoorsAndStays()
    {
        var evening = ConditionsFixtures.Placed(ConditionsFixtures.Lifting(), ConditionsFixtures.Tuesday, (20, 15), (20, 0), (21, 30), Together.AfterBedtime, Sam, Lee);
        var plan = ConditionsFixtures.Week(evening);

        var result = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 18, 23, ConditionsFixtures.Rainy));

        Assert.Empty(result.Changes);
    }

    [Fact]
    public void HoursTheForecastDoesNotHoldAreNotBad()
    {
        var result = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(), ConditionsFixtures.Week(Tuesday5()), ConditionsRead.Of([]));

        Assert.Empty(result.Changes);
        Assert.True(result.ConditionsWereRead);
    }

    [Fact]
    public void AForecastThatCouldNotBeReadChangesNothing()
    {
        var plan = ConditionsFixtures.Week(Tuesday5());

        var result = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, ConditionsRead.Unavailable("The forecast service did not answer."));

        Assert.False(result.ConditionsWereRead);
        Assert.Empty(result.Changes);
        Assert.Same(plan, result.Plan);
    }

    [Fact]
    public void ASessionAlreadyStartedIsLeftAlone()
    {
        var now = At(ConditionsFixtures.Tuesday, 17, 10);
        var plan = ConditionsFixtures.Week(Tuesday5());

        var result = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(now: now), plan, BadAllAfternoon(ConditionsFixtures.Rainy));

        Assert.Empty(result.Changes);
        Assert.Same(plan, result.Plan);
    }

    [Fact]
    public void ADayAwayIsNotJudgedByTheWeatherAtHome()
    {
        var plan = ConditionsFixtures.Week(Tuesday5());
        var awayTuesday = (Func<string, DateOnly, bool>)((person, day) => person == Sam && day == Day(ConditionsFixtures.Tuesday));

        var away = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, BadAllAfternoon(ConditionsFixtures.Rainy), isAway: awayTuesday);
        var home = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(), plan, BadAllAfternoon(ConditionsFixtures.Rainy), isAway: (person, day) => false);

        Assert.Empty(away.Changes);
        Assert.Single(home.Changes);
    }

    [Fact]
    public void ASessionRunningIntoABadHourIsBad()
    {
        var session = ConditionsFixtures.Placed(ConditionsFixtures.Cardio(), ConditionsFixtures.Tuesday, (17, 40), (16, 30), (19, 0));
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 15, 20, hour => hour.Start.Hour == 18 ? ConditionsFixtures.Rainy(hour) : hour);

        var result = Adapt(session, conditions);

        Assert.Single(result.Changes);
    }

    [Fact]
    public void ASessionEndingAsABadHourBeginsIsFine()
    {
        var session = ConditionsFixtures.Placed(ConditionsFixtures.Cardio(), ConditionsFixtures.Tuesday, (17, 25), (17, 0), (18, 0));
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Tuesday, 18, 20, ConditionsFixtures.Rainy);

        var result = Adapt(session, conditions);

        Assert.Empty(result.Changes);
    }

    // ---- a shared session, one object ----

    [Fact]
    public void AMovedFamilyWalkIsOneObjectInEveryPlan()
    {
        var family = ConditionsFixtures.Placed(ConditionsFixtures.Walk(TimeSpan.FromMinutes(40)), ConditionsFixtures.Sunday, (10, 0), (9, 0), (13, 0), Together.FamilyWalk, Sam, Lee);
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Sunday, 9, 11, ConditionsFixtures.Rainy);

        var result = Adapt(family, conditions);

        var change = Assert.Single(result.Changes);
        Assert.Equal(ConditionAction.MovedInTime, change.Action);
        Assert.Same(change.Replacement, Assert.Single(result.Plan.JointSessions));
        Assert.Same(change.Replacement, Assert.Single(SamOf(result.Plan).Sessions));
        Assert.Same(change.Replacement, Assert.Single(LeeOf(result.Plan).Sessions));
        Assert.Equal(Together.FamilyWalk, change.Replacement!.Together);
    }

    [Fact]
    public void OneChangeIsRecordedPerSessionNotPerPerson()
    {
        var family = ConditionsFixtures.Placed(ConditionsFixtures.Walk(TimeSpan.FromMinutes(40)), ConditionsFixtures.Sunday, (10, 0), (9, 30), (12, 0), Together.FamilyWalk, Sam, Lee);
        var conditions = ConditionsFixtures.WeekWith(ConditionsFixtures.Sunday, 8, 14, ConditionsFixtures.Smoky);

        Assert.Single(Adapt(family, conditions).Changes);
    }

    // ---- the week as a whole ----

    [Fact]
    public void ChangesComeInTimeOrder()
    {
        var thursday = ConditionsFixtures.Placed(ConditionsFixtures.Cardio(), 3, (17, 0), (16, 30), (19, 0));
        var tuesday = Tuesday5();
        var week = ConditionsFixtures.Week(thursday, tuesday);
        var conditions = ConditionsRead.Of(
            ConditionsFixtures.FineWeek().Select(hour => hour.Start.Hour is >= 15 and < 20 && ConditionsFixtures.DayOffset(hour) is 1 or 3 ? ConditionsFixtures.Rainy(hour) : hour));

        var result = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(), week, conditions);

        Assert.Equal([Day(1), Day(3)], result.Changes.Select(change => change.Day).ToArray());
    }

    [Fact]
    public void AdaptedSessionsStayInTimeOrder()
    {
        var early = ConditionsFixtures.Placed(ConditionsFixtures.Lifting(), ConditionsFixtures.Tuesday, (7, 0), (6, 30), (8, 0));
        var late = Tuesday5();

        var result = Adapt(late, BadAllAfternoon(ConditionsFixtures.Rainy));
        var two = ConditionsAdapter.Apply(ConditionsFixtures.FreeWeek(), ConditionsFixtures.Week(late, early), BadAllAfternoon(ConditionsFixtures.Rainy));

        var starts = SamOf(two.Plan).Sessions.Select(session => session.Start).ToList();
        Assert.Equal(starts.OrderBy(start => start).ToList(), starts);
        Assert.Equal(2, starts.Count);
        Assert.Single(result.Changes);
    }

    // ---- the real planner, whatever the weather ----

    [Fact]
    public void AdaptedWeeksKeepEveryPlannerRule()
    {
        var changed = 0;
        var rested = 0;
        for (var seed = 1; seed <= 120; seed++)
        {
            var random = new Random(seed);
            var equipment = EquipmentSet.Of(EquipmentCatalogue.All.Where(item => random.Next(2) == 0).Select(item => item.Id));
            var request = Request(Calendars(), equipment, now: Est(10, 30, 12), weekShift: random.Next(2) * 7);
            var plan = WorkoutPlanner.Plan(request);
            // A day is fine, bad all through (a storm, a smoke day), or bad by the hour.
            var conditions = ConditionsRead.Of(
                Enumerable.Range(0, 7)
                    .SelectMany(day =>
                    {
                        var mode = random.Next(3);
                        return Enumerable.Range(0, 24).Select(hour => RandomHour(random, request.WeekStart.AddDays(day), hour, mode));
                    })
                    .ToList());

            var result = ConditionsAdapter.Apply(request, plan, conditions);

            var where = $"seed {seed}";
            Assert.True(PlanRules.Violations(request, result.Plan).Count == 0, $"{where}: {string.Join(", ", PlanRules.Violations(request, result.Plan))}");
            foreach (var session in ConditionsFixtures.All(result.Plan).Where(item => item.Session.Kind is SessionKind.OutdoorCardio or SessionKind.GentleWalk))
            {
                Assert.True(
                    ConditionsAssessment.Of(conditions, session.Start, session.End, new ConditionsLimits()) is null,
                    $"{where}: an outdoor session is left in bad weather on {session.Day}");
            }

            Assert.True(
                ConditionsFixtures.All(result.Plan).Count() + result.Changes.Count(change => change.Replacement is null) >= ConditionsFixtures.All(plan).Count(),
                $"{where}: a session went missing without a change recorded");
            changed += result.Changes.Count;
            rested += result.Changes.Count(change => change.Action == ConditionAction.Rested);
        }

        Assert.True(changed > 100, $"The generated weeks changed only {changed} sessions; the test is not exercising the adapter.");
        Assert.True(rested > 0, "No generated week rested a session.");
    }

    private static HourConditions RandomHour(Random random, DateOnly date, int hour, int mode)
    {
        var bad = mode == 1 || (mode == 2 && random.Next(3) == 0);
        var kind = random.Next(4) switch
        {
            0 => PrecipitationKind.Rain,
            1 => PrecipitationKind.Snow,
            2 => PrecipitationKind.Ice,
            _ => PrecipitationKind.None,
        };
        return new HourConditions(
            At(date.DayNumber - WeekStart.DayNumber, hour),
            random.Next(20, 100),
            bad ? random.Next(50, 100) : random.Next(0, 30),
            kind,
            random.Next(0, 3) == 0 ? null : random.Next(20, 180),
            true);
    }

    // ---- the seam ----

    private sealed class FakeSource(ConditionsRead answer) : IConditionsSource
    {
        public DateTimeOffset? From { get; private set; }

        public DateTimeOffset? To { get; private set; }

        public Task<ConditionsRead> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
        {
            From = from;
            To = to;
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public async Task TheSourceIsAskedForTheWholeLocalWeek()
    {
        var source = new FakeSource(ConditionsRead.Of([]));
        var request = ConditionsFixtures.FreeWeek();

        await ConditionsAdapter.AdaptAsync(request, ConditionsFixtures.Week(Tuesday5()), source, CancellationToken.None);

        Assert.True(source.From <= At(0, 0), "The window starts after the first midnight of the week.");
        Assert.True(source.To >= At(7, 0), "The window ends before the last midnight of the week.");
    }

    [Fact]
    public async Task AFetchedForecastIsApplied()
    {
        var source = new FakeSource(BadAllAfternoon(ConditionsFixtures.Rainy));

        var result = await ConditionsAdapter.AdaptAsync(ConditionsFixtures.FreeWeek(), ConditionsFixtures.Week(Tuesday5()), source, CancellationToken.None);

        Assert.Equal(ConditionAction.MovedIndoors, Assert.Single(result.Changes).Action);
    }

    [Fact]
    public async Task AnUnavailableSourceChangesNothing()
    {
        var source = new FakeSource(ConditionsRead.Unavailable("The forecast service did not answer."));

        var result = await ConditionsAdapter.AdaptAsync(ConditionsFixtures.FreeWeek(), ConditionsFixtures.Week(Tuesday5()), source, CancellationToken.None);

        Assert.False(result.ConditionsWereRead);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void NothingHereReachesTheNetwork()
    {
        var files = ConditionsAndTripsSources.Files();
        Assert.True(files.Count >= 5, $"The guard found only {files.Count} files; it is not reading the real sources.");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("HttpClient", text);
            Assert.DoesNotContain("System.Net", text);
        }
    }
}

/// <summary>The Domain files this PBI owns, for the source guards.</summary>
internal static class ConditionsAndTripsSources
{
    public static IReadOnlyList<string> Files() =>
        ConditionsAndTripsFiles("Constitution.Domain", "Workouts");

    private static List<string> ConditionsAndTripsFiles(string project, string folder)
    {
        var directory = Path.Combine(ConstitutionSource.Root(), project, folder);
        return Directory.EnumerateFiles(directory, "*.cs")
            .Where(file => Path.GetFileName(file).StartsWith("Conditions", StringComparison.Ordinal) || Path.GetFileName(file).StartsWith("Trips", StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
    }
}
