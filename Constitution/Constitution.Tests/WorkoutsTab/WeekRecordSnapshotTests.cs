using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.WorkoutPipeline;
using Constitution.Tests.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>The saved week read back into what the tab is made from: it must be the plan the run placed, and never a plan that looks real and is not.</summary>
public sealed class WeekRecordSnapshotTests
{
    private static readonly Session Strong = new(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [new("Squat"), new("Row")]);
    private static readonly Session Walk = new(SessionKind.GentleWalk, TimeSpan.FromMinutes(30), false, [new("Easy walk")]);

    private static readonly DateTimeOffset Monday = Est(11, 2, 7);

    private static async Task<(WorkoutWeekReport Report, StoredWorkoutWeek Record)> Planned(Action<WeekRig>? adjust = null)
    {
        using var rig = new WeekRig();
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Strong), rig.Session(Sam, 2, 7, Walk), rig.Session(Lee, 1, 7, Walk)));
        adjust?.Invoke(rig);
        var report = await rig.PlannedAsync();
        return (report, WorkoutWeekRecordBuilder.From(report, KeyAssignments.Empty, Est(11, 2, 5, 30), new DateOnly(2026, 11, 2)));
    }

    private static WeekReadInputs Inputs(DateTimeOffset now, Func<DateOnly, bool>? pages = null, KeyAssignments? issued = null) =>
        new(Sam, now, Request(Calendars(), now: now), issued ?? KeyAssignments.Empty, SkipLedger.Empty, pages ?? (_ => false));

    private static WorkoutsSnapshot Ready(WorkoutsRead read) => Assert.IsType<WorkoutsRead.Ready>(read).Snapshot;

    [Fact]
    public async Task TheSnapshotHoldsThePlacedPlanTheRunMade()
    {
        var (report, record) = await Planned();

        var snapshot = Ready(WeekRecordSnapshot.From(record, Inputs(Monday)));

        foreach (var person in new[] { Sam, Lee })
        {
            var made = report.PlacedPlan.PlanFor(person)!.Sessions.Select(Shape).ToList();
            var read = snapshot.Weeks.Single().Plan.PlanFor(person)!.Sessions.Select(Shape).ToList();
            Assert.Equal(made, read);
        }
    }

    private static string Shape(PlannedSession session) =>
        $"{session.Day:yyyy-MM-dd} {session.Start:o} {session.Session.Kind} {session.Session.Duration} {session.Session.IsSweaty} " +
        $"{string.Join('|', session.Session.Exercises.Select(item => item.Name))} {string.Join('|', session.Participants)} {session.Together}";

    [Fact]
    public async Task ItIsTheOwnersTabAndTheOwnersPlan()
    {
        var (_, record) = await Planned();

        var snapshot = Ready(WeekRecordSnapshot.From(record, Inputs(Monday)));

        Assert.Equal(Sam, snapshot.Person);
        Assert.Equal(2, snapshot.PersonPlanOf(new DateOnly(2026, 11, 2))!.Sessions.Count);
        Assert.NotNull(snapshot.SessionOn(new DateOnly(2026, 11, 2)));
        Assert.Null(snapshot.SessionOn(new DateOnly(2026, 11, 3)));
    }

    [Fact]
    public async Task TheScoreAndTheCalendarTimeComeFromTheRecord()
    {
        var (_, record) = await Planned();
        var day = new DateOnly(2026, 11, 2);

        var facts = Ready(WeekRecordSnapshot.From(record, Inputs(Monday))).FactsOn(day);

        Assert.Equal(record.Sessions.First(item => item.Person == Sam && item.Day == day).ScoreBpm, facts.ScoreBpm);
        Assert.Equal(new PlacedTime(Est(11, 2, 16), Est(11, 2, 16, 40)), facts.OnCalendar);
    }

    [Fact]
    public async Task APageOnTheTabletIsSaidOnlyWhenOneWentThere()
    {
        var (_, record) = await Planned();
        var day = new DateOnly(2026, 11, 2);

        var sent = Ready(WeekRecordSnapshot.From(record, Inputs(Monday, asked => asked == day)));
        var notSent = Ready(WeekRecordSnapshot.From(record, Inputs(Monday)));

        Assert.True(sent.FactsOn(day).PagesOnTablet);
        Assert.False(sent.FactsOn(new DateOnly(2026, 11, 4)).PagesOnTablet);
        Assert.False(notSent.FactsOn(day).PagesOnTablet);
    }

    [Fact]
    public async Task AReasonForAChangeIsShownOnItsDayAlone()
    {
        var (_, record) = await Planned();
        var edited = record with
        {
            Reasons = [new StoredReason(Sam, new DateOnly(2026, 11, 4), "Rain is due, so the walk moves indoors."), new StoredReason(Lee, new DateOnly(2026, 11, 2), "Not Sam's.")],
        };

        var snapshot = Ready(WeekRecordSnapshot.From(edited, Inputs(Monday)));

        Assert.Equal("Rain is due, so the walk moves indoors.", snapshot.FactsOn(new DateOnly(2026, 11, 4)).ChangeReason);
        Assert.Null(snapshot.FactsOn(new DateOnly(2026, 11, 2)).ChangeReason);
    }

    [Fact]
    public async Task TheIssuedNumbersAndTheLedgerRideAlong()
    {
        var (_, record) = await Planned();
        var issued = KeyAssignments.Empty.With([new KeyIssue(Sam, 105703, SessionKind.Strength, DayOfWeek.Monday, 0, 4)]);

        var snapshot = Ready(WeekRecordSnapshot.From(record, Inputs(Monday, issued: issued)));

        Assert.Same(issued, snapshot.Assignments);
        Assert.Same(SkipLedger.Empty, snapshot.Skips);
    }

    [Fact]
    public async Task AWeekThatHasEndedIsNoPlanYetNotAnEmptyOne()
    {
        var (_, record) = await Planned();

        var read = WeekRecordSnapshot.From(record, Inputs(Est(11, 9, 7)));

        Assert.IsType<WorkoutsRead.NoPlanYet>(read);
    }

    [Fact]
    public async Task TheLastDayOfTheWeekStillReadsIt()
    {
        var (_, record) = await Planned();

        Assert.IsType<WorkoutsRead.Ready>(WeekRecordSnapshot.From(record, Inputs(Est(11, 7, 23, 30))));
    }

    [Fact]
    public async Task APersonWithNoSessionsIsAPlanWithNothingInIt()
    {
        var (_, record) = await Planned();
        var onlyLee = record with { Sessions = record.Sessions.Where(item => item.Person == Lee).ToList() };

        var snapshot = Ready(WeekRecordSnapshot.From(onlyLee, Inputs(Monday)));

        Assert.NotNull(snapshot.PersonPlanOf(new DateOnly(2026, 11, 2)));
        Assert.Empty(snapshot.PersonPlanOf(new DateOnly(2026, 11, 2))!.Sessions);
    }

    [Fact]
    public async Task ASharedSessionIsOneObjectInEveryPlanAndInTheJointList()
    {
        using var rig = new WeekRig();
        rig.BothAreEstablished();
        var shared = rig.Session(Sam, -1, 14, Walk) with { Participants = [Sam, Lee], Together = Together.AfterBedtime };
        rig.AuthorWith(_ => new HouseholdWorkoutPlan(WeekStart, [new WeeklyPlan(Sam, WeekStart, [shared], null), new WeeklyPlan(Lee, WeekStart, [shared], null)], [shared]));
        var record = WorkoutWeekRecordBuilder.From(await rig.PlannedAsync(), KeyAssignments.Empty, Est(11, 2, 5, 30), new DateOnly(2026, 11, 2));

        var plan = Ready(WeekRecordSnapshot.From(record, Inputs(Monday))).Weeks.Single().Plan;

        var joint = Assert.Single(plan.JointSessions);
        Assert.Same(joint, plan.PlanFor(Sam)!.Sessions.Single());
        Assert.Same(joint, plan.PlanFor(Lee)!.Sessions.Single());
    }

    [Fact]
    public async Task ASwapOnTheTabMovesTheSavedSessionAndNothingElse()
    {
        var (_, record) = await Planned();
        var snapshot = Ready(WeekRecordSnapshot.From(record, Inputs(Monday)));
        var old = snapshot.SessionOn(new DateOnly(2026, 11, 2))!;
        var moved = old with { Day = new DateOnly(2026, 11, 3), Start = Est(11, 3, 16) };
        var swap = new SwapResult(Sam, [new SessionMove(old, moved, null)], snapshot.Weeks, snapshot.Weeks);

        var next = WeekRecordEdit.Apply(record, swap);

        var edited = Assert.Single(next.Sessions, item => item.Person == Sam && item.Day == new DateOnly(2026, 11, 3));
        Assert.Equal(Est(11, 3, 16), edited.Start);
        Assert.DoesNotContain(next.Sessions, item => item.Person == Sam && item.Day == new DateOnly(2026, 11, 2));
        Assert.Equal(record.Sessions.Count, next.Sessions.Count);
        Assert.Contains(next.Sessions, item => item.Person == Lee);
    }

    [Fact]
    public async Task ASwapOfASessionThatIsNotSavedIsRefused()
    {
        var (_, record) = await Planned();
        var snapshot = Ready(WeekRecordSnapshot.From(record, Inputs(Monday)));
        var stranger = snapshot.SessionOn(new DateOnly(2026, 11, 2))! with { Start = Est(11, 2, 19) };
        var swap = new SwapResult(Sam, [new SessionMove(stranger, stranger with { Day = new DateOnly(2026, 11, 3) }, null)], snapshot.Weeks, snapshot.Weeks);

        Assert.Throws<InvalidOperationException>(() => WeekRecordEdit.Apply(record, swap));
    }
}
