using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Workouts;

/// <summary>
/// The invented week every planner test stands on: the household's week of Sunday 1 November 2026 to Saturday 7 November. The
/// clocks went back at 2 am on that first Sunday and every waking hour is on standard time (UTC-5), so every expected instant is
/// written out and none is computed with the code under test. Day offsets count from the week's first day: 0 is Sunday, 1 Monday,
/// 6 Saturday.
/// Sam works 8 to 4 and Lee 8 to 3:30 on weekdays; dinner is 6 to 7 and the children's routine runs
/// 7:15 to their bedtime at 8. Sleep is not in any of that, which is what the waking hours are for
/// (6:30 am to 9:30 pm unless a test says otherwise).
/// </summary>
internal static class WorkoutFixture
{
    public static readonly DateOnly WeekStart = new(2026, 11, 1);

    /// <summary>An instant <paramref name="dayOffset"/> days after the week's Sunday, on the clock.</summary>
    public static DateTimeOffset At(int dayOffset, int hour, int minute = 0) => Est(11, 1 + dayOffset, hour, minute);

    public static DateOnly Day(int dayOffset) => WeekStart.AddDays(dayOffset);

    // ---- the run's own tests count from the Monday it runs on ----
    // The weekly run is tested on the morning it normally runs, Monday 2 November at 5:30, with the week already one day old.
    // Those tests say "day 0" for that morning's workout and "day 1" for the next, so these three count from the Monday
    // (offset -1 is the week's Sunday); the planner and the placement tests count from the week's first day, above.

    /// <summary>The Monday of the fixture's week, the day the run's tests treat as today.</summary>
    public static readonly DateOnly FixtureMonday = WeekStart.AddDays(1);

    /// <summary>An instant <paramref name="dayOffset"/> days after the fixture's Monday, on the clock.</summary>
    public static DateTimeOffset MondayAt(int dayOffset, int hour, int minute = 0) => At(dayOffset + 1, hour, minute);

    /// <summary>The date <paramref name="dayOffset"/> days after the fixture's Monday.</summary>
    public static DateOnly MondayDay(int dayOffset) => FixtureMonday.AddDays(dayOffset);

    public static int MondayDayIndex(PlannedSession session) => session.Day.DayNumber - FixtureMonday.DayNumber;

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
