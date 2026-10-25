using Constitution.Domain.Documents;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;

namespace Constitution.Tests.MealSheet;

/// <summary>An invented week shaped like book p.7. Every dish and name here is made up.</summary>
internal static class MealSheetSamples
{
    public static readonly DateOnly Monday = new(2026, 11, 16);

    public static readonly DateTimeOffset Deadline = new(2026, 11, 22, 8, 0, 0, TimeSpan.FromHours(-5));

    public static RecipeId Id(string value) => new(value);

    private static DinnerChoice Cooked(int offset, string card, string cook, int startHour, int startMinute, bool kids = false)
    {
        var day = Monday.AddDays(offset);
        var start = new DateTimeOffset(day.Year, day.Month, day.Day, startHour, startMinute, 0, TimeSpan.FromHours(-5));
        return DinnerChoice.Cooked(day, Id(card), cook, start, start.AddHours(1), false, kids, "Planned in the free gap.");
    }

    public static IReadOnlyList<DinnerChoice> Dinners() =>
    [
        Cooked(0, "lentil-soup", "Sam", 17, 15),
        Cooked(1, "sheet-pan-chicken", "Lee", 17, 15, kids: true),
        Cooked(2, "black-bean-chili", "Sam", 7, 40),
        Cooked(3, "miso-salmon", "Lee", 17, 15),
        DinnerChoice.EatOut(Monday.AddDays(4), "Friday is somebody else's kitchen."),
        Cooked(5, "turkey-meatballs", "Sam", 17, 15),
        Cooked(6, "veggie-fried-rice", "Lee", 17, 15),
    ];

    public static IReadOnlyDictionary<RecipeId, MealSheetDish> Dishes() => new Dictionary<RecipeId, MealSheetDish>
    {
        [Id("lentil-soup")] = new("Lentil soup, crusty bread"),
        [Id("sheet-pan-chicken")] = new("Sheet-pan chicken, potatoes", KidsMeal: "buttered noodles"),
        [Id("black-bean-chili")] = new("Black bean chili", MealMarker.SlowCooker),
        [Id("miso-salmon")] = new("Miso salmon, rice, greens", MealMarker.NewRecipe),
        [Id("turkey-meatballs")] = new("Turkey meatballs, spaghetti"),
        [Id("veggie-fried-rice")] = new("Veggie fried rice"),
    };

    public static IReadOnlyDictionary<DateOnly, MealSheetOuting> Outings() => new Dictionary<DateOnly, MealSheetOuting>
    {
        [Monday.AddDays(4)] = new("Corner noodle house", new DateTime(2026, 11, 20, 17, 45, 0)),
    };

    public static MealSheetModel Model(
        IEnumerable<DinnerChoice>? dinners = null,
        IReadOnlyDictionary<RecipeId, MealSheetDish>? dishes = null,
        string? voice = "Soup weather is here. One new dish, Thursday, and Friday is somebody else’s kitchen.",
        DateTimeOffset? deadline = null) =>
        new(
            MealWeek.Planned(Monday, dinners ?? Dinners()),
            dishes ?? Dishes(),
            deadline ?? Deadline,
            Outings(),
            voice);

    public static InkPageLayout Page(MealSheetModel? model = null) => MealSheetDocument.Build(model ?? Model()).Single();
}
