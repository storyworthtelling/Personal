using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Recipes;

namespace Constitution.Tests.MealsWeek;

/// <summary>An invented week of dinners: Monday 2026-11-16 to Sunday. Dishes, cooks and places are made up.</summary>
internal static class WeekFixture
{
    public static readonly DateOnly Monday = new(2026, 11, 16);

    public static DateTimeOffset At(int dayOffset, int hour, int minute) =>
        new(Monday.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(-5));

    public static RecipeId Id(string name) => new(name);

    private static DinnerChoice Cooked(int day, string id, string cook, int startHour, int startMinute, bool stretch = false) =>
        DinnerChoice.Cooked(Monday.AddDays(day), Id(id), cook, At(day, startHour, startMinute), At(day, startHour, startMinute).AddMinutes(45), stretch, false, "A plain reason.");

    public static MealWeek Week() => MealWeek.Planned(Monday,
    [
        Cooked(0, "lentil-soup", "Sam", 5, 30),
        Cooked(1, "sheet-pan-chicken", "Lee", 5, 45),
        Cooked(2, "black-bean-chili", "Robin", 4, 0),
        Cooked(3, "miso-salmon", "Lee", 5, 30, stretch: true),
        DinnerChoice.EatOut(Monday.AddDays(4), "Everyone is out on Friday."),
        Cooked(5, "turkey-meatballs", "Sam", 5, 15),
        DinnerChoice.Leftovers(Monday.AddDays(6), "Saturday's meatballs, again."),
    ]);

    public static IReadOnlyDictionary<RecipeId, DishFacts> Dishes() => new Dictionary<RecipeId, DishFacts>
    {
        [Id("lentil-soup")] = new("Lentil soup, crusty bread"),
        [Id("sheet-pan-chicken")] = new("Sheet-pan chicken, potatoes"),
        [Id("black-bean-chili")] = new("Black bean chili", SlowCooker: true),
        [Id("miso-salmon")] = new("Miso salmon, rice, greens", IsNew: true),
        [Id("turkey-meatballs")] = new("Turkey meatballs, spaghetti"),
    };

    public static MealWeekSnapshot Snapshot(MealWeek? week = null) => new(week ?? Week(), Dishes());

    public static MealWeekSnapshot Friday(MealWeek? week = null) => Snapshot(week) with
    {
        EatingOut = new Dictionary<DateOnly, EatOutDetail>
        {
            [Monday.AddDays(4)] = new("Corner noodle house", new TimeOnly(18, 30), new TimeOnly(17, 45), "everyone"),
        },
    };
}
