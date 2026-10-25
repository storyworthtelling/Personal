using Constitution.Domain.Documents;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;

namespace Constitution.Tests.MealSheet;

/// <summary>An invented week shaped like book p.7, Sunday 15 to Saturday 21 November 2026. Every dish and name here is made up.</summary>
internal static class MealSheetSamples
{
    public static readonly DateOnly Sunday = new(2026, 11, 15);

    public static readonly DateTimeOffset Deadline = new(2026, 11, 22, 8, 0, 0, TimeSpan.FromHours(-5));

    public static RecipeId Id(string value) => new(value);

    private static DinnerChoice Cooked(int offset, string card, string cook, int startHour, int startMinute, bool kids = false)
    {
        var day = Sunday.AddDays(offset);
        var start = new DateTimeOffset(day.Year, day.Month, day.Day, startHour, startMinute, 0, TimeSpan.FromHours(-5));
        return DinnerChoice.Cooked(day, Id(card), cook, start, start.AddHours(1), false, kids, "Planned in the free gap.");
    }

    public static IReadOnlyList<DinnerChoice> Dinners() =>
    [
        Cooked(0, "veggie-fried-rice", "Lee", 17, 15),
        Cooked(1, "lentil-soup", "Sam", 17, 15),
        Cooked(2, "sheet-pan-chicken", "Lee", 17, 15, kids: true),
        Cooked(3, "black-bean-chili", "Sam", 7, 40),
        Cooked(4, "miso-salmon", "Lee", 17, 15),
        DinnerChoice.EatOut(Sunday.AddDays(5), "Friday is somebody else's kitchen."),
        Cooked(6, "turkey-meatballs", "Sam", 17, 15),
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
        [Sunday.AddDays(5)] = new("Corner noodle house", new DateTime(2026, 11, 20, 17, 45, 0)),
    };

    public static MealSheetModel Model(
        IEnumerable<DinnerChoice>? dinners = null,
        IReadOnlyDictionary<RecipeId, MealSheetDish>? dishes = null,
        string? voice = "Soup weather is here. One new dish, Thursday, and Friday is somebody else’s kitchen.",
        DateTimeOffset? deadline = null) =>
        new(
            MealWeek.Planned(Sunday, dinners ?? Dinners()),
            dishes ?? Dishes(),
            deadline ?? Deadline,
            Outings(),
            voice);

    public static InkPageLayout Page(MealSheetModel? model = null) => MealSheetDocument.Build(model ?? Model()).Single();
}
