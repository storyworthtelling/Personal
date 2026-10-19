using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.Workouts;

/// <summary>
/// C-5.1.2 · Assessment week. A first install, or two weeks without a done session, gets three gentle sessions
/// whose numbers are read back; the plan proper starts in week two. The three named tests are the acceptance;
/// the rest pin the boundaries (the fifteenth day, not the fourteenth) and what "gentle" means in minutes.
/// </summary>
public class AssessmentTests
{
    private static readonly DateOnly Week = WeekStart;

    private static string[] Shape(IEnumerable<PlannedSession> sessions) =>
        sessions.Select(item => $"{item.Day:yyyy-MM-dd} {item.Start:HH:mm} {item.Session.Kind} {item.Session.Duration.TotalMinutes}").ToArray();

    // ---- FirstWeekIsAssessment ----

    [Fact]
    public void FirstWeekIsAssessment()
    {
        var decision = WorkoutPhaseRules.Decide(TrainingHistory.Fresh, Week);
        Assert.Equal(new PhaseDecision(WorkoutPhase.Assessment, PhaseReason.FirstInstall), decision);

        var week = AssessmentPlanner.Plan(Request(Calendars(), bothAdults: false));
        var sessions = SamOf(week.Plan).Sessions;

        Assert.Equal(3, sessions.Count);
        Assert.Equal(3, week.Sessions.Count);
        var kinds = sessions.Select(item => item.Session.Kind).ToList();
        Assert.Contains(SessionKind.Strength, kinds);
        Assert.Contains(SessionKind.Mobility, kinds);
        Assert.Single(kinds, kind => kind is SessionKind.OutdoorCardio or SessionKind.Treadmill);
        Assert.DoesNotContain(SessionKind.GentleWalk, kinds);
    }

    [Fact]
    public void EachSessionNamesTheTestItIs()
    {
        var week = AssessmentPlanner.Plan(Request(Calendars(), bothAdults: false));

        Assert.Equal(3, week.Sessions.Count);
        Assert.Equal(
            [AssessmentTest.StrengthBaselines, AssessmentTest.TimedWalk, AssessmentTest.MobilityCheck],
            week.Sessions.Select(item => item.Test).OrderBy(test => test).ToArray());
        foreach (var item in week.Sessions)
        {
            Assert.Equal(AssessmentCatalogue.TestFor(item.Placed.Session.Kind), item.Test);
            Assert.NotEmpty(item.Measures);
            Assert.Equal(AssessmentCatalogue.MeasuresOf(item.Test), item.Measures);
            Assert.Contains(item.Placed, SamOf(week.Plan).Sessions);
        }
    }

    [Fact]
    public void TheWeekIsGentleAndShort()
    {
        var week = AssessmentPlanner.Plan(Request(Calendars(), bothAdults: false));
        var moderate = SamOf(Plan(Calendars(), bothAdults: false)).Sessions;

        Assert.Equal(3, week.Sessions.Count);
        foreach (var item in week.Sessions)
        {
            Assert.True(item.Placed.Session.Duration <= TimeSpan.FromMinutes(30), $"{item.Test} runs {item.Placed.Session.Duration}");
            Assert.True(item.Placed.Session.Duration >= TimeSpan.FromMinutes(20));
        }

        var strengthLength = moderate.First(item => item.Session.Kind == SessionKind.Strength).Session.Duration;
        Assert.Equal(TimeSpan.FromMinutes(40), strengthLength);
        Assert.True(week.Sessions.Single(item => item.Test == AssessmentTest.StrengthBaselines).Placed.Session.Duration < strengthLength);
    }

    [Fact]
    public void AssessmentSessionsStayInsideTheRules()
    {
        var request = Request(Calendars(), bothAdults: false);
        var week = AssessmentPlanner.Plan(request);

        Assert.Equal(3, week.Sessions.Count);
        foreach (var item in SamOf(week.Plan).Sessions)
        {
            Assert.True(item.Gap.Length >= WorkoutPlanner.MinimumGap);
            Assert.True(item.Start >= item.Gap.Start && item.End <= item.Gap.End);
            Assert.True(request.People[0].Busy.IsFree(item.Start, item.End));
            Assert.NotEqual(DayOfWeek.Sunday, item.Day.DayOfWeek);
        }

        var hardDays = SamOf(week.Plan).Sessions.Where(item => SessionKinds.IsHard(item.Session.Kind)).Select(item => item.Day.DayNumber).OrderBy(day => day).ToList();
        Assert.Equal(2, hardDays.Count);
        Assert.True(hardDays[1] - hardDays[0] >= 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void NoAssessmentPrescribesAnUntickedItem(int weekShift)
    {
        var week = AssessmentPlanner.Plan(Request(Calendars(), EquipmentSet.Of([]), bothAdults: false, weekShift: weekShift));

        Assert.Equal(3, week.Sessions.Count);
        foreach (var item in week.Sessions)
        {
            Assert.Empty(item.Placed.Session.EquipmentNeeded);
        }
    }

    [Fact]
    public void ATreadmillWalkNeedsATickedTreadmill()
    {
        // The planner alternates the road and the treadmill week by week when one is ticked: of two weeks running, one of each.
        var walks = new[] { 0, 7 }
            .Select(shift => AssessmentPlanner.Plan(Request(Calendars(), EquipmentSet.Everything(), bothAdults: false, weekShift: shift)))
            .Select(week => week.Sessions.Single(item => item.Test == AssessmentTest.TimedWalk).Placed.Session)
            .ToList();

        Assert.Equal(
            [SessionKind.OutdoorCardio, SessionKind.Treadmill],
            walks.Select(walk => walk.Kind).OrderBy(kind => kind).ToArray());
        Assert.Equal([EquipmentCatalogue.TreadmillId], walks.Single(walk => walk.Kind == SessionKind.Treadmill).EquipmentNeeded);
        Assert.Empty(walks.Single(walk => walk.Kind == SessionKind.OutdoorCardio).EquipmentNeeded);
    }

    [Fact]
    public void AnAssessmentKeepsTheLastHardDayApart()
    {
        // A hard session on the Sunday before keeps Monday free of a hard one.
        var sam = new RecoveryInputs { LastHardSessionDay = Day(-1) };
        var week = AssessmentPlanner.Plan(Request(Calendars(), sam: sam, bothAdults: false));

        Assert.Equal(3, week.Sessions.Count);
        Assert.DoesNotContain(SamOf(week.Plan).Sessions, item => item.Day == Day(0) && SessionKinds.IsHard(item.Session.Kind));
    }

    [Fact]
    public void AssessmentAsksForThreeWhateverLastWeekDid()
    {
        var eased = new RecoveryInputs { SessionsDoneLastWeek = 0, SessionsSkippedLastWeek = 5 };
        var week = AssessmentPlanner.Plan(Request(Calendars(), sam: eased, bothAdults: false));

        Assert.Equal(3, SamOf(week.Plan).Sessions.Count);
    }

    [Fact]
    public void AnUnreadableCalendarStillPlansNothing()
    {
        var calendars = CalendarsWith(sam: Constitution.Domain.Calendar.CalendarRead.Unreadable("could not be read"));
        var week = AssessmentPlanner.Plan(Request(calendars, bothAdults: false));

        Assert.Empty(SamOf(week.Plan).Sessions);
        Assert.NotNull(SamOf(week.Plan).NothingBecause);
        Assert.Empty(week.Sessions);
    }

    // ---- TwoWeeksOffReRunsIt ----

    [Fact]
    public void TwoWeeksOffReRunsIt()
    {
        var fifteen = new TrainingHistory { AssessmentWeekStart = Week.AddDays(-70), LastDoneDay = Week.AddDays(-15) };
        Assert.Equal(new PhaseDecision(WorkoutPhase.Assessment, PhaseReason.TwoWeeksOff), WorkoutPhaseRules.Decide(fifteen, Week));

        var fourteen = new TrainingHistory { AssessmentWeekStart = Week.AddDays(-70), LastDoneDay = Week.AddDays(-14) };
        Assert.Equal(new PhaseDecision(WorkoutPhase.Moderate, PhaseReason.Established), WorkoutPhaseRules.Decide(fourteen, Week));
    }

    [Fact]
    public void ADoneSessionTwoWeeksBackIsOnlyOneWeekOff()
    {
        // Done on the Monday two weeks back: the week after it was empty, this one is just starting.
        var history = new TrainingHistory { AssessmentWeekStart = Week.AddDays(-70), LastDoneDay = Week.AddDays(-14) };
        var lastWeek = new TrainingHistory { AssessmentWeekStart = Week.AddDays(-70), LastDoneDay = Week.AddDays(-8) };
        var sundayBefore = new TrainingHistory { AssessmentWeekStart = Week.AddDays(-70), LastDoneDay = Week.AddDays(-1) };

        Assert.Equal(WorkoutPhase.Moderate, WorkoutPhaseRules.Decide(history, Week).Phase);
        Assert.Equal(WorkoutPhase.Moderate, WorkoutPhaseRules.Decide(lastWeek, Week).Phase);
        Assert.Equal(WorkoutPhase.Moderate, WorkoutPhaseRules.Decide(sundayBefore, Week).Phase);
    }

    [Fact]
    public void ALongBreakWithNoRecordOfAnAssessmentReRuns()
    {
        var history = new TrainingHistory { LastDoneDay = Week.AddDays(-40) };

        Assert.Equal(new PhaseDecision(WorkoutPhase.Assessment, PhaseReason.TwoWeeksOff), WorkoutPhaseRules.Decide(history, Week));
    }

    [Fact]
    public void ActivityWithNoAssessmentOnRecordIsNotAFirstInstall()
    {
        var history = new TrainingHistory { LastDoneDay = Week.AddDays(-3) };

        Assert.Equal(new PhaseDecision(WorkoutPhase.Moderate, PhaseReason.Established), WorkoutPhaseRules.Decide(history, Week));
    }

    [Fact]
    public void AnAssessmentWeekWithNothingDoneRunsAgainAtTwoWeeks()
    {
        var oneWeek = new TrainingHistory { AssessmentWeekStart = Week.AddDays(-7) };
        var twoWeeks = new TrainingHistory { AssessmentWeekStart = Week.AddDays(-14) };

        Assert.Equal(WorkoutPhase.Moderate, WorkoutPhaseRules.Decide(oneWeek, Week).Phase);
        Assert.Equal(new PhaseDecision(WorkoutPhase.Assessment, PhaseReason.TwoWeeksOff), WorkoutPhaseRules.Decide(twoWeeks, Week));
    }

    [Fact]
    public void AReRunStartsTheCountAgain()
    {
        // Long off, then re-assessed last week: the old gap no longer counts.
        var history = new TrainingHistory { AssessmentWeekStart = Week.AddDays(-7), LastDoneDay = Week.AddDays(-60) };

        Assert.Equal(new PhaseDecision(WorkoutPhase.Moderate, PhaseReason.Established), WorkoutPhaseRules.Decide(history, Week));
    }

    [Fact]
    public void ReplanningTheAssessmentWeekStaysAssessment()
    {
        var history = TrainingHistory.Fresh.WithAssessmentWeek(Week);

        Assert.Equal(Week, history.AssessmentWeekStart);
        Assert.Equal(new PhaseDecision(WorkoutPhase.Assessment, PhaseReason.AssessmentUnderway), WorkoutPhaseRules.Decide(history, Week));
    }

    [Fact]
    public async Task TwoWeeksOffPlansTheAssessmentWeek()
    {
        var histories = new Dictionary<string, TrainingHistory>
        {
            [Sam] = new() { AssessmentWeekStart = Week.AddDays(-70), LastDoneDay = Week.AddDays(-20) },
        };

        var plan = await PhasedWeek.PlanAsync(Request(Calendars(), bothAdults: false), histories, Fallback.Offline(), CancellationToken.None);

        Assert.Equal(WorkoutPhase.Assessment, plan.Phases[Sam].Phase);
        Assert.Equal(3, plan.Assessments.Count);
        Assert.Equal(3, SamOf(plan.Plan).Sessions.Count);
    }

    // ---- WeekTwoStartsModerate ----

    [Fact]
    public void WeekTwoStartsModerate()
    {
        var history = new TrainingHistory { AssessmentWeekStart = Week.AddDays(-7), LastDoneDay = Week.AddDays(-3) };
        Assert.Equal(new PhaseDecision(WorkoutPhase.Moderate, PhaseReason.Established), WorkoutPhaseRules.Decide(history, Week));
    }

    [Fact]
    public async Task WeekTwoIsTheOrdinaryPlan()
    {
        var histories = new Dictionary<string, TrainingHistory>
        {
            [Sam] = new() { AssessmentWeekStart = Week.AddDays(-7), LastDoneDay = Week.AddDays(-3) },
        };
        var request = Request(Calendars(), bothAdults: false);

        var phased = await PhasedWeek.PlanAsync(request, histories, Fallback.Offline(), CancellationToken.None);
        var ordinary = SamOf(WorkoutPlanner.Plan(request)).Sessions;
        var got = SamOf(phased.Plan).Sessions;

        Assert.Empty(phased.Assessments);
        Assert.NotEmpty(ordinary);
        Assert.Equal(Shape(ordinary), Shape(got));
        Assert.Contains(got, item => item.Session.Kind == SessionKind.Strength && item.Session.Duration == TimeSpan.FromMinutes(40));
    }

    [Fact]
    public async Task EachPersonHasTheirOwnPhase()
    {
        var histories = new Dictionary<string, TrainingHistory>
        {
            [Sam] = TrainingHistory.Fresh,
            [Lee] = new() { AssessmentWeekStart = Week.AddDays(-14), LastDoneDay = Week.AddDays(-2) },
        };

        var phased = await PhasedWeek.PlanAsync(Request(Calendars()), histories, Fallback.Offline(), CancellationToken.None);

        Assert.Equal(WorkoutPhase.Assessment, phased.Phases[Sam].Phase);
        Assert.Equal(WorkoutPhase.Moderate, phased.Phases[Lee].Phase);
        Assert.Equal(3, phased.Assessments.Count);
        Assert.All(phased.Assessments, item => Assert.Equal([Sam], item.Placed.Participants));
        Assert.Equal(3, SamOf(phased.Plan).Sessions.Count);
        Assert.Contains(LeeOf(phased.Plan).Sessions, item => item.Session.Duration > TimeSpan.FromMinutes(30));
        Assert.Equal([Sam, Lee], phased.Plan.Plans.Select(item => item.Person).ToArray());
    }

    [Fact]
    public async Task AnAssessmentPersonTakesPartInNoJointSession()
    {
        var histories = new Dictionary<string, TrainingHistory>
        {
            [Sam] = TrainingHistory.Fresh,
            [Lee] = new() { AssessmentWeekStart = Week.AddDays(-14), LastDoneDay = Week.AddDays(-2) },
        };

        var phased = await PhasedWeek.PlanAsync(Request(Calendars()), histories, Fallback.Offline(), CancellationToken.None);

        Assert.Empty(phased.Plan.JointSessions);
        Assert.DoesNotContain(SamOf(phased.Plan).Sessions, item => item.IsJoint);
    }

    [Fact]
    public async Task TwoEstablishedAdultsAreThePlainPlanner()
    {
        var histories = new Dictionary<string, TrainingHistory>
        {
            [Sam] = new() { AssessmentWeekStart = Week.AddDays(-14), LastDoneDay = Week.AddDays(-2) },
            [Lee] = new() { AssessmentWeekStart = Week.AddDays(-14), LastDoneDay = Week.AddDays(-2) },
        };
        var request = Request(Calendars());

        var phased = await PhasedWeek.PlanAsync(request, histories, Fallback.Offline(), CancellationToken.None);
        var plain = WorkoutPlanner.Plan(request);

        Assert.Equal(Shape(SamOf(plain).Sessions), Shape(SamOf(phased.Plan).Sessions));
        Assert.Equal(Shape(LeeOf(plain).Sessions), Shape(LeeOf(phased.Plan).Sessions));
        Assert.Equal(Shape(plain.JointSessions), Shape(phased.Plan.JointSessions));
    }

    [Fact]
    public async Task AMissingHistoryIsRefusedNotGuessed()
    {
        var histories = new Dictionary<string, TrainingHistory> { [Sam] = TrainingHistory.Fresh };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            PhasedWeek.PlanAsync(Request(Calendars()), histories, Fallback.Offline(), CancellationToken.None));
    }

    [Fact]
    public async Task AssessmentWeeksNeverReachTheAuthor()
    {
        var author = new CountingAuthor();
        var histories = new Dictionary<string, TrainingHistory> { [Sam] = TrainingHistory.Fresh, [Lee] = TrainingHistory.Fresh };

        var phased = await PhasedWeek.PlanAsync(Request(Calendars()), histories, new PlanWithFallback(author, new RecordingLog()), CancellationToken.None);

        Assert.Equal(0, author.Calls);
        Assert.Null(phased.Moderate);
        Assert.Equal(6, phased.Assessments.Count);
    }

    [Fact]
    public async Task OnlyTheEstablishedPeopleAreSentToTheAuthor()
    {
        var author = new CountingAuthor();
        var histories = new Dictionary<string, TrainingHistory>
        {
            [Sam] = TrainingHistory.Fresh,
            [Lee] = new() { AssessmentWeekStart = Week.AddDays(-14), LastDoneDay = Week.AddDays(-2) },
        };

        var phased = await PhasedWeek.PlanAsync(Request(Calendars()), histories, new PlanWithFallback(author, new RecordingLog()), CancellationToken.None);

        Assert.Equal(1, author.Calls);
        Assert.Equal([Lee], author.Asked.Select(item => item.Busy.Person).ToArray());
        Assert.NotNull(phased.Moderate);
    }
}
