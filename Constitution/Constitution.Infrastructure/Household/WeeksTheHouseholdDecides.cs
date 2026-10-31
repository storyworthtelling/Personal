using Constitution.Domain.Meals.Pipeline;

namespace Constitution.Infrastructure.Household;

/// <summary>
/// The kept weeks as the phone that holds the planning lease decides from them (D154): this phone's own, plus any week the other phone planned and published to the household file that this
/// phone has no copy of. After a hand-over the new lease holder has never kept the old planner's week, and a week it cannot see is a week nothing is decided for (and a denied dinner's cook block
/// stays); the household's record of it stands in. <b>This is the opposite of <see cref="KeptWeeksBesideTheHousehold"/></b>, which takes the other phone's weeks away because the recipe run delivers
/// them from the household's record instead; deciding has no second copy to avoid, only a missing one to fill.
///
/// <para>A week this phone kept itself is never replaced by a published copy of it, and this phone's own mirror in the file adds nothing. An unreadable own record, or a household file that
/// will not open, is returned as it is: failing closed is the deadline step's own typed stop, not something to read around. A decision's changes are saved to this phone's own record only.</para>
/// </summary>
public sealed class WeeksTheHouseholdDecides(IMealWeekRecords own, IWeekPlanSource plans) : IMealWeekRecords
{
    public MealWeekRecordRead Load()
    {
        var local = own.Load();
        if (local is MealWeekRecordRead.Unreadable || plans.Read() is not WeekPlansRead.Held held)
        {
            return local;
        }

        var record = (local as MealWeekRecordRead.Held)?.Record ?? MealWeekRecord.Empty;
        var missing = SharedWeeks.FromOthers(held.Set, held.Me).Where(plan => record.For(plan.WeekStart) is null).Select(plan => plan.Stored).ToList();
        return missing.Count == 0
            ? local
            : new MealWeekRecordRead.Held(new MealWeekRecord(record.Weeks.Concat(missing).OrderBy(week => week.WeekStart).TakeLast(MealWeekRecord.Kept).ToList()));
    }

    public void Save(MealWeekRecord record) => own.Save(record);
}
