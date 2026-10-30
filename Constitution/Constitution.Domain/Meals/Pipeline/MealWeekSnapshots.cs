using Constitution.Domain.Meals.Screens;


namespace Constitution.Domain.Meals.Pipeline;

/// <summary>A saved week as the This week view and Today's dinner card read it.</summary>
public static class MealWeekSnapshots
{
    /// <summary>
    /// The week saved for the day: the one that contains it, else the next one to come, else nothing (a week that is over is not a
    /// plan). The dishes are as they were titled when the week was planned; an evening out has no detail and no approval is recorded.
    /// </summary>
    public static StoredMealWeek? Pick(MealWeekRecord record, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Weeks.FirstOrDefault(week => week.WeekStart <= today && today < week.WeekStart.AddDays(MealWeek.Evenings))
            ?? record.Weeks.Where(week => week.WeekStart > today).OrderBy(week => week.WeekStart).FirstOrDefault();
    }

    public static MealWeekSnapshot From(StoredMealWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);
        var dishes = week.Dishes
            .GroupBy(dish => dish.Recipe)
            .ToDictionary(group => group.Key, group => new DishFacts(group.First().Title, group.First().SlowCooker, group.First().IsNew));
        return new MealWeekSnapshot(week.Week, dishes);
    }
}
