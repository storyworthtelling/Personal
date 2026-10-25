using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Scraps;
using Constitution.Domain.Today;
using Constitution.Domain.Voice;

namespace Constitution.Tests.MealsWeek;

/// <summary>C-4.1.3: tonight's dinner on an index card, or a receipt when eating out (book p.11, D46). Invented facts.</summary>
public class DinnerCardTests
{
    private static readonly DateOnly Tuesday = WeekFixture.Monday.AddDays(1);
    private static readonly DateOnly Friday = WeekFixture.Monday.AddDays(4);

    private static DinnerClipping? For(DateOnly day) =>
        DinnerClipping.For(new MealWeekScreenRead.Ready(WeekFixture.Friday()), day);

    [Fact]
    public void DinnerCarriesTheCook()
    {
        var card = For(Tuesday)!;

        Assert.Equal(ScrapKind.IndexCard, card.Paper);
        Assert.Equal("Dinner · 6:30", card.Kicker);
        Assert.Equal("Sheet-pan chicken, potatoes", card.Headline);
        Assert.Equal("Lee cooks", card.Line);
    }

    [Fact]
    public void EatingOutIsAReceipt()
    {
        var card = For(Friday)!;

        Assert.Equal(ScrapKind.Receipt, card.Paper);
        Assert.Equal("Dinner · 6:30", card.Kicker);
        Assert.Equal("Out · Corner noodle house", card.Headline);
        Assert.Equal("Leave 5:45 · everyone", card.Line);
    }

    [Fact]
    public void AnEveningOutWithNoDetailStaysAReceiptAndInventsNothing()
    {
        var read = new MealWeekScreenRead.Ready(WeekFixture.Snapshot());
        var card = DinnerClipping.For(read, Friday)!;

        Assert.Equal(ScrapKind.Receipt, card.Paper);
        Assert.Equal("Dinner · out", card.Kicker);
        Assert.Equal("Out tonight", card.Headline);
        Assert.Equal("Everyone is out on Friday.", card.Line);
    }

    [Fact]
    public void LeftoversAreAnIndexCard()
    {
        var card = For(WeekFixture.Monday.AddDays(6))!;

        Assert.Equal(ScrapKind.IndexCard, card.Paper);
        Assert.Equal("Dinner", card.Kicker);
        Assert.Equal("Leftovers", card.Headline);
    }

    [Fact]
    public void NoPlanMeansNoCard()
    {
        Assert.Null(DinnerClipping.For(new MealWeekScreenRead.NoPlanYet(), Tuesday));
        Assert.Null(DinnerClipping.For(new MealWeekScreenRead.Unavailable("Not readable."), Tuesday));
        Assert.Null(For(WeekFixture.Monday.AddDays(20)));
    }

    [Fact]
    public void TheTopLineGetsTheSameDinner()
    {
        var read = new MealWeekScreenRead.Ready(WeekFixture.Friday());

        Assert.Equal("Sheet-pan chicken, potatoes", DinnerClipping.PlanOf(read, Tuesday)!.Dish);
        Assert.True(DinnerClipping.PlanOf(read, Friday)!.IsOut);
        Assert.Null(DinnerClipping.PlanOf(new MealWeekScreenRead.NoPlanYet(), Tuesday));
    }

    [Fact]
    public void TheClippingRidesTheTodayPlanAdditively()
    {
        var plan = new TodayPlan(new TimeOnly(5, 30), "strength", 40, null) { ScoreBpm = 132 };
        var card = For(Tuesday);
        var withDinner = plan.WithDinner(DinnerPlan.At("Sheet-pan chicken, potatoes"), card);

        Assert.Same(card, TodayModel.Dinner(withDinner));
        Assert.Null(TodayModel.Dinner(plan));
        Assert.Null(TodayModel.Dinner(null));
        Assert.Equal(132, withDinner.ScoreBpm);
        Assert.Equal("Strength, 40 minutes", TodayModel.Clipping(withDinner)!.Headline);
        Assert.Contains("Sheet-pan chicken", TodayModel.TopLine(withDinner).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARestDayKeepsItsDinnerCard()
    {
        var plan = new TodayPlan(null, null, null, null).WithDinner(DinnerPlan.Out, For(Friday));

        Assert.Null(TodayModel.Clipping(plan));
        Assert.Equal(ScrapKind.Receipt, TodayModel.Dinner(plan)!.Paper);
    }
}
