using Constitution.Domain.Delivery;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.MealWorker;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.SharedWeek.SharedWeekData;

namespace Constitution.Tests.SharedWeek;

/// <summary>When the pass wakes the part that serves the other phone's week, and what it costs when nothing is due.</summary>
public class SharedWeekPassTests
{
    private static readonly DateTimeOffset Quiet = Edt(10, 25, 12);
    private static readonly DateTimeOffset BeforeDeadline = Edt(10, 22, 12);
    private static readonly DateTimeOffset InsideHour = Edt(10, 25, 16, 30);

    // The plan for the day is settled earlier that morning, so only the shared part can be due.
    private static MealPassStampRead.Held SettledFor(DateTimeOffset now)
    {
        var morning = new DateTimeOffset(now.Year, now.Month, now.Day, 6, 5, 0, now.Offset);
        return MealWorkerData.Stamp(morning, Constitution.Domain.Calendar.PlanningWeek.StartOf(now, Zone).AddDays(7), morning, morning);
    }

    private static MealPassWork Decide(DateTimeOffset now, IReadOnlyList<SharedWeekPlan>? shared, string? person = Lee, Func<string, DeliveredMealDocument?>? delivered = null) =>
        MealPassPlan.Decide(
            MealPassTrigger.Scheduled, now, Zone, SettledFor(now), new MealWeekRecordRead.NothingYet(), new MealDecisionRecordRead.NothingYet(), person, delivered, shared);

    [Fact]
    public void ASheetWaitingBeforeItsDeadlineIsDue()
    {
        Assert.True(Decide(BeforeDeadline, [Plan()]).SharedWeek);
    }

    [Fact]
    public void ARecipeInItsHourIsDue()
    {
        var work = Decide(InsideHour, [Plan()]);

        Assert.True(work.SharedWeek);
        Assert.False(work.RecipeHour);
    }

    [Fact]
    public void NothingWaitingMeansNothingIsDue()
    {
        Assert.False(Decide(Quiet, [Plan()]).SharedWeek);
        Assert.False(Decide(BeforeDeadline, []).SharedWeek);
        Assert.False(Decide(BeforeDeadline, null).SharedWeek);
        Assert.False(Decide(BeforeDeadline, [Plan()], person: null).SharedWeek);
    }

    [Fact]
    public void ADeliveredPageIsNotWaiting()
    {
        var sent = new DeliveredMealDocument("x", DeliveryKind.Meals, Lee, new DateOnly(2026, 10, 25), Deadline, "f");

        Assert.False(Decide(BeforeDeadline, [Plan()], delivered: _ => sent).SharedWeek);
    }

    [Fact]
    public void TheOlderCallsAreUnchanged()
    {
        var work = MealPassPlan.Decide(
            MealPassTrigger.Scheduled, Quiet, Zone, SettledFor(Quiet), new MealWeekRecordRead.NothingYet(), new MealDecisionRecordRead.NothingYet());

        Assert.False(work.Any);
        Assert.False(work.SharedWeek);
    }

    [Fact]
    public void ASharedWeekAloneMakesTheWorkAny()
    {
        Assert.True(new MealPassWork(false, false, false, true).Any);
    }

    private sealed class Seen(PassRig rig)
    {
        public int SharedReads { get; set; }

        public IReadOnlyList<SharedWeekPlan> Plans { get; set; } = [];

        public MealPassSeams Seams => rig.Seams with
        {
            SharedPlans = () =>
            {
                SharedReads++;
                rig.Diary.Add("read shared");
                return Plans;
            },
        };
    }

    [Fact]
    public async Task ThePassRunsTheRecipesPartForASharedWeek()
    {
        var rig = new PassRig { Person = Lee, StampRead = SettledFor(BeforeDeadline) };
        var seen = new Seen(rig) { Plans = [Plan()] };

        var result = await MealWeekPass.RunAsync(MealPassTrigger.Scheduled, BeforeDeadline, Zone, seen.Seams, CancellationToken.None);

        Assert.IsType<MealPassStep<MealRecipesOutcome>.Done>(result.Recipes);
        Assert.Contains("recipes", rig.Diary);
    }

    [Fact]
    public async Task ANormalPassOutsideEveryHourReadsTheSharedPlansOnceAndRunsNothing()
    {
        var rig = new PassRig { Person = Lee, StampRead = SettledFor(Quiet) };
        var seen = new Seen(rig) { Plans = [Plan()] };

        var result = await MealWeekPass.RunAsync(MealPassTrigger.Scheduled, Quiet, Zone, seen.Seams, CancellationToken.None);

        Assert.True(result.NothingRan);
        Assert.Equal(1, seen.SharedReads);
        Assert.DoesNotContain("recipes", rig.Diary);
    }

    [Fact]
    public async Task APassWithNoSharedSeamIsAsItWas()
    {
        var rig = new PassRig { Person = Lee, StampRead = SettledFor(BeforeDeadline) };

        var result = await MealWeekPass.RunAsync(MealPassTrigger.Scheduled, BeforeDeadline, Zone, rig.Seams, CancellationToken.None);

        Assert.True(result.NothingRan);
    }
}
