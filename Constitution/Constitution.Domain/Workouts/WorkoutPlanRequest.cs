using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>
/// What the planner is told about one person beyond their calendar. Every field is a seam for a later
/// PBI (done and skipped days, soreness, recovery signals); this PBI only reads the three below, and an
/// install with no history passes <see cref="Fresh"/>.
/// </summary>
public sealed record RecoveryInputs
{
    /// <summary>Sessions finished last week, or null when there is no history (a first install).</summary>
    public int? SessionsDoneLastWeek { get; init; }

    /// <summary>Sessions that were planned last week and not done.</summary>
    public int SessionsSkippedLastWeek { get; init; }

    /// <summary>The most recent hard (strength or cardio) session before this week, if any; the day after it is not hard.</summary>
    public DateOnly? LastHardSessionDay { get; init; }

    public static RecoveryInputs Fresh => new();
}

/// <summary>One person's calendar for the week and what is known of their recent training.</summary>
public sealed record PersonWeek(BusyTime Busy)
{
    public RecoveryInputs Recovery { get; init; } = RecoveryInputs.Fresh;
}

/// <summary>
/// The hours of the day a workout may fall in. Sleep is not in the never-placed time (that is work,
/// dinner and the children's routine), so without these bounds an empty night would be a free gap.
/// </summary>
public sealed record WorkoutSettings
{
    public TimeOnly WakingStart { get; init; } = new(6, 30);

    public TimeOnly WakingEnd { get; init; } = new(21, 30);
}

/// <summary>
/// Everything <see cref="WorkoutPlanner.Plan"/> reads, passed in: no clock, no store, no hidden state.
/// <see cref="Now"/> is the clock; nothing before it is planned.
/// </summary>
/// <param name="WeekStart">The first of seven consecutive days (a Monday in practice; only the date is used).</param>
/// <param name="Rules">The household rules; the zone and the children's bedtime come from here.</param>
/// <param name="People">One entry per adult to plan for. Two or more allows joint sessions.</param>
/// <param name="Equipment">What this install has ticked in Settings.</param>
/// <param name="Now">The moment of planning.</param>
public sealed record WorkoutPlanRequest(
    DateOnly WeekStart,
    PlanningRules Rules,
    IReadOnlyList<PersonWeek> People,
    EquipmentSet Equipment,
    DateTimeOffset Now)
{
    public WorkoutSettings Settings { get; init; } = new();
}
