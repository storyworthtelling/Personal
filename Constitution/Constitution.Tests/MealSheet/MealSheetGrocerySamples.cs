using Constitution.Domain.Budgets;
using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using Constitution.Tests.Groceries;

namespace Constitution.Tests.MealSheet;

/// <summary>An invented groceries page: three dinners' ingredients and eight standing staples. Every dish and amount is made up.</summary>
internal static class MealSheetGrocerySamples
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    public static Pantry StandingPantry() => Pantry.From(
    [
        GroceryData.OnHand("olive oil", Aisle.CannedAndJarred, standing: true),
        GroceryData.OnHand("rolled oats", Aisle.DryGoods, standing: true),
        GroceryData.OnHand("peanut butter", Aisle.CannedAndJarred, standing: true),
        GroceryData.OnHand("eggs", Aisle.DairyAndEggs, standing: true),
        GroceryData.OnHand("rice", Aisle.DryGoods, standing: true),
        GroceryData.OnHand("garlic", Aisle.Produce, standing: true),
        GroceryData.OnHand("bread", Aisle.Bakery, standing: true),
        GroceryData.OnHand("milk", Aisle.DairyAndEggs, standing: true),
        GroceryData.OnHand("onion", Aisle.Produce),
        GroceryData.OnHand("lemon", Aisle.Produce),
        GroceryData.OnHand("salt", Aisle.BakingAndSpices),
    ]);

    public static GroceryBudgetStatus Budget(string budget, string spent, decimal? estimate)
    {
        var held = MonthlyBudgets.None.Apply(BudgetPayload.Parse(key => key switch { "month" => "2026-11", "budget" => budget, _ => spent }).Payload!, Noon);
        return GroceryBudget.Weigh(held, "2026-11", estimate);
    }

    public static GroceryBudgetStatus Over() => Budget("180.00", "20.00", 194.50m);

    public static GroceryBudgetStatus Within() => Budget("600.00", "100.00", 90.25m);

    public static GroceryBudgetStatus Unknown() => GroceryBudget.Weigh(MonthlyBudgets.None, "2026-11", 50m);

    public static MealSheetGroceries Groceries(GroceryBudgetStatus? budget = null, Pantry? pantry = null, GroceryList? list = null)
    {
        var held = pantry ?? StandingPantry();
        return new(list ?? GroceryList.From(GroceryData.ThreeDinners(), GroceryData.Cards, held, 4), held, budget ?? Over());
    }

    public static MealSheetModel Model(MealSheetGroceries? groceries = null, HolidayAsk? ask = null, int pages = 5) =>
        MealSheetSamples.Model(daily: MealSheetSamples.Daily(), pages: pages) with { Groceries = groceries ?? Groceries(), AskAhead = ask };

    public static IReadOnlyList<InkPageLayout> Pages(MealSheetModel? model = null) => MealSheetDocument.Build(model ?? Model());

    public static InkPageLayout Page(MealSheetModel? model = null) => Pages(model)[3];

    /// <summary>Standing staples named by number, spread over the aisles, so a page has many ticks to place.</summary>
    public static Pantry ManyStaples(int count) => Pantry.From(
        Enumerable.Range(1, count).Select(n => GroceryData.OnHand($"staple number {n} of the pantry", Aisles.All[n % Aisles.All.Count], standing: true)));
}
