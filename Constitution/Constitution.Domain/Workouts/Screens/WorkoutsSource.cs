using Constitution.Domain.Calendar;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>Where a session's calendar event actually sits, as the calendar says (the placement step's word, not the planner's proposal).</summary>
public sealed record PlacedTime(DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// What the tab knows about one day beyond the plan itself. <paramref name="ScoreBpm"/> is the one headline Score setting;
/// <paramref name="ChangeReason"/> is the planner's one-line reason when it changed something; <paramref name="OnCalendar"/>
/// is the event's real time, or null when none was written; <paramref name="PagesOnTablet"/> is whether the pages have gone.
/// </summary>
public sealed record DayFacts(int? ScoreBpm = null, string? ChangeReason = null, PlacedTime? OnCalendar = null, bool PagesOnTablet = false);

/// <summary>A week of the plan with the request it was made from, so a change can be held to the same rules (<see cref="PlanRules"/>).</summary>
public sealed record WorkoutWeek(WorkoutPlanRequest Request, HouseholdWorkoutPlan Plan);

/// <summary>Everything the Workouts tab is made from. The window is today and the seven days after it; weeks may be one or two.</summary>
public sealed record WorkoutsSnapshot(
    string Person,
    DateOnly Today,
    DateTimeOffset Now,
    IReadOnlyList<WorkoutWeek> Weeks,
    IReadOnlyDictionary<DateOnly, DayFacts> Facts,
    SkipLedger Skips)
{
    /// <summary>The week of the plan that holds the day, or null when no plan was made for it.</summary>
    public WorkoutWeek? WeekOf(DateOnly day) =>
        Weeks.FirstOrDefault(week => day >= week.Plan.WeekStart && day < week.Plan.WeekStart.AddDays(7));

    /// <summary>This person's plan for the day's week, or null.</summary>
    public WeeklyPlan? PersonPlanOf(DateOnly day) => WeekOf(day)?.Plan.PlanFor(Person);

    /// <summary>The person's session on the day, or null on a rest day.</summary>
    public PlannedSession? SessionOn(DateOnly day) => PersonPlanOf(day)?.SessionsOn(day).FirstOrDefault();

    public DayFacts FactsOn(DateOnly day) => Facts.TryGetValue(day, out var facts) ? facts : new DayFacts();

    public bool IsSkipped(DateOnly day) => Skips.OutcomeOf(Person, day) == SessionOutcome.Skipped;
}

/// <summary>What reading the plan gave: a plan, or the plain fact that there is none yet.</summary>
public abstract record WorkoutsRead
{
    public sealed record NoPlanYet : WorkoutsRead;

    public sealed record Ready(WorkoutsSnapshot Snapshot) : WorkoutsRead;
}

/// <summary>
/// Where the tab's plan comes from, and where its changes go. The weekly pipeline fills this; until it does the tab says
/// there is no plan. <see cref="Read"/> may be slow (calendars), so the head calls it off the UI thread.
/// </summary>
public interface IWorkoutsSource
{
    WorkoutsRead Read(DateTimeOffset now);

    /// <summary>Keeps a change: the swapped plan, or the skip written to the ledger. Called once per change.</summary>
    void Record(WorkoutsChange change);
}

/// <summary>A change the owner made on the tab that the source has to keep.</summary>
public abstract record WorkoutsChange
{
    public sealed record Swapped(SwapResult Swap) : WorkoutsChange;

    public sealed record SkipCommitted(PendingSkip Skip, SkipLedger Ledger) : WorkoutsChange;
}

/// <summary>The source used until the pipeline is wired: it has no plan, and says so.</summary>
public sealed class NoWorkoutsSource : IWorkoutsSource
{
    public WorkoutsRead Read(DateTimeOffset now) => new WorkoutsRead.NoPlanYet();

    public void Record(WorkoutsChange change) => throw new InvalidOperationException("There is no plan to change yet.");
}
