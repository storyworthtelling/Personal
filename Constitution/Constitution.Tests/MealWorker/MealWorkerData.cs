using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealWorker;

/// <summary>Invented weeks, records and stamps for the meal worker's tests. The week opening on Sunday 25 October has its deadline at 8:00 that morning.</summary>
internal static class MealWorkerData
{
    public static readonly DateOnly NextSunday = new(2026, 11, 1);

    public static MealWeek WeekOf(DateOnly start) => MealWeek.Planned(
        start,
        Enumerable.Range(0, 7).Select(offset => offset == 1
            ? DinnerChoice.Cooked(start.AddDays(1), new RecipeId("lemon-rice"), Sam, Edt(10, 26, 17, 15), Edt(10, 26, 18), false, false, "From the box.")
            : DinnerChoice.NothingPlanned(start.AddDays(offset), "Nothing is planned.")),
        []);

    public static StoredMealWeek Stored(DateOnly start) => new(WeekOf(start), Edt(10, 21, 12), [new StoredDish(new RecipeId("lemon-rice"), "Invented lemon rice")]);

    public static MealWeekRecordRead.Held Kept(params DateOnly[] starts) => new(new MealWeekRecord(starts.Select(Stored).ToList()));

    public static MealDecisionRecordRead.Held Decided(DateOnly week, bool evidencePending = false) =>
        new(new MealDecisionRecord([new MealDecisionEntry(week, Edt(10, 25, 8, 5), 8, 0, 8, evidencePending)]));

    public static MealPassStampRead.Held Stamp(
        DateTimeOffset? settledAt = null, DateOnly? week = null, DateTimeOffset? planTried = null, DateTimeOffset? deadlineTried = null) =>
        new(new MealPassStamp(settledAt, week, planTried, deadlineTried));
}
