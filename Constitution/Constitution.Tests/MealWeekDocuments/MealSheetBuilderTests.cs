using System.Globalization;
using Constitution.Domain.Budgets;
using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWeekDocuments.DocsData;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>The weekly meal sheet made from a planned week: the right dishes, markers, groceries, budget, ask page and page count.</summary>
public class MealSheetBuilderTests
{
    private sealed class HolidayOnTheSunday(HolidayRole role) : IHolidayCalendar
    {
        public HolidayEvents Between(DateOnly first, DateOnly last) =>
            new(true, [new HolidayEvent("Harvest supper", Sunday.AddDays(9), role, "the Okafors’", new TimeOnly(17, 0))]);
    }

    private static MealSheetBuildRequest Request(
        MonthlyBudgets? budgets = null, IHolidayCalendar? holidays = null, Pantry? pantry = null, IReadOnlyList<CookedMeal>? history = null) =>
        new(Week(), Cards(), Zone, 4, pantry ?? Stocked(), budgets ?? MonthlyBudgets.None, holidays ?? new UnavailableHolidayCalendar())
        {
            History = history ?? Cards().Select(card => new CookedMeal(card.Id, Sunday.AddDays(-14))).ToList(),
        };

    [Fact]
    public void TheDeadlineIsSundayEightOnTheOwnersClock()
    {
        var model = MealSheetBuilder.Build(Request());

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.FromHours(-4)), model.Deadline);
        Assert.Equal(MealDeadline.ForWeek(Sunday, Zone), model.Deadline);
    }

    [Fact]
    public void EveryCookedDinnerHasItsDish()
    {
        var model = MealSheetBuilder.Build(Request());

        Assert.Equal(6, model.Dishes.Count);
        Assert.Equal("Lentil soup", model.Dishes[Id("lentil-soup")].Title);
    }

    [Fact]
    public void ACardNeverCookedIsMarkedNew()
    {
        var history = Cards().Where(card => card.Id != Id("miso-salmon")).Select(card => new CookedMeal(card.Id, Sunday.AddDays(-14))).ToList();

        var model = MealSheetBuilder.Build(Request(history: history));

        Assert.Equal(MealMarker.NewRecipe, model.Dishes[Id("miso-salmon")].Marker);
        Assert.Null(model.Dishes[Id("lentil-soup")].Marker);
    }

    [Fact]
    public void WithNoHistoryNothingIsClaimedNew()
    {
        var model = MealSheetBuilder.Build(Request(history: []));

        Assert.All(model.Dishes.Values, dish => Assert.Null(dish.Marker));
    }

    [Fact]
    public void ASlowCookerCardIsMarkedEvenWhenNew()
    {
        var history = Cards().Where(card => card.Id != Id("black-bean-chili") && card.Id != Id("lentil-soup")).Select(card => new CookedMeal(card.Id, Sunday.AddDays(-14))).ToList();
        var request = Request(history: history) with { SlowCookerCards = new HashSet<RecipeId> { Id("black-bean-chili") } };

        var model = MealSheetBuilder.Build(request);

        Assert.Equal(MealMarker.SlowCooker, model.Dishes[Id("black-bean-chili")].Marker);
        Assert.Equal(MealMarker.NewRecipe, model.Dishes[Id("lentil-soup")].Marker);
    }

    [Fact]
    public void TheKidsDinnerIsNamedOnlyWhenOneIsGiven()
    {
        var without = MealSheetBuilder.Build(Request());
        var with = MealSheetBuilder.Build(Request() with
        {
            KidsMeals = new Dictionary<DateOnly, KidsMealFacts> { [Sunday.AddDays(2)] = new(KidsMeal(), Id("lentil-soup")) },
        });

        Assert.Null(without.Dishes[Id("sheet-pan-chicken")].KidsMeal);
        Assert.Equal("Buttered noodles", with.Dishes[Id("sheet-pan-chicken")].KidsMeal);
    }

    [Fact]
    public void NoBreakfastLunchOrLunchboxPageIsMade()
    {
        var model = MealSheetBuilder.Build(Request());

        Assert.Null(model.Daily);
        Assert.Equal(2, MealSheetDocument.Build(model).Count);
    }

    [Fact]
    public void TheGroceriesAreTheListLessThePantry()
    {
        var model = MealSheetBuilder.Build(Request());

        var lines = model.Groceries!.List.Lines;
        Assert.DoesNotContain(lines, line => line.Name == "red lentils");
        Assert.Contains(lines, line => line.Name == "carrots");
        Assert.Contains(lines, line => line.Name == "olive oil");
    }

    [Fact]
    public void AnEmptyPantryListsEverything()
    {
        var model = MealSheetBuilder.Build(Request(pantry: Pantry.Empty));

        Assert.Contains(model.Groceries!.List.Lines, line => line.Name == "red lentils");
    }

    [Fact]
    public void ACardMissingFromTheBoxIsRefusedPlainly()
    {
        var request = Request() with { Cards = Cards().Where(card => card.Id != Id("lentil-soup")).ToList() };

        Assert.Throws<ArgumentException>(() => MealSheetBuilder.Build(request));
    }

    [Fact]
    public void AnUnknownBudgetPrintsNothing()
    {
        var model = MealSheetBuilder.Build(Request());

        Assert.Equal(GroceryBudgetState.Unknown, model.Groceries!.Budget.State);
        var text = TextOf(MealSheetDocument.Build(model));
        Assert.DoesNotContain("budget", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnOverspentMonthIsFlaggedInWords()
    {
        var model = MealSheetBuilder.Build(Request(budgets: MonthBudget(100m, 130m)));

        Assert.Equal(GroceryBudgetState.Over, model.Groceries!.Budget.State);
        Assert.Contains("OVER BUDGET", TextOf(MealSheetDocument.Build(model)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBudgetIsTheMonthTheWeekOpensIn()
    {
        // The week opens on 25 October; a November budget alone says nothing about it.
        var november = MonthlyBudgets.None.Apply(new BudgetPayload("2026-11", 200m, 10m), Edt(10, 20, 9));

        var model = MealSheetBuilder.Build(Request(budgets: november));

        Assert.Equal(GroceryBudgetState.Unknown, model.Groceries!.Budget.State);
    }

    [Fact]
    public void NoAskPageWhenTheCalendarCannotBeAsked()
    {
        var model = MealSheetBuilder.Build(Request());

        Assert.Null(model.AskAhead);
        var pages = MealSheetDocument.Build(model);
        Assert.Equal(2, pages.Count);
        Assert.Equal(2, model.Pages);
    }

    [Fact]
    public void AHostedHolidayNextWeekAddsTheAskPage()
    {
        var model = MealSheetBuilder.Build(Request(holidays: new HolidayOnTheSunday(HolidayRole.Hosting)));

        Assert.NotNull(model.AskAhead);
        var pages = MealSheetDocument.Build(model);
        Assert.Equal(3, pages.Count);
        Assert.Equal(3, model.Pages);
        Assert.Equal("Harvest supper", pages[2].Title);
    }

    [Fact]
    public void AHolidayAtHomeAsksNothing()
    {
        var model = MealSheetBuilder.Build(Request(holidays: new HolidayOnTheSunday(HolidayRole.StayingHome)));

        Assert.Null(model.AskAhead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryFooterCountsThePagesBuilt(bool ask)
    {
        var model = MealSheetBuilder.Build(Request(holidays: ask ? new HolidayOnTheSunday(HolidayRole.Visiting) : null));

        var pages = MealSheetDocument.Build(model);

        for (var number = 1; number <= pages.Count; number++)
        {
            var footer = pages[number - 1].Printed.Single(item => item.Name == "page-number").Text;
            Assert.Equal($"{number.ToString(CultureInfo.InvariantCulture)} of {pages.Count.ToString(CultureInfo.InvariantCulture)}", footer);
        }
    }

    [Fact]
    public void EveryCookedRowStatesItsStartTime()
    {
        var model = MealSheetBuilder.Build(Request());

        var rows = MealSheetDocument.Build(model)[0].Printed.Where(item => item.Name?.EndsWith("-line", StringComparison.Ordinal) == true).Select(item => item.Text!).ToList();

        foreach (var cooked in model.Week.Dinners.Where(dinner => dinner.Kind == DinnerKind.Cooked))
        {
            Assert.Contains(rows, line => line.Contains($"{cooked.Cook} cooks from 5:15 pm", StringComparison.Ordinal));
        }

        Assert.Equal(6, rows.Count(line => line.Contains("cooks from", StringComparison.Ordinal)));
    }

    [Fact]
    public void ASlowCookerRowSaysWhenItIsStarted()
    {
        var model = MealSheetBuilder.Build(Request() with { SlowCookerCards = new HashSet<RecipeId> { Id("black-bean-chili") } });

        Assert.Contains("starts it", TextOf(MealSheetDocument.Build(model)), StringComparison.Ordinal);
    }

    [Fact]
    public void AWeekNotPlannedHasNoSheet()
    {
        var request = Request() with { Week = MealWeek.NotPlanned(Sunday, MealStop.BoxTooSmall, "The box is small.") };

        Assert.Throws<ArgumentException>(() => MealSheetBuilder.Build(request));
    }

    [Fact]
    public void AHeadcountBelowOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MealSheetBuilder.Build(Request() with { Headcount = 0 }));
    }
}
