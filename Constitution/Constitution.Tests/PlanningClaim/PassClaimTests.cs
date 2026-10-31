using Constitution.Tests.Diagnostics;
using Constitution.Domain.Household.Lease;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Household;
using Constitution.Infrastructure.Workouts;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.HouseholdHead;
using Constitution.Tests.MealDeadlineStep;
using Constitution.Tests.MealWeekRun;
using Constitution.Tests.MealWorker;
using Storyworthtelling.Household;
using static Constitution.Tests.Calendar.InventedFamily;
using Run = Constitution.Infrastructure.Meals.MealWeekRun;
using Step = Constitution.Infrastructure.Meals.MealDeadlineStep;

namespace Constitution.Tests.PlanningClaim;

/// <summary>D148 end to end with fakes: the meal run, the deadline step and the pass, each with the lease the head hands them (open the week), over a real household service.</summary>
public class PassClaimTests
{
    private static int ClaimCount(HeadRig rig) => LeaseView.Read(rig.Opened()).Claims.Count;

    [Fact]
    public async Task TheMealRunWritesTheClaimAndStopsAsNotReadyThenPlansWhenSettled()
    {
        using var phone = new HeadRig();
        var meal = new MealWeekRig();
        var seams = meal.Seams with { Lease = new HouseholdPlanningStanding(phone.Service).OpenTheWeekAsync };

        var first = await Run.RunAsync(meal.Inputs(), seams, CancellationToken.None);
        phone.Now = phone.Now.Add(LeaseTerms.SettleWindow).AddMinutes(1);
        var second = await Run.RunAsync(meal.Inputs(), seams, CancellationToken.None);

        Assert.Equal(MealNotPlannedBecause.PlanningNotReady, Assert.IsType<MealWeekOutcome.NotPlanned>(first).Because);
        Assert.Equal(1, ClaimCount(phone));
        Assert.IsType<MealWeekOutcome.Planned>(second);
    }

    [Fact]
    public async Task APartnerBehindALiveOwnerIsNotThePlannerAndThatIsFinal()
    {
        using var owner = new HeadRig();
        using var partner = new HeadRig(owner.Drive, HouseholdFileRole.SharedWith);
        await new HouseholdPlanningStanding(owner.Service).OpenTheWeekAsync(CancellationToken.None);
        var meal = new MealWeekRig();
        var seams = meal.Seams with { Lease = new HouseholdPlanningStanding(partner.Service).OpenTheWeekAsync };

        var outcome = await Run.RunAsync(meal.Inputs(), seams, CancellationToken.None);

        var stopped = Assert.IsType<MealWeekOutcome.NotPlanned>(outcome);
        Assert.Equal(MealNotPlannedBecause.NotThePlanner, stopped.Because);
        Assert.True(MealPassPlan.Settles(stopped));
    }

    [Fact]
    public void NotReadyIsNeverFinalForTheDay()
    {
        Assert.False(MealPassPlan.Settles(new MealWeekOutcome.NotPlanned(MealNotPlannedBecause.PlanningNotReady, "Waiting.")));
    }

    [Fact]
    public async Task AStaleStandingDecidesNothingAndASyncedOneDoes()
    {
        using var phone = new HeadRig();
        var standing = new HouseholdPlanningStanding(phone.Service);
        await standing.OpenTheWeekAsync(CancellationToken.None);
        phone.Now = phone.Now.Add(LeaseTerms.SettleWindow).AddMinutes(1);
        var deadline = await DeadlineRig.PlannedAsync();
        var seams = deadline.Seams with { Lease = standing.OpenTheWeekAsync };
        var inputs = new Constitution.Domain.Meals.Pipeline.MealDeadlineInputs(deadline.Now, Rules());

        phone.Drive.Failure = new IOException("invented outage");
        var stale = await Step.RunAsync(inputs, seams, CancellationToken.None);
        deadline.AssertNothingTouched();
        phone.Drive.Failure = null;
        var fresh = await Step.RunAsync(inputs, seams, CancellationToken.None);

        Assert.Equal(MealDeadlineStoppedBecause.PlanningNotReady, Assert.IsType<MealDeadlineOutcome.Stopped>(stale).Because);
        Assert.IsType<MealDeadlineOutcome.Decided>(fresh);
    }

    [Fact]
    public async Task APassWritesTheClaimAndDoesNotSettleThePlanOnIt()
    {
        using var phone = new HeadRig();
        var meal = new MealWeekRig();
        var pass = new PassRig();
        pass.Plan = async token => new GateResult<MealWeekOutcome>.Ran(
            await Run.RunAsync(meal.Inputs(), meal.Seams with { Lease = new HouseholdPlanningStanding(phone.Service).OpenTheWeekAsync }, token));

        var result = await pass.RunAsync(Edt(10, 26, 7, 5));

        Assert.Equal(1, ClaimCount(phone));
        Assert.IsType<MealPassStep<MealWeekOutcome>.Done>(result.Plan);
        Assert.Null(pass.Saved.Last().PlanSettledAt);
        Assert.NotNull(pass.Saved.Last().PlanTriedAt);
    }

    [Fact]
    public void BothHeadSeamsHandOverTheClaimingLeaseAndNeitherTheReadOnlyOne()
    {
        var services = File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => file.EndsWith("MealWeek/MealWeekServices.cs", StringComparison.Ordinal)));

        Assert.Equal(2, services.Split("household.Planning.OpenTheWeekAsync,").Length - 1);
        Assert.DoesNotContain("Planning.AskAsync", services, StringComparison.Ordinal);
    }
}
