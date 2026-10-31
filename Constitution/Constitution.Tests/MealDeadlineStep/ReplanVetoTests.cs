using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>A replan of the denied evenings, through the committed planner and its existing never-again mechanism, without editing either.</summary>
public class ReplanVetoTests
{
    private static async Task<MealWeek> PlanAsync(MealWeekRequest request) =>
        (await new MealWithFallback(new NoMealAuthor(), new Quiet()).PlanAsync(request, CancellationToken.None)).Week;

    private sealed class Quiet : IMealLog
    {
        public void Record(Constitution.Domain.Workouts.PlanOrigin origin, Constitution.Domain.Workouts.AuthorUnavailable? reason)
        {
        }
    }

    [Fact]
    public async Task AVetoedDishIsNotServedAnyEvening()
    {
        var request = Request();
        var first = await PlanAsync(request);
        var dishes = Cooks(first).Select(dinner => dinner.Recipe!.Value).Distinct().ToList();
        Assert.NotEmpty(dishes);

        var replanned = await PlanAsync(ReplanVeto.Apply(request, dishes.Take(2).ToList()));

        Assert.DoesNotContain(Cooks(replanned), dinner => dishes.Take(2).Contains(dinner.Recipe!.Value));
        Assert.True(replanned.IsPlanned);
    }

    [Fact]
    public void TheVetoCoversEveryAdultAndKeepsTheRequestsOwn()
    {
        var request = Request(neverAgain: Never("lentil-curry", "Lee"));

        var vetoed = ReplanVeto.Apply(request, [Id("corn-tacos"), Id("lentil-curry")]);

        Assert.Equal(["Lee", "Sam"], vetoed.NeverAgain[Id("corn-tacos")].Order());
        Assert.Equal(["Lee", "Sam"], vetoed.NeverAgain[Id("lentil-curry")].Order());
    }

    [Fact]
    public void TheVetoIsOnlyForTheRequestItIsAppliedTo()
    {
        var request = Request();

        _ = ReplanVeto.Apply(request, [Id("corn-tacos")]);

        Assert.Empty(request.NeverAgain);
    }

    [Fact]
    public void NoDishesLeavesTheRequestAsItWas()
    {
        var request = Request();

        Assert.Same(request, ReplanVeto.Apply(request, []));
    }
}
