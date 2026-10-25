using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Screens;

namespace Constitution.Tests.MealsWeek;

/// <summary>C-6.7.1: tonight and the approved week on two cards (book p.13). Invented facts throughout.</summary>
public class ThisWeekModelTests
{
    private static readonly DateOnly Tuesday = WeekFixture.Sunday.AddDays(2);

    private static MealsWeekModel Build(DateOnly today, MealWeekSnapshot? snapshot = null) =>
        MealsWeekModel.Build(new MealWeekScreenRead.Ready(snapshot ?? WeekFixture.Friday()), today);

    [Fact]
    public void TodayIsBold()
    {
        var model = Build(Tuesday);

        Assert.Equal(7, model.Days.Count);
        Assert.Single(model.Days, line => line.IsToday);
        Assert.Equal("Tue", model.Days.Single(line => line.IsToday).DayLabel);
        Assert.Equal(WeekFixture.Sunday.AddDays(2), model.Days.Single(line => line.IsToday).Day);
    }

    [Fact]
    public void TheWeekIsDrawnSundayToSaturday()
    {
        var model = Build(Tuesday);

        Assert.Equal(["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"], model.Days.Select(line => line.DayLabel));
    }

    [Fact]
    public void EarlierDaysAreMarkedPast()
    {
        var model = Build(Tuesday);

        Assert.True(model.Days[0].IsPast);
        Assert.True(model.Days[1].IsPast);
        Assert.False(model.Days[2].IsPast);
        Assert.False(model.Days[3].IsPast);
    }

    [Fact]
    public void NoSwapTonight()
    {
        var model = Build(Tuesday);

        Assert.False(model.TonightCanBeSwapped);
        Assert.NotNull(model.Tonight);
        Assert.All(model.Days, line => Assert.False(line.CanBeSwapped));
    }

    [Fact]
    public void TonightShowsDishCookAndStart()
    {
        var tonight = Build(Tuesday).Tonight!;

        Assert.Equal("Tonight · 6:30", tonight.Kicker);
        Assert.Equal("Sheet-pan chicken, potatoes", tonight.Dish);
        Assert.Equal("Lee cooks · start 5:45", tonight.CookLine);
    }

    [Fact]
    public void MarkersAreSlowCookerAndNew()
    {
        var model = Build(Tuesday);

        Assert.Equal(["slow cooker"], model.Days[3].Markers);
        Assert.Equal(["new"], model.Days[4].Markers);
        Assert.Empty(model.Days[1].Markers);
    }

    [Fact]
    public void AnEveningOutIsReadAsOut()
    {
        var friday = Build(Tuesday).Days[5];

        Assert.True(friday.IsOut);
        Assert.Equal("Out · Corner noodle house", friday.Dish);
        Assert.Null(friday.Cook);
    }

    [Fact]
    public void AnEveningOutWithoutAPlaceStillSaysOut()
    {
        var friday = Build(Tuesday, WeekFixture.Snapshot()).Days[5];

        Assert.Equal("Out", friday.Dish);
        Assert.Equal("Everyone is out on Friday.", friday.ReasonLine);
    }

    [Fact]
    public void LeftoversCarryTheirReason()
    {
        var sunday = Build(Tuesday).Days[0];

        Assert.Equal("Leftovers", sunday.Dish);
        Assert.Equal("Saturday's meatballs, again.", sunday.ReasonLine);
    }

    [Fact]
    public void ACookedDinnerNamesItsCook()
    {
        var model = Build(Tuesday);

        Assert.Equal("Sam", model.Days[1].Cook);
        Assert.Equal("Robin", model.Days[3].Cook);
    }

    [Fact]
    public void ApprovedSundayHeadsTheWeekCard()
    {
        var sunday = WeekFixture.Sunday;
        var model = Build(Tuesday, WeekFixture.Snapshot() with { ApprovedOn = sunday });

        Assert.Equal("This week · approved Sunday", model.WeekHeading);
        Assert.Equal("This week", Build(Tuesday).WeekHeading);
    }

    [Fact]
    public void AMissingDishIsNamedHonestly()
    {
        var snapshot = new MealWeekSnapshot(WeekFixture.Week(), new Dictionary<Constitution.Domain.Recipes.RecipeId, DishFacts>());

        Assert.Equal("A card no longer in the box", Build(Tuesday, snapshot).Days[1].Dish);
    }

    [Fact]
    public void ATodayOutsideTheWeekHasNoTonight()
    {
        var model = Build(WeekFixture.Sunday.AddDays(9));

        Assert.Null(model.Tonight);
        Assert.Equal(MealsWeekModel.NothingTonightSentence, model.TonightSentence);
        Assert.DoesNotContain(model.Days, line => line.IsToday);
    }

    [Fact]
    public void NothingPlannedTonightSaysSoWithItsReason()
    {
        var week = MealWeek.Planned(WeekFixture.Sunday,
            Enumerable.Range(0, 7).Select(day => Constitution.Domain.Meals.DinnerChoice.NothingPlanned(WeekFixture.Sunday.AddDays(day), "Nobody is home.")));
        var model = Build(Tuesday, WeekFixture.Snapshot(week));

        Assert.Equal("Tonight", model.Tonight!.Kicker);
        Assert.Equal("Nothing planned", model.Tonight.Dish);
        Assert.Equal("Nobody is home.", model.Tonight.CookLine);
    }

    [Fact]
    public void TheBarKeepsIAteSomethingElse()
    {
        Assert.Equal("I ate something else", MealsWeekModel.ActionLabel);
    }

    [Fact]
    public void ANoteOfTheWeekIsCarried()
    {
        var week = MealWeek.Planned(WeekFixture.Sunday, WeekFixture.Week().Dinners, ["No stretch dish this week."]);

        Assert.Equal(["No stretch dish this week."], Build(Tuesday, WeekFixture.Snapshot(week)).Notes);
    }

    [Fact]
    public void NoPlanYetIsAHonestSentence()
    {
        var model = MealsWeekModel.Build(new MealWeekScreenRead.NoPlanYet(), Tuesday);

        Assert.Equal(WeekState.NoPlan, model.State);
        Assert.Equal(MealsWeekModel.NoPlanSentence, model.Status);
        Assert.Empty(model.Days);
        Assert.Null(model.Tonight);
    }

    [Fact]
    public void ATooSmallBoxIsSaidInTheWeeksOwnWords()
    {
        var week = MealWeek.NotPlanned(WeekFixture.Sunday, MealStop.BoxTooSmall, "The box holds 6 cards; a week needs 10.");
        var model = Build(Tuesday, WeekFixture.Snapshot(week));

        Assert.Equal(WeekState.NotEnoughRecipes, model.State);
        Assert.Equal("The box holds 6 cards; a week needs 10.", model.Status);
        Assert.Empty(model.Days);
    }

    [Fact]
    public void AnUnreadableCalendarIsNeverNothingOn()
    {
        var week = MealWeek.NotPlanned(WeekFixture.Sunday, MealStop.CalendarUnreadable, "A calendar could not be read.");
        var model = Build(Tuesday, WeekFixture.Snapshot(week));

        Assert.Equal(WeekState.CalendarUnreadable, model.State);
        Assert.Equal("A calendar could not be read.", model.Status);
    }

    [Fact]
    public void UnavailableQuotesOnlyItsFixedReason()
    {
        var model = MealsWeekModel.Build(new MealWeekScreenRead.Unavailable("The saved meal week could not be read."), Tuesday);

        Assert.Equal(WeekState.Unavailable, model.State);
        Assert.Equal("The saved meal week could not be read.", model.Status);
    }

    [Fact]
    public void RelativeDayLabelsAreInPlainWords()
    {
        var model = Build(Tuesday);

        Assert.Equal("Yesterday", model.Days[1].RelativeLabel);
        Assert.Equal("Today", model.Days[2].RelativeLabel);
        Assert.Equal("Tomorrow", model.Days[3].RelativeLabel);
        Assert.Equal("Friday", model.Days[5].RelativeLabel);
    }

    [Fact]
    public void TheNoSourceSaysThereIsNoPlan()
    {
        var read = new NoMealWeekSource().Read(DateTimeOffset.UtcNow);

        Assert.IsType<MealWeekScreenRead.NoPlanYet>(read);
    }
}
