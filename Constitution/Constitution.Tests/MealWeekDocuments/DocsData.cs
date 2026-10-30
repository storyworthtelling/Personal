using Constitution.Domain.Budgets;
using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>An invented week for the meal documents: Sam and Lee cook, Robin and Juniper eat. Every dish, ingredient and time is made up.</summary>
internal static class DocsData
{
    public static RecipeId Id(string value) => new(value);

    /// <summary>A card of 45 minutes (15 hands-on, 30 waiting) for four, with two ingredients.</summary>
    public static Recipe Card(string id, string title, int? servings = 4, Cuisine cuisine = Cuisine.Other) => new(
        Id(id),
        title,
        cuisine,
        [new Ingredient("1", "cup", "red lentils"), new Ingredient("2", null, "carrots")],
        [new RecipeStep("Chop the vegetables.", 15, true), new RecipeStep("Simmer until soft.", 30, false)],
        servings,
        "family card",
        false);

    public static List<Recipe> Cards() =>
    [
        Card("veggie-fried-rice", "Veggie fried rice"),
        Card("lentil-soup", "Lentil soup"),
        Card("sheet-pan-chicken", "Sheet-pan chicken"),
        Card("black-bean-chili", "Black bean chili"),
        Card("miso-salmon", "Miso salmon"),
        Card("turkey-meatballs", "Turkey meatballs"),
    ];

    /// <summary>A cooked dinner whose block ends where the 6 pm family dinner starts.</summary>
    public static DinnerChoice Cooked(int offset, string card, string cook, int startHour = 17, int startMinute = 15, bool kids = false)
    {
        var day = Sunday.AddDays(offset);
        var end = new DateTimeOffset(day.Year, day.Month, day.Day, 18, 0, 0, TimeSpan.FromHours(-4));
        var start = new DateTimeOffset(day.Year, day.Month, day.Day, startHour, startMinute, 0, TimeSpan.FromHours(-4));
        return DinnerChoice.Cooked(day, Id(card), cook, start, end, false, kids, "Planned in the free gap.");
    }

    public static MealWeek Week() => MealWeek.Planned(
        Sunday,
        [
            Cooked(0, "veggie-fried-rice", Lee),
            Cooked(1, "lentil-soup", Sam),
            Cooked(2, "sheet-pan-chicken", Lee, kids: true),
            Cooked(3, "black-bean-chili", Sam),
            Cooked(4, "miso-salmon", Lee),
            DinnerChoice.EatOut(Sunday.AddDays(5), "Friday is somebody else's kitchen."),
            Cooked(6, "turkey-meatballs", Sam),
        ]);

    public static IReadOnlyList<RosterEntry> People() =>
    [
        new(new PersonId("sam"), Sam, MemberRole.Adult, true, null),
        new(new PersonId("lee"), Lee, MemberRole.Adult, true, null),
        new(new PersonId("robin"), Robin, MemberRole.Child, false, null),
        new(new PersonId("juniper"), Juniper, MemberRole.Child, false, null),
    ];

    public static MonthlyBudgets MonthBudget(decimal budget, decimal spent) =>
        MonthlyBudgets.None.Apply(new BudgetPayload("2026-10", budget, spent), Edt(10, 20, 9));

    public static Pantry Stocked() => Pantry.From(
    [
        new Staple("red lentils", Aisle.DryGoods, false, standing: true),
        new Staple("olive oil", Aisle.BakingAndSpices, true, standing: true),
    ]);

    public static RecipeKidsMeal KidsMeal() => new(
        "Buttered noodles",
        "noodles",
        [Robin, Juniper],
        "Tonight is too spicy for them.",
        [new RecipeIngredient("Noodles", "8 oz", "noodles")],
        [new RecipeClockStep("Boil the noodles.", 12, true)]);

    /// <summary>Every printed word of every page, one string.</summary>
    public static string TextOf(IReadOnlyList<InkPageLayout> pages) =>
        string.Join("\n", pages.SelectMany(page => page.Printed).Where(item => item.Text is not null).Select(item => item.Text));
}
