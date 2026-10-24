using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealPlanning;

/// <summary>Invented dishes and an invented week for the dinner planner tests. Nothing here is a real recipe or a real evening.</summary>
internal static class MealData
{
    /// <summary>The Sunday before the week starts, at noon: nothing has passed.</summary>
    public static readonly DateTimeOffset SundayNoon = Edt(10, 25, 12);

    public static Recipe Dish(string id, Cuisine cuisine, bool forKids = false, int minutes = 40) => new(
        new RecipeId(id),
        "Invented " + id.Replace('-', ' '),
        cuisine,
        [new Ingredient("1", "cup", "water")],
        [new RecipeStep("Cook it.", minutes, true)],
        4,
        null,
        forKids);

    /// <summary>Twelve cards over five cuisines; four are for the children.</summary>
    public static List<Recipe> Box() =>
    [
        Dish("saffron-rice", Cuisine.Indian, forKids: true),
        Dish("lentil-curry", Cuisine.Indian),
        Dish("tomato-pasta", Cuisine.Italian, forKids: true),
        Dish("mushroom-risotto", Cuisine.Italian),
        Dish("bean-burritos", Cuisine.Mexican, forKids: true),
        Dish("corn-tacos", Cuisine.Mexican),
        Dish("noodle-stir-fry", Cuisine.Asian),
        Dish("dumpling-soup", Cuisine.Asian),
        Dish("herb-omelette", Cuisine.French),
        Dish("onion-tart", Cuisine.French),
        Dish("roast-chicken", Cuisine.American, forKids: true),
        Dish("veggie-stew", Cuisine.Other),
    ];

    public static IReadOnlySet<DateOnly> AllEvenings() => Enumerable.Range(0, 7).Select(offset => Monday.AddDays(offset)).ToHashSet();

    /// <summary>A request over an invented week. Calendars are plain lists of entries; the rest is optional.</summary>
    public static MealWeekRequest Request(
        IEnumerable<CalendarEntry>? sam = null,
        IEnumerable<CalendarEntry>? lee = null,
        IEnumerable<CalendarEntry>? family = null,
        IReadOnlyList<Recipe>? recipes = null,
        IReadOnlySet<DateOnly>? childrenHome = null,
        IEnumerable<RecipeId>? soon = null,
        IReadOnlyList<CookedMeal>? history = null,
        IReadOnlyDictionary<RecipeId, IReadOnlySet<string>>? neverAgain = null,
        decimal? budget = null,
        int seed = 1,
        DateTimeOffset? now = null,
        HouseholdCalendarSnapshot? calendars = null)
    {
        var rules = Rules();
        var snapshot = calendars ?? Calendars(sam, lee, family);
        return new MealWeekRequest(
            Monday,
            recipes ?? Box(),
            rules,
            AdultTime(rules, snapshot),
            childrenHome ?? new HashSet<DateOnly>(),
            soon?.ToHashSet() ?? new HashSet<RecipeId>(),
            history,
            neverAgain,
            budget,
            seed,
            now ?? SundayNoon);
    }

    public static List<BusyTime> AdultTime(PlanningRules rules, IHouseholdCalendars calendars) =>
        rules.Adults.Select(adult => BusyTime.For(adult.Name, Monday, Monday.AddDays(6), rules, calendars)).ToList();

    public static CalendarEntry On(DateOnly day, string title, int fromHour, int fromMinute, int toHour, int toMinute) =>
        CalendarEntry.Timed(title, Edt(day.Month, day.Day, fromHour, fromMinute), Edt(day.Month, day.Day, toHour, toMinute));

    public static RecipeId Id(string value) => new(value);

    public static IReadOnlyList<CookedMeal> Cooked(params (string Recipe, int DaysBeforeMonday)[] meals) =>
        meals.Select(meal => new CookedMeal(Id(meal.Recipe), Monday.AddDays(-meal.DaysBeforeMonday))).ToList();

    public static IReadOnlyDictionary<RecipeId, IReadOnlySet<string>> Never(string recipe, params string[] people) =>
        new Dictionary<RecipeId, IReadOnlySet<string>> { [Id(recipe)] = people.ToHashSet() };

    public static IEnumerable<DinnerChoice> Cooks(MealWeek week) => week.Dinners.Where(dinner => dinner.Kind == DinnerKind.Cooked);
}
