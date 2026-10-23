using Constitution.Domain.Calendar;
using Constitution.Domain.Equipment;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// What one run is asked: which week, when it is, who the household is, what is ticked in Settings, and whose pages this
/// install delivers. The people planned are the rules' adults.
/// </summary>
/// <param name="WeekStart">The Monday of the week (only the date is used).</param>
/// <param name="Now">The moment of the run, on the household's clock offset. Nothing reads another clock.</param>
/// <param name="DeliverFor">The adults whose pages this phone puts on its tablet (each phone is paired to its own tablet, D85). Every name must be one of the rules' adults.</param>
public sealed record WorkoutWeekRequest(
    DateOnly WeekStart,
    DateTimeOffset Now,
    PlanningRules Rules,
    EquipmentSet Equipment,
    IReadOnlyList<string> DeliverFor)
{
    public WorkoutSettings Settings { get; init; } = new();

    /// <summary>
    /// How many days after today a page is sent ahead: 0 sends today's page only, which is what "the workout reaches the tablet
    /// each morning" means, and keeps a page from being read back (and a blank one read as a skip) before its day.
    /// </summary>
    public int DeliverAheadDays { get; init; }

    /// <summary>
    /// Which week the calendar is given. False (the default): the week as planned, before the weather, the morning's recovery,
    /// soreness or travel have had their say, which depends on nothing that changes from one run to the next, so the keys of the
    /// events already written keep naming the same days. True: the adapted week, which places a rained-out walk as a rest day
    /// (no event) on a first run but, on a later run, can write a second event beside the first (the writer cannot move or
    /// remove one) and renumber the keys. The paper and the Workouts tab always read the adapted week.
    /// </summary>
    public bool PlaceAdaptedWeek { get; init; }

    /// <summary>Meals, shopping and the like, placed before the workouts. None until the meal planner exists.</summary>
    public IReadOnlyList<HouseholdEventRequest> HouseholdEvents { get; init; } = [];

    public PlacementOptions Placement { get; init; } = new();

    /// <summary>
    /// What the owner has done to the plan on the Workouts tab: the swaps, moves and skips. The run re-derives its base plan each
    /// time, so without these a session the owner moved would be planned where it was, its event would read as gone and the page
    /// for the day it went to would never be made. None by default; the caller reads them from <c>IWorkoutOverrides</c>.
    /// </summary>
    public PlanOverrides PlanOverrides { get; init; } = PlanOverrides.None;

    /// <summary>Where bad weather begins; null for the defaults, with the room to shower taken from <see cref="Placement"/>.</summary>
    public ConditionsLimits? Conditions { get; init; }
}
