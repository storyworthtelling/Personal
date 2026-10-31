using Constitution.Domain.Meals.Pipeline;

namespace Constitution.Infrastructure.Household;

/// <summary>
/// This phone's own kept weeks, read the way the recipe-hour run needs them once the household file carries the other phone's (D127): without the weeks the other phone has published, which
/// <see cref="Constitution.Infrastructure.Meals.MealSharedWeekRun"/> delivers from the household's record instead, so one week is never delivered from two copies. Read only by its type
/// (<see cref="IKeptWeeks"/> has no save). A file that will not open leaves this phone's own weeks exactly as they are.
/// </summary>
public sealed class KeptWeeksBesideTheHousehold(IKeptWeeks own, IWeekPlanSource plans) : IKeptWeeks
{
    public MealWeekRecordRead Load()
    {
        var local = own.Load();
        return plans.Read() is WeekPlansRead.Held held ? SharedWeeks.KeptAfter(local, held.Set, held.Me) : local;
    }
}
