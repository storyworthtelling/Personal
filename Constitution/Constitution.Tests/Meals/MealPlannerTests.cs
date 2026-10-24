using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealPlanning;

/// <summary>C-6.3.1, the rules-based core: the week's dinners from the box, the calendars and the household's words.</summary>
public class MealPlannerTests
{
    [Fact]
    public void SevenDinnersFromTheBox()
    {
        var request = Request();

        var week = MealPlanner.Plan(request);

        Assert.True(week.IsPlanned);
        Assert.Equal(7, week.Dinners.Count);
        Assert.Equal(Enumerable.Range(0, 7).Select(offset => Monday.AddDays(offset)), week.Dinners.Select(dinner => dinner.Day));
        Assert.All(Cooks(week), dinner => Assert.Contains(request.Recipes, recipe => recipe.Id == dinner.Recipe));
        Assert.Equal(7, Cooks(week).Count());
    }

    [Fact]
    public void NoCardIsCookedTwiceInAWeek()
    {
        var week = MealPlanner.Plan(Request());

        Assert.Equal(7, Cooks(week).Select(dinner => dinner.Recipe).Distinct().Count());
    }

    [Fact]
    public void OneStretchDishANotCookedLatelyCard()
    {
        var history = Cooked(("saffron-rice", 20), ("lentil-curry", 21), ("tomato-pasta", 22), ("mushroom-risotto", 23), ("bean-burritos", 24), ("corn-tacos", 25), ("noodle-stir-fry", 26), ("dumpling-soup", 27), ("herb-omelette", 28), ("onion-tart", 29), ("roast-chicken", 30));

        var week = MealPlanner.Plan(Request(history: history));

        var stretch = Assert.Single(week.Dinners.Where(dinner => dinner.IsStretch));
        Assert.Equal(Id("veggie-stew"), stretch.Recipe);
    }

    [Fact]
    public void TheStretchLandsOnThursday()
    {
        var week = MealPlanner.Plan(Request());

        var stretch = Assert.Single(week.Dinners.Where(dinner => dinner.IsStretch));
        Assert.Equal(DayOfWeek.Thursday, stretch.Day.DayOfWeek);
    }

    [Fact]
    public void NoStretchWhenEveryCardWasCookedLately()
    {
        var history = Box().Select((recipe, index) => new CookedMeal(recipe.Id, Monday.AddDays(-15 - index))).ToList();

        var week = MealPlanner.Plan(Request(history: history));

        Assert.DoesNotContain(week.Dinners, dinner => dinner.IsStretch);
        Assert.Contains(week.Notes, note => note.Contains("stretch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CookThisSoonCardsComeFirst()
    {
        var week = MealPlanner.Plan(Request(soon: [Id("veggie-stew"), Id("onion-tart")]));

        var firstTwo = week.Dinners.Take(2).Select(dinner => dinner.Recipe).ToList();
        Assert.Contains(Id("veggie-stew"), firstTwo);
        Assert.Contains(Id("onion-tart"), firstTwo);
    }

    [Fact]
    public void ACookThisSoonCardStillWaitsOutTheFortnight()
    {
        var week = MealPlanner.Plan(Request(soon: [Id("veggie-stew")], history: Cooked(("veggie-stew", 5))));

        Assert.DoesNotContain(week.Dinners, dinner => dinner.Recipe == Id("veggie-stew"));
        Assert.Contains(week.Notes, note => note.Contains("Invented veggie stew", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(10, 4)]
    [InlineData(11, 3)]
    [InlineData(14, 0)]
    [InlineData(30, 0)]
    public void NoRepeatWithinFourteenDays(int daysAgo, int firstEvening)
    {
        var week = MealPlanner.Plan(Request(soon: [Id("roast-chicken")], history: Cooked(("roast-chicken", daysAgo))));

        var placed = week.Dinners.ToList().FindIndex(dinner => dinner.Recipe == Id("roast-chicken"));
        Assert.Equal(firstEvening, placed);
    }

    [Fact]
    public void ADishAlreadyPlannedForAWeekAheadBlocksToo()
    {
        var future = new List<CookedMeal> { new(Id("roast-chicken"), Monday.AddDays(10)) };

        var week = MealPlanner.Plan(Request(soon: [Id("roast-chicken")], history: future));

        Assert.All(week.Dinners, dinner => Assert.NotEqual(Id("roast-chicken"), dinner.Recipe));
    }

    [Fact]
    public void NoNeighbouringEveningsShareACuisine()
    {
        for (var seed = 0; seed < 25; seed++)
        {
            var week = MealPlanner.Plan(Request(seed: seed));
            var cuisines = week.Dinners.Select(dinner => Box().First(recipe => recipe.Id == dinner.Recipe).Cuisine).ToList();

            for (var index = 1; index < cuisines.Count; index++)
            {
                Assert.NotEqual(cuisines[index - 1], cuisines[index]);
            }
        }
    }

    [Fact]
    public void CuisinesAreSpreadAcrossTheWeek()
    {
        var week = MealPlanner.Plan(Request());

        var perCuisine = week.Dinners.GroupBy(dinner => Box().First(recipe => recipe.Id == dinner.Recipe).Cuisine).Select(group => group.Count());
        Assert.True(perCuisine.Max() <= 2, "No cuisine is cooked more than twice when six are on offer.");
    }

    [Fact]
    public void TheCookBlockEndsWhereTheFamilyDinnerStarts()
    {
        var week = MealPlanner.Plan(Request());

        Assert.Equal(7, Cooks(week).Count());
        Assert.All(Cooks(week), dinner =>
        {
            Assert.Equal(18, dinner.CookEnd!.Value.Hour);
            Assert.Equal(0, dinner.CookEnd.Value.Minute);
            Assert.Equal(dinner.Day, DateOnly.FromDateTime(dinner.CookEnd.Value.DateTime));
            Assert.True(dinner.CookStart < dinner.CookEnd);
        });
    }

    [Fact]
    public void TheBlockLengthFollowsTheCardWithinLimits()
    {
        var recipes = Box().Select((recipe, index) => recipe with { Steps = [new RecipeStep("Cook it.", index == 0 ? 10 : 500, true)] }).ToList();

        var week = MealPlanner.Plan(Request(recipes: recipes));

        Assert.All(Cooks(week), dinner =>
        {
            var length = dinner.CookEnd!.Value - dinner.CookStart!.Value;
            Assert.InRange(length.TotalMinutes, 30, 120);
        });
    }

    [Fact]
    public void ACookIsNeverPlacedOnBlockedTime()
    {
        var monday = Monday;
        var request = Request(
            sam: [On(monday, "Dentist", 16, 30, 18, 0)],
            lee: [On(monday, "Class", 17, 0, 18, 0)]);

        var week = MealPlanner.Plan(request);

        Assert.NotEqual(DinnerKind.Cooked, week.Dinners[0].Kind);
    }

    [Fact]
    public void WhoeverIsFreeCooks()
    {
        var request = Request(sam: [On(Monday, "Dentist", 16, 30, 18, 0)]);

        var week = MealPlanner.Plan(request);

        Assert.Equal(Lee, week.Dinners[0].Cook);
    }

    [Fact]
    public void CooksShareTheWeekWhenBothAreFree()
    {
        var week = MealPlanner.Plan(Request());

        var counts = Cooks(week).GroupBy(dinner => dinner.Cook).Select(group => group.Count()).ToList();
        Assert.Equal(2, counts.Count);
        Assert.InRange(counts.Max() - counts.Min(), 0, 1);
    }

    [Fact]
    public void WhenNobodyIsFreeNothingIsPlannedAndItSaysWhy()
    {
        var blocked = new[] { On(Monday, "Away", 15, 0, 17, 59) };

        var week = MealPlanner.Plan(Request(sam: blocked, lee: blocked));

        var monday = week.Dinners[0];
        Assert.Equal(DinnerKind.NothingPlanned, monday.Kind);
        Assert.Contains("cook", monday.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhenNobodyIsHomeNothingIsPlanned()
    {
        var out2 = new[] { On(Monday, "Away", 17, 30, 20, 0) };

        var week = MealPlanner.Plan(Request(sam: out2, lee: out2));

        Assert.Equal(DinnerKind.NothingPlanned, week.Dinners[0].Kind);
        Assert.Contains("home", week.Dinners[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeftoversFollowACookedEvening()
    {
        var blocked = new[] { On(Monday.AddDays(1), "Away", 15, 0, 17, 59) };

        var week = MealPlanner.Plan(Request(sam: blocked, lee: blocked));

        Assert.Equal(DinnerKind.Cooked, week.Dinners[0].Kind);
        Assert.Equal(DinnerKind.Leftovers, week.Dinners[1].Kind);
        Assert.Equal(DinnerKind.Cooked, week.Dinners[2].Kind);
    }

    [Fact]
    public void LeftoversNeverFollowLeftovers()
    {
        var blocked = Enumerable.Range(1, 2).Select(offset => On(Monday.AddDays(offset), "Away", 15, 0, 17, 59)).ToList();

        var week = MealPlanner.Plan(Request(sam: blocked, lee: blocked));

        Assert.Equal(DinnerKind.Leftovers, week.Dinners[1].Kind);
        Assert.Equal(DinnerKind.NothingPlanned, week.Dinners[2].Kind);
    }

    [Fact]
    public void TheRulesNeverPlanEatingOut()
    {
        var blocked = Enumerable.Range(0, 7).Select(offset => On(Monday.AddDays(offset), "Away", 15, 0, 17, 59)).ToList();

        var week = MealPlanner.Plan(Request(sam: blocked, lee: blocked));

        Assert.DoesNotContain(week.Dinners, dinner => dinner.Kind == DinnerKind.EatOut);
    }

    [Fact]
    public void AnUnreadableCalendarPlansNothingAndSaysWhy()
    {
        var request = Request(calendars: CalendarsWith(lee: CalendarRead.Unreadable("Not shared yet.")));

        var week = MealPlanner.Plan(request);

        Assert.False(week.IsPlanned);
        Assert.Equal(MealStop.CalendarUnreadable, week.Stop);
        Assert.Empty(week.Dinners);
        Assert.Contains("calendar", week.StopReason!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Not shared yet", week.StopReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingAdultCalendarPlansNothing()
    {
        var request = Request() with { AdultTime = [Request().AdultTime[0]] };

        Assert.Equal(MealStop.CalendarUnreadable, MealPlanner.Plan(request).Stop);
    }

    [Fact]
    public void ACalendarThatMissesPartOfTheWeekPlansNothing()
    {
        var rules = Rules();
        var shortWindow = rules.Adults.Select(adult => BusyTime.For(adult.Name, Monday, Monday.AddDays(3), rules, Calendars())).ToList();

        var week = MealPlanner.Plan(Request() with { AdultTime = shortWindow });

        Assert.Equal(MealStop.CalendarUnreadable, week.Stop);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(12, true)]
    public void TheBoxMustCountToTen(int cards, bool plans)
    {
        var recipes = Box().Take(cards).ToList();
        if (cards > 12)
        {
            recipes.AddRange(Enumerable.Range(0, cards - 12).Select(index => Dish("extra-" + index, Cuisine.Other)));
        }

        var week = MealPlanner.Plan(Request(recipes: recipes));

        Assert.Equal(plans, week.IsPlanned);
        if (!plans)
        {
            Assert.Equal(MealStop.BoxTooSmall, week.Stop);
            Assert.Contains(cards + " of 10", week.StopReason!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AShortBoxIsReportedBeforeAnUnreadableCalendar()
    {
        var request = Request(recipes: Box().Take(3).ToList(), calendars: CalendarsWith(sam: CalendarRead.Unreadable("No.")));

        Assert.Equal(MealStop.BoxTooSmall, MealPlanner.Stop(request));
    }

    [Fact]
    public void ANeverAgainDishWaitsForTheAdultToBeAway()
    {
        var recipes = Box();
        var vetoes = recipes.ToDictionary(recipe => recipe.Id, recipe => (IReadOnlySet<string>)new HashSet<string> { Sam });
        var samOut = new[] { On(Monday.AddDays(1), "Concert", 17, 0, 21, 0) };

        var week = MealPlanner.Plan(Request(sam: samOut, neverAgain: vetoes));

        var cooked = Cooks(week).ToList();
        Assert.Equal([Monday.AddDays(1)], cooked.Select(dinner => dinner.Day));
        Assert.Equal(Lee, cooked[0].Cook);
    }

    [Fact]
    public void ANeverAgainDishFromAChildMeansTheChildrenEatDifferently()
    {
        var recipes = Box();
        var vetoes = recipes.ToDictionary(recipe => recipe.Id, recipe => (IReadOnlySet<string>)new HashSet<string> { Robin });

        var home = MealPlanner.Plan(Request(neverAgain: vetoes, childrenHome: AllEvenings()));
        var away = MealPlanner.Plan(Request(neverAgain: vetoes));

        Assert.All(Cooks(home), dinner => Assert.True(dinner.ChildrenEatDifferently));
        Assert.All(Cooks(away), dinner => Assert.False(dinner.ChildrenEatDifferently));
    }

    [Fact]
    public void ChildrenGetADifferentMealWhenTheAdultsDishIsNotForThem()
    {
        var week = MealPlanner.Plan(Request(childrenHome: AllEvenings()));

        var box = Box();
        Assert.All(Cooks(week), dinner =>
            Assert.Equal(!box.First(recipe => recipe.Id == dinner.Recipe).ForKids, dinner.ChildrenEatDifferently));
    }

    [Fact]
    public void ChildrenGetTheirOwnDishFirstWhenTheyAreHome()
    {
        var week = MealPlanner.Plan(Request(childrenHome: AllEvenings()));

        var box = Box();
        var forKids = Cooks(week).Count(dinner => box.First(recipe => recipe.Id == dinner.Recipe).ForKids);
        Assert.Equal(box.Count(recipe => recipe.ForKids), forKids);
    }

    [Fact]
    public void NothingChangesForTheChildrenWhenTheyAreAway()
    {
        var week = MealPlanner.Plan(Request());

        Assert.All(week.Dinners, dinner => Assert.False(dinner.ChildrenEatDifferently));
    }

    [Fact]
    public void EveningsThatHavePassedAreNotPlanned()
    {
        var week = MealPlanner.Plan(Request(now: Edt(10, 28, 9)));

        Assert.Equal(DinnerKind.NothingPlanned, week.Dinners[0].Kind);
        Assert.Equal(DinnerKind.NothingPlanned, week.Dinners[1].Kind);
        Assert.Equal(DinnerKind.Cooked, week.Dinners[2].Kind);
    }

    [Fact]
    public void TheSameSeedMakesTheSameWeek()
    {
        var first = MealWeekCodec.Write(MealPlanner.Plan(Request(seed: 7)));
        var second = MealWeekCodec.Write(MealPlanner.Plan(Request(seed: 7)));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADifferentSeedCanMakeADifferentWeek()
    {
        var weeks = Enumerable.Range(0, 20).Select(seed => MealWeekCodec.Write(MealPlanner.Plan(Request(seed: seed)))).Distinct().Count();

        Assert.True(weeks > 1);
    }

    [Fact]
    public void TheSeedBreaksTiesBetweenEqualCards()
    {
        var alike = Enumerable.Range(0, 12).Select(index => Dish("same-" + new string('x', index + 1), Cuisine.Other)).ToList();

        var mondays = Enumerable.Range(0, 20).Select(seed => MealPlanner.Plan(Request(recipes: alike, seed: seed)).Dinners[0].Recipe).Distinct().Count();

        Assert.True(mondays > 2);
    }

    [Fact]
    public void TheBudgetSeamChangesNothingYet()
    {
        var without = MealWeekCodec.Write(MealPlanner.Plan(Request(budget: null)));
        var with = MealWeekCodec.Write(MealPlanner.Plan(Request(budget: 80.00m)));

        Assert.Equal(without, with);
    }

    [Fact]
    public void AWeekMustStartOnAMonday()
    {
        var tuesday = Monday.AddDays(1);

        Assert.Throws<ArgumentException>(() => new MealWeekRequest(tuesday, Box(), Rules(), AdultTime(Rules(), Calendars())));
    }

    [Fact]
    public void TwoCardsWithOneIdAreRefused()
    {
        var twin = Box().Append(Dish("saffron-rice", Cuisine.Other)).ToList();

        Assert.Throws<ArgumentException>(() => Request(recipes: twin));
    }

    [Fact]
    public void EveryDinnerHasAReason()
    {
        var week = MealPlanner.Plan(Request(childrenHome: AllEvenings()));

        Assert.All(week.Dinners, dinner => Assert.False(string.IsNullOrWhiteSpace(dinner.Reason)));
    }
}
