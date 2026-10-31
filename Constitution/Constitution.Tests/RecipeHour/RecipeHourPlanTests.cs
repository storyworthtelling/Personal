using Constitution.Domain.Delivery;
using Constitution.Domain.Meals.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWorker.MealWorkerData;

namespace Constitution.Tests.RecipeHour;

/// <summary>What the pass finds due when a recipe is in its hour. Monday's dinner is Sam's, block from 5:15 pm; everything else is quiet.</summary>
public class RecipeHourPlanTests
{
    private static readonly DateTimeOffset Inside = Edt(10, 26, 16, 20);

    private static MealPassWork Look(
        DateTimeOffset now, string? person = Sam, Func<string, DeliveredMealDocument?>? delivered = null, MealPassTrigger trigger = MealPassTrigger.Scheduled) =>
        MealPassPlan.Decide(
            trigger,
            now,
            Zone,
            Stamp(Edt(10, 26, 6, 10), NextSunday),
            Kept(Sunday),
            Decided(Sunday),
            person,
            delivered ?? (_ => null));

    [Fact]
    public void ARecipeInItsHourMakesAPassDue()
    {
        var work = Look(Inside);

        Assert.True(work.RecipeHour);
        Assert.True(work.Any);
        Assert.False(work.PlanWeek);
        Assert.False(work.Deadline);
    }

    [Fact]
    public void OutsideTheHourNothingIsDue()
    {
        Assert.False(Look(Edt(10, 26, 16, 14)).Any);
        Assert.False(Look(Edt(10, 26, 17, 15)).Any);
    }

    [Fact]
    public void ADeliveredPageIsNotDueAgain()
    {
        var name = DeliveredName.For(DeliveryKind.Recipe, Sunday.AddDays(1), Sam);
        var record = new DeliveredMealDocument(name, DeliveryKind.Recipe, Sam, Sunday.AddDays(1), Edt(10, 26, 17, 15), "same");

        var work = Look(Inside, delivered: asked => asked == name ? record : null);

        Assert.False(work.Any);
    }

    [Fact]
    public void ThePartnersDinnerIsNotThisPhonesToSend()
    {
        Assert.False(Look(Inside, person: Lee).RecipeHour);
    }

    [Fact]
    public void WithoutATabletNothingIsDue()
    {
        Assert.False(Look(Inside, person: null).RecipeHour);
    }

    [Fact]
    public void OpeningTheAppFindsTheSameThing()
    {
        Assert.True(Look(Inside, trigger: MealPassTrigger.Opened).RecipeHour);
        Assert.False(Look(Edt(10, 26, 12), trigger: MealPassTrigger.Opened).RecipeHour);
    }

    [Fact]
    public void TheOlderFourArgumentLookStillAsksNoRecipeQuestion()
    {
        var work = MealPassPlan.Decide(MealPassTrigger.Scheduled, Inside, Zone, Stamp(Edt(10, 26, 6, 10), NextSunday), Kept(Sunday), Decided(Sunday));

        Assert.False(work.RecipeHour);
    }
}
