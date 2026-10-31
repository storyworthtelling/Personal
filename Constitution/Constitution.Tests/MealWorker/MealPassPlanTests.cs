using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWorker.MealWorkerData;

namespace Constitution.Tests.MealWorker;

/// <summary>What the pass finds due. Everything it reads is handed in, so every case is a moment and three small records.</summary>
public class MealPassPlanTests
{
    private static readonly MealWeekRecordRead NoWeeks = new MealWeekRecordRead.NothingYet();
    private static readonly MealDecisionRecordRead NoDecisions = new MealDecisionRecordRead.NothingYet();
    private static readonly MealPassStampRead NoStamp = new MealPassStampRead.NothingYet();

    private static MealPassWork Look(
        DateTimeOffset now,
        MealPassStampRead? stamp = null,
        MealWeekRecordRead? weeks = null,
        MealDecisionRecordRead? decisions = null,
        MealPassTrigger trigger = MealPassTrigger.Scheduled) =>
        MealPassPlan.Decide(trigger, now, Zone, stamp ?? NoStamp, weeks ?? NoWeeks, decisions ?? NoDecisions);

    // ---- planning the week ----

    [Fact]
    public void AFreshPhonePlansAndDecidesNothing()
    {
        var work = Look(Edt(10, 26, 7, 5));

        Assert.True(work.PlanWeek);
        Assert.False(work.Deadline);
    }

    [Fact]
    public void ADayThatPlannedAlreadyIsQuiet()
    {
        var work = Look(Edt(10, 26, 7, 5), Stamp(Edt(10, 26, 6, 10), NextSunday));

        Assert.False(work.Any);
    }

    [Fact]
    public void TheNextDaysFirstPassPlansAgain()
    {
        var work = Look(Edt(10, 27, 6, 5), Stamp(Edt(10, 26, 9), NextSunday));

        Assert.True(work.PlanWeek);
    }

    [Fact]
    public void BeforeTheDaysPlanTimeYesterdaysPlanStands()
    {
        var work = Look(Edt(10, 26, 5, 5), Stamp(Edt(10, 25, 7, 5), NextSunday));

        Assert.False(work.PlanWeek);
    }

    [Fact]
    public void AWeekThatRolledOverPlansAgainAtOnce()
    {
        // Saturday night the run planned for Sunday 25 October; at three on that Sunday morning the week to plan is the next one.
        var work = Look(Edt(10, 25, 3), Stamp(Edt(10, 24, 23), Sunday));

        Assert.True(work.PlanWeek);
    }

    [Fact]
    public void AClockSetBackPlansAgain()
    {
        var work = Look(Edt(10, 26, 7, 5), Stamp(Edt(10, 27, 9), NextSunday));

        Assert.True(work.PlanWeek);
    }

    [Fact]
    public void ATryThatDidNotSettleWaitsOutTheGapOnTheSchedule()
    {
        var soon = Look(Edt(10, 26, 8, 5), Stamp(planTried: Edt(10, 26, 7, 5)));
        var later = Look(Edt(10, 26, 10, 5), Stamp(planTried: Edt(10, 26, 7, 5)));

        Assert.False(soon.PlanWeek);
        Assert.True(later.PlanWeek);
    }

    [Fact]
    public void OpeningTheAppTriesAgainAtOnce()
    {
        var work = Look(Edt(10, 26, 7, 6), Stamp(planTried: Edt(10, 26, 7, 5)), trigger: MealPassTrigger.Opened);

        Assert.True(work.PlanWeek);
    }

    [Fact]
    public void OpeningTheAppAfterAPlanThatSettledDoesNothing()
    {
        var work = Look(Edt(10, 26, 7, 6), Stamp(Edt(10, 26, 7, 5), NextSunday, Edt(10, 26, 7, 5)), trigger: MealPassTrigger.Opened);

        Assert.False(work.Any);
    }

    [Fact]
    public void ATryFromTheFutureDoesNotWait()
    {
        var work = Look(Edt(10, 26, 8, 5), Stamp(planTried: Edt(10, 27, 7, 5)));

        Assert.True(work.PlanWeek);
    }

    [Fact]
    public void AnUnreadableStampCostsOneRun()
    {
        var work = Look(Edt(10, 26, 7, 5), new MealPassStampRead.Unreadable("could not be opened"));

        Assert.True(work.PlanWeek);
    }

    // ---- the deadline ----

    [Fact]
    public void ADeadlinePassedAndUndecidedIsDue()
    {
        var work = Look(Edt(10, 25, 8, 5), weeks: Kept(Sunday));

        Assert.True(work.Deadline);
    }

    [Fact]
    public void BeforeTheDeadlineNothingIsDue()
    {
        var work = Look(Edt(10, 25, 7, 5), Stamp(Edt(10, 25, 6, 5), NextSunday.AddDays(-7)), Kept(Sunday));

        Assert.False(work.Deadline);
    }

    [Fact]
    public void ADecidedWeekIsNotDueAgain()
    {
        var work = Look(Edt(10, 25, 9, 5), weeks: Kept(Sunday), decisions: Decided(Sunday));

        Assert.False(work.Deadline);
    }

    [Fact]
    public void JudgingStillPendingIsDueAgainAfterTheGap()
    {
        var soon = Look(Edt(10, 25, 10, 5), Stamp(deadlineTried: Edt(10, 25, 9, 5)), Kept(Sunday), Decided(Sunday, evidencePending: true));
        var later = Look(Edt(10, 25, 13, 5), Stamp(deadlineTried: Edt(10, 25, 9, 5)), Kept(Sunday), Decided(Sunday, evidencePending: true));

        Assert.False(soon.Deadline);
        Assert.True(later.Deadline);
    }

    [Fact]
    public void OpeningTheAppDoesNotSkipTheDeadlinesGap()
    {
        var work = Look(Edt(10, 25, 10, 5), Stamp(deadlineTried: Edt(10, 25, 9, 5)), Kept(Sunday), Decided(Sunday, evidencePending: true), MealPassTrigger.Opened);

        Assert.False(work.Deadline);
    }

    [Fact]
    public void AnUnreadableRecordMakesTheDeadlineDueSoTheStepCanSayWhy()
    {
        var weeks = Look(Edt(10, 25, 9), weeks: new MealWeekRecordRead.Unreadable("unreadable"));
        var decisions = Look(Edt(10, 25, 9), weeks: Kept(Sunday), decisions: new MealDecisionRecordRead.Unreadable("unreadable"));

        Assert.True(weeks.Deadline);
        Assert.True(decisions.Deadline);
    }

    [Fact]
    public void APhoneThatPlansNothingNeverHasADeadline()
    {
        var work = Look(Edt(10, 25, 9), weeks: NoWeeks, decisions: Decided(Sunday));

        Assert.False(work.Deadline);
    }

    // ---- what settles a plan ----

    [Fact]
    public void OnlyAnAnswerThatCannotChangeTodaySettles()
    {
        Assert.True(MealPassPlan.Settles(new MealWeekOutcome.NotPlanned(MealNotPlannedBecause.NotThePlanner, "The other phone plans.")));
        Assert.True(MealPassPlan.Settles(new MealWeekOutcome.Planned(Report())));

        foreach (var because in Enum.GetValues<MealNotPlannedBecause>().Where(item => item != MealNotPlannedBecause.NotThePlanner))
        {
            Assert.False(MealPassPlan.Settles(new MealWeekOutcome.NotPlanned(because, "A reason.")), because.ToString());
        }
    }

    internal static MealWeekReport Report() =>
        new(WeekOf(NextSunday), Constitution.Domain.Workouts.PlanOrigin.RulesOnly, null, [], null, true, MealRecordSave.Saved, false);
}
