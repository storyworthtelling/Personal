using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>The source the tab reads: absent is "no plan yet", unreadable says so in its own sentence, and a swap is kept in the saved week.</summary>
public sealed class ReportWorkoutsSourceTests
{
    private static readonly Session Strong = new(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [new("Squat"), new("Row")]);

    private static readonly DateTimeOffset Monday = Est(11, 2, 7);

    private static StoredWorkoutWeek Record() => new(
        WeekStart,
        Est(11, 2, 5, 30),
        [new StoredSession(Sam, new DateOnly(2026, 11, 2), Est(11, 2, 16), "Strength", 40, false, [Sam], "None", ["Squat", "Row"], 132)],
        [],
        [],
        []);

    private sealed record Rig(FakeReports Reports, FakeIssued Issued, FakeLedgerStore Ledger, FakeRequests Requests, ReportWorkoutsSource Source);

    private static Rig Make(WorkoutWeekRecordRead? read = null, string? owner = Sam)
    {
        var reports = new FakeReports(read ?? new WorkoutWeekRecordRead.Held(Record()));
        var issued = new FakeIssued(KeyAssignmentsRead.Of(KeyAssignments.Empty));
        var ledger = new FakeLedgerStore(new SkipLedgerRead(SkipLedger.Empty, 0, false));
        var requests = new FakeRequests(new WeekRequestRead.Ready(Request(Calendars(), now: Monday)));
        return new Rig(reports, issued, ledger, requests, new ReportWorkoutsSource(reports, issued, ledger, requests, _ => false, owner));
    }

    [Fact]
    public void NoReportYetIsNoPlanYet()
    {
        var rig = Make(new WorkoutWeekRecordRead.NothingYet());

        Assert.IsType<WorkoutsRead.NoPlanYet>(rig.Source.Read(Monday));
        Assert.Equal(0, rig.Requests.Asked);
    }

    [Fact]
    public void AnUnreadableReportSaysSoInItsOwnSentence()
    {
        var rig = Make(new WorkoutWeekRecordRead.Unreadable("The saved week could not be read."));

        var read = Assert.IsType<WorkoutsRead.Unavailable>(rig.Source.Read(Monday));

        Assert.Equal("The saved week could not be read.", read.Reason);
    }

    [Fact]
    public void ARecordOfIssuedNumbersThatCannotBeReadIsNotReplacedByAnEmptyOne()
    {
        var rig = Make();
        rig.Issued.Read = KeyAssignmentsRead.Unreadable("The record of issued numbers could not be read.");

        var read = Assert.IsType<WorkoutsRead.Unavailable>(rig.Source.Read(Monday));

        Assert.Equal("The record of issued numbers could not be read.", read.Reason);
    }

    [Fact]
    public void ALedgerThatCannotBeReadIsNotReadAsNoSkips()
    {
        var rig = Make();
        rig.Ledger.Read = new SkipLedgerRead(SkipLedger.Empty, 0, true);

        var read = Assert.IsType<WorkoutsRead.Unavailable>(rig.Source.Read(Monday));

        Assert.Equal(ReportWorkoutsSource.LedgerUnreadable, read.Reason);
    }

    [Fact]
    public void AnInstallWithNoOwnerMarkedHasNoTabToShow()
    {
        var rig = Make(owner: null);

        var read = Assert.IsType<WorkoutsRead.Unavailable>(rig.Source.Read(Monday));

        Assert.Equal(ReportWorkoutsSource.NoOwner, read.Reason);
    }

    [Fact]
    public void AWeekThatCannotBeSetUpSaysWhy()
    {
        var rig = Make();
        rig.Requests.Answer = new WeekRequestRead.Unavailable("The household has not been set up yet.");

        var read = Assert.IsType<WorkoutsRead.Unavailable>(rig.Source.Read(Monday));

        Assert.Equal("The household has not been set up yet.", read.Reason);
    }

    [Fact]
    public void AReadableReportIsThePlanWithTheIssuedNumbers()
    {
        var rig = Make();

        var snapshot = Assert.IsType<WorkoutsRead.Ready>(rig.Source.Read(Monday)).Snapshot;

        Assert.NotNull(snapshot.Assignments);
        Assert.Equal("Strength", snapshot.SessionOn(new DateOnly(2026, 11, 2))!.Session.Kind.ToString());
    }

    [Fact]
    public void ASwapIsKeptInTheSavedWeek()
    {
        var rig = Make();
        var snapshot = Assert.IsType<WorkoutsRead.Ready>(rig.Source.Read(Monday)).Snapshot;
        var old = snapshot.SessionOn(new DateOnly(2026, 11, 2))!;
        var moved = old with { Day = new DateOnly(2026, 11, 3), Start = Est(11, 3, 16) };

        rig.Source.Record(new WorkoutsChange.Swapped(new SwapResult(Sam, [new SessionMove(old, moved, null)], snapshot.Weeks, snapshot.Weeks)));

        Assert.Equal(new DateOnly(2026, 11, 3), Assert.Single(rig.Reports.Saved!.Sessions).Day);
        var again = Assert.IsType<WorkoutsRead.Ready>(rig.Source.Read(Monday)).Snapshot;
        Assert.NotNull(again.SessionOn(new DateOnly(2026, 11, 3)));
    }

    [Fact]
    public void ASkipAddsNothingHereBecauseTheLedgerHoldsIt()
    {
        var rig = Make();

        var snapshot = Assert.IsType<WorkoutsRead.Ready>(rig.Source.Read(Monday)).Snapshot;
        var session = snapshot.SessionOn(new DateOnly(2026, 11, 2))!;

        rig.Source.Record(new WorkoutsChange.SkipCommitted(new PendingSkip(Sam, session.Day, session.Session.Kind, session, null, null, Monday)));

        Assert.Null(rig.Reports.Saved);
    }

    [Fact]
    public void ChangingAnAbsentReportSaysThereIsNothingToChange()
    {
        var rig = Make(new WorkoutWeekRecordRead.NothingYet());

        Assert.Throws<InvalidOperationException>(() =>
            rig.Source.Record(new WorkoutsChange.Swapped(new SwapResult(Sam, [], [], []))));
    }
}
