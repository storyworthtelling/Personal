using Codex.Domain.Design;
using Constitution.Domain.Calendar;
using Constitution.Domain.Notices;
using Constitution.Domain.Placement;
using Constitution.Domain.Waiting;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// A week that could not be planned waits behind the dog-ear as one Problem notice, filed under one cause so a second failed run changes
/// nothing, and a run that plans the week takes it away. The reason (not set up, not shared, would not answer) is for the Diagnostics
/// trail; the Waiting page says only what the owner can know and nothing a calendar or a ledger contained.
/// </summary>
public class WorkoutWeekNoticesTests
{
    private static readonly DateTimeOffset Now = Est(11, 4, 6, 45);

    private static WorkoutWeekOutcome NotPlanned(NotPlannedBecause because) =>
        new WorkoutWeekOutcome.NotPlanned(because, [], "A plain sentence.");

    private static WorkoutWeekOutcome Planned()
    {
        var plan = new HouseholdWorkoutPlan(new DateOnly(2026, 11, 2), [], []);
        var request = new WorkoutPlanRequest(plan.WeekStart, Rules(), [], Constitution.Domain.Equipment.EquipmentSet.Everything(), Est(11, 2, 0));
        var report = new WorkoutWeekReport(
            request, plan, plan, new Dictionary<string, PhaseDecision>(), null, [], [], [], false, [], [], [], new PlacementRun(null, [], [], null), []);
        return new WorkoutWeekOutcome.Planned(report);
    }

    [Theory]
    [InlineData(NotPlannedBecause.CalendarsUnreadable)]
    [InlineData(NotPlannedBecause.RecordsUnreadable)]
    [InlineData(NotPlannedBecause.NoOneToPlan)]
    public void AWeekNotPlannedRaisesTheProblemNotice(NotPlannedBecause because)
    {
        var board = new NoticeBoard();

        Assert.True(WorkoutWeekNotices.Apply(board, NotPlanned(because), Now));

        var waiting = Assert.Single(board.Waiting());
        Assert.Equal(NoticeKind.WeekNotPlanned, waiting.Kind);
        Assert.Equal(WorkoutWeekNotices.NotPlannedCause, waiting.CauseKey);
        Assert.Equal(FoldRole.Problem, waiting.Role);
    }

    [Fact]
    public void TheCauseIsAFixedWord()
    {
        Assert.Equal("week:not-planned", WorkoutWeekNotices.NotPlannedCause);
    }

    [Fact]
    public void AnotherFailedRunChangesNothing()
    {
        var board = new NoticeBoard();
        WorkoutWeekNotices.Apply(board, NotPlanned(NotPlannedBecause.CalendarsUnreadable), Now);

        Assert.False(WorkoutWeekNotices.Apply(board, NotPlanned(NotPlannedBecause.RecordsUnreadable), Now.AddHours(1)));

        var waiting = Assert.Single(board.Waiting());
        Assert.Equal(Now, waiting.CreatedAt);
    }

    [Fact]
    public void APlannedWeekTakesTheNoticeAway()
    {
        var board = new NoticeBoard();
        WorkoutWeekNotices.Apply(board, NotPlanned(NotPlannedBecause.CalendarsUnreadable), Now);

        Assert.True(WorkoutWeekNotices.Apply(board, Planned(), Now.AddDays(1)));

        Assert.Empty(board.Waiting());
    }

    [Fact]
    public void APlannedWeekWithNothingWaitingChangesNothing()
    {
        var board = new NoticeBoard();

        Assert.False(WorkoutWeekNotices.Apply(board, Planned(), Now));
        Assert.Empty(board.Waiting());
    }

    [Fact]
    public void ItDoesNotTouchAnotherNotice()
    {
        var board = new NoticeBoard();
        board.Raise(NoticeKind.ReMarkableDelivery, "credential:remarkable", Now);

        WorkoutWeekNotices.Apply(board, Planned(), Now);
        WorkoutWeekNotices.Apply(board, NotPlanned(NotPlannedBecause.NoOneToPlan), Now);

        Assert.Equal(2, board.Waiting().Count);
        Assert.Contains(board.Waiting(), notice => notice.Kind == NoticeKind.ReMarkableDelivery);
    }

    [Fact]
    public void ThePageSaysWhatHappenedAndThatItLooksAgain()
    {
        var board = new NoticeBoard();
        WorkoutWeekNotices.Apply(board, NotPlanned(NotPlannedBecause.CalendarsUnreadable), Now);

        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        Assert.Equal("This week couldn’t be planned", item.Title);
        Assert.Equal(
            "Constitution couldn’t read a calendar or record it needs, so it has not planned the week, written to a calendar or sent a page. It looks again at the next run.",
            item.Body);
        Assert.Null(item.Button);
        Assert.Empty(item.Chips);
        Assert.Equal(FoldRole.Problem, item.Role);
    }

    [Fact]
    public void TheCopyCarriesNoNumberAndNoNameAndNoRefusalOfASignIn()
    {
        var board = new NoticeBoard();
        WorkoutWeekNotices.Apply(board, NotPlanned(NotPlannedBecause.NoOneToPlan), Now);
        var item = Assert.Single(WaitingPageModel.From(board, Now).Items);

        var words = item.Title + " " + item.Body;
        Assert.DoesNotContain(words, character => char.IsDigit(character));
        Assert.DoesNotContain("sign-in was refused", words, StringComparison.OrdinalIgnoreCase);
        foreach (var name in new[] { Sam, Lee, Robin, Juniper })
        {
            Assert.DoesNotContain(name, words, StringComparison.OrdinalIgnoreCase);
        }
    }
}
