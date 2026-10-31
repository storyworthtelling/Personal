using Constitution.Domain.Meals.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWorker.MealWorkerData;

namespace Constitution.Tests.MealWorker;

/// <summary>Which kept week the deadline step decides: the one rule the step and the worker's look at what is due both read.</summary>
public class MealDeadlineTargetTests
{
    private static MealDeadlineChoice Choose(MealWeekRecord kept, MealDecisionRecord decided, DateTimeOffset now) =>
        MealDeadlineTarget.Choose(kept, decided, now, Zone);

    private static MealWeekRecord Weeks(params DateOnly[] starts) => ((MealWeekRecordRead.Held)Kept(starts)).Record;

    [Fact]
    public void NothingKeptHasNothingToDecide()
    {
        var choice = Choose(MealWeekRecord.Empty, MealDecisionRecord.Empty, Edt(10, 25, 9));

        Assert.Equal(MealDeadlineNothingBecause.NoWeekKept, Assert.IsType<MealDeadlineChoice.Nothing>(choice).Because);
    }

    [Fact]
    public void BeforeTheDeadlineHasNothingToDecide()
    {
        var choice = Choose(Weeks(Sunday), MealDecisionRecord.Empty, Edt(10, 25, 7, 59));

        Assert.Equal(MealDeadlineNothingBecause.BeforeTheDeadline, Assert.IsType<MealDeadlineChoice.Nothing>(choice).Because);
    }

    [Fact]
    public void ExactlyAtTheDeadlineTheWeekIsDecided()
    {
        var choice = Choose(Weeks(Sunday), MealDecisionRecord.Empty, Edt(10, 25, 8));

        var decide = Assert.IsType<MealDeadlineChoice.Decide>(choice);
        Assert.Equal(Sunday, decide.Target.WeekStart);
        Assert.Null(decide.Existing);
    }

    [Fact]
    public void TheNewestPassedWeekIsTheTarget()
    {
        var older = Sunday.AddDays(-7);

        var choice = Choose(Weeks(older, Sunday, NextSunday), MealDecisionRecord.Empty, Edt(10, 25, 9));

        Assert.Equal(Sunday, Assert.IsType<MealDeadlineChoice.Decide>(choice).Target.WeekStart);
    }

    [Fact]
    public void ADecidedNewestWeekLeavesAnOlderUndecidedOneAlone()
    {
        var older = Sunday.AddDays(-7);
        var decided = ((MealDecisionRecordRead.Held)Decided(Sunday)).Record;

        var choice = Choose(Weeks(older, Sunday), decided, Edt(10, 25, 9));

        Assert.Equal(MealDeadlineNothingBecause.AlreadyDecided, Assert.IsType<MealDeadlineChoice.Nothing>(choice).Because);
    }

    [Fact]
    public void AWeekWithJudgingStillPendingIsTheTargetAgain()
    {
        var decided = ((MealDecisionRecordRead.Held)Decided(Sunday, evidencePending: true)).Record;

        var choice = Choose(Weeks(Sunday), decided, Edt(10, 25, 9));

        var decide = Assert.IsType<MealDeadlineChoice.Decide>(choice);
        Assert.Equal(Sunday, decide.Target.WeekStart);
        Assert.NotNull(decide.Existing);
        Assert.True(decide.Existing!.EvidencePending);
    }

    [Fact]
    public void AWeekWithARemovalStillDueIsTheTargetAgain()
    {
        var entry = new MealDecisionEntry(Sunday, Edt(10, 25, 8, 5), 7, 1, 8, false) { DeniedEvenings = ["tuesday"], EventsPending = ["tuesday"] };
        var decided = new MealDecisionRecord([entry]);

        var today = Choose(Weeks(Sunday), decided, Edt(10, 27, 9));
        var after = Choose(Weeks(Sunday), decided, Edt(10, 28, 9));

        Assert.IsType<MealDeadlineChoice.Decide>(today);
        Assert.Equal(MealDeadlineNothingBecause.AlreadyDecided, Assert.IsType<MealDeadlineChoice.Nothing>(after).Because);
    }
}
