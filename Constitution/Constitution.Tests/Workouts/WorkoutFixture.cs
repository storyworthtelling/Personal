using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Workouts;

/// <summary>
/// The invented week every planner test stands on: Monday 2 November 2026, all on standard time
/// (UTC-5), so every expected instant is written out and none is computed with the code under test.
/// Sam works 8 to 4 and Lee 8 to 3:30 on weekdays; dinner is 6 to 7 and the children's routine runs
/// 7:15 to their bedtime at 8. Sleep is not in any of that, which is what the waking hours are for
/// (6:30 am to 9:30 pm unless a test says otherwise).
/// </summary>
internal static class WorkoutFixture
{
    public static readonly DateOnly WeekStart = new(2026, 11, 2);

    /// <summary>An instant <paramref name="dayOffset"/> days after the week's Monday, on the clock.</summary>
    public static DateTimeOffset At(int dayOffset, int hour, int minute = 0) => Est(11, 2 + dayOffset, hour, minute);

    public static DateOnly Day(int dayOffset) => WeekStart.AddDays(dayOffset);

    /// <summary>A whole day that is spoken for.</summary>
    public static IEnumerable<CalendarEntry> BlockDay(int dayOffset) =>
        [CalendarEntry.Timed("Away", At(dayOffset, 0), At(dayOffset + 1, 0))];

    /// <summary>Everything on the day is spoken for except the stretch from <paramref name="from"/> to <paramref name="to"/> (hours, minutes).</summary>
    public static IEnumerable<CalendarEntry> OnlyFree(int dayOffset, (int Hour, int Minute) from, (int Hour, int Minute) to) =>
    [
        CalendarEntry.Timed("Busy", At(dayOffset, 0), At(dayOffset, from.Hour, from.Minute)),
        CalendarEntry.Timed("Busy", At(dayOffset, to.Hour, to.Minute), At(dayOffset + 1, 0)),
    ];

    public static IEnumerable<CalendarEntry> BlockDays(params int[] dayOffsets) => dayOffsets.SelectMany(BlockDay);

    public static BusyTime BusyFor(string person, HouseholdCalendarSnapshot calendars, int weekShift = 0, PlanningRules? rules = null) =>
        BusyTime.For(person, WeekStart.AddDays(weekShift), WeekStart.AddDays(weekShift + 6), rules ?? Rules(), calendars);

    public static WorkoutPlanRequest Request(
        HouseholdCalendarSnapshot calendars,
        EquipmentSet? equipment = null,
        RecoveryInputs? sam = null,
        RecoveryInputs? lee = null,
        DateTimeOffset? now = null,
        bool bothAdults = true,
        int weekShift = 0,
        PlanningRules? rules = null)
    {
        var used = rules ?? Rules();
        var people = new List<PersonWeek> { new(BusyFor(Sam, calendars, weekShift, used)) { Recovery = sam ?? RecoveryInputs.Fresh } };
        if (bothAdults)
        {
            people.Add(new PersonWeek(BusyFor(Lee, calendars, weekShift, used)) { Recovery = lee ?? RecoveryInputs.Fresh });
        }

        return new WorkoutPlanRequest(
            WeekStart.AddDays(weekShift),
            used,
            people,
            equipment ?? EquipmentSet.Everything(),
            now ?? Est(10, 30, 12));
    }

    public static HouseholdWorkoutPlan Plan(HouseholdCalendarSnapshot calendars, EquipmentSet? equipment = null, bool bothAdults = true, RecoveryInputs? sam = null, RecoveryInputs? lee = null, DateTimeOffset? now = null, int weekShift = 0) =>
        WorkoutPlanner.Plan(Request(calendars, equipment, sam, lee, now, bothAdults, weekShift));

    public static WeeklyPlan SamOf(HouseholdWorkoutPlan plan) => plan.PlanFor(Sam)!;

    public static WeeklyPlan LeeOf(HouseholdWorkoutPlan plan) => plan.PlanFor(Lee)!;

    public static int DayIndex(PlannedSession session) => session.Day.DayNumber - WeekStart.DayNumber;
}
