using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts.Screens;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// One session the owner moved on the Workouts tab. <paramref name="Key"/> names it the way the tab and the calendar do (the
/// placement key of the session in the plan as it stood when the owner moved it: kind and place among its kind, never the date),
/// <paramref name="FromDay"/> is the day it left, and the rest is where it went (its start, and the free gap it was fitted into).
/// The session itself rides along, so a week that only receives it (a move across the Saturday-to-Sunday edge) can be made without the week it left.
/// </summary>
public sealed record SessionMoved(
    string Person,
    string Key,
    DateOnly FromDay,
    DateOnly Day,
    DateTimeOffset Start,
    FreeGap Gap,
    Session Session);

/// <summary>One swap or move as the owner made it: every session in it goes together, since each one's key is read before any of them has moved.</summary>
public sealed record MoveGroup(IReadOnlyList<SessionMoved> Moves);

/// <summary>A session the owner skipped by hand. It stays in the plan (taking it out would renumber the keys of those after it), and gets no event and no page.</summary>
public sealed record SessionSkipped(string Person, DateOnly Day);

/// <summary>A session as it moved from the base plan to where the owner put it.</summary>
public sealed record Relocation(PlannedSession Old, PlannedSession New);

/// <summary>What <see cref="PlanOverrides.ApplyTo"/> made: the plan with the owner's moves in it, and what it could not place.</summary>
public sealed record AppliedOverrides(HouseholdWorkoutPlan Plan, IReadOnlyList<Relocation> Relocated, IReadOnlyList<string> Unresolved);

/// <summary>
/// The owner's hand on the plan: the swaps, moves and skips made on the Workouts tab, kept so the weekly run, which re-derives
/// its base plan each time, plans the week the owner left rather than the one they changed.
/// </summary>
public sealed record PlanOverrides(IReadOnlyList<MoveGroup> Groups, IReadOnlyList<SessionSkipped> Skips)
{
    public static PlanOverrides None { get; } = new([], []);

    public bool IsEmpty => Groups.Count == 0 && Skips.Count == 0;

    /// <summary>These overrides and then <paramref name="later"/>: the moves in the order they were made, a skip once.</summary>
    public PlanOverrides Then(PlanOverrides later)
    {
        ArgumentNullException.ThrowIfNull(later);
        return new([.. Groups, .. later.Groups], [.. Skips, .. later.Skips]);
    }

    /// <summary>What the owner's change was, as an override: the producer the tab's change goes through.</summary>
    public static PlanOverrides From(WorkoutsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change switch
        {
            WorkoutsChange.Swapped swapped => new(
                [new MoveGroup(swapped.Swap.Moves.Select(move => Moved(swapped.Swap, move)).ToList())], []),
            WorkoutsChange.SkipCommitted skipped => new([], [new SessionSkipped(skipped.Skip.Person, skipped.Skip.Day)]),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "The tab made a change this record does not know."),
        };
    }

    /// <summary>Whether the owner skipped the person's session on that day.</summary>
    public bool IsSkipped(string person, DateOnly day) =>
        Skips.Any(item => Same(item.Person, person) && item.Day == day);

    /// <summary>
    /// The week with the owner's moves in it (skips leave the plan alone). A pure function of the plan, these overrides and the record of
    /// issued workout numbers (<paramref name="assignments"/>: a move names its session by the number it wore, so it is found again in a
    /// week that has been re-planned; null names sessions by their place among their kind). So a run
    /// on a week that has not changed makes the same plan every time. The moves are made in the order they were made, each group
    /// whole: every key in it is read from the plan before any of it has moved, which is how the tab named them.
    ///
    /// <para>A group whose session cannot be found by its key in this week (the plan has changed under it) is left out of this
    /// week entirely and named in <see cref="AppliedOverrides.Unresolved"/>: half a swap would put one session on two days.</para>
    /// </summary>
    public AppliedOverrides ApplyTo(HouseholdWorkoutPlan plan, KeyAssignments? assignments = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var current = plan;
        var relocated = new List<Relocation>();
        var unresolved = new List<string>();
        foreach (var group in Groups)
        {
            current = Apply(current, group, relocated, unresolved, assignments);
        }

        return new AppliedOverrides(current, relocated, unresolved);
    }

    private static SessionMoved Moved(SwapResult swap, SessionMove move) =>
        new(swap.Person, WorkoutKeys.In(swap.Before, swap.Person, move.Old, swap.Assignments).Value, move.Old.Day, move.New.Day, move.New.Start, move.New.Gap, move.New.Session);

    private static HouseholdWorkoutPlan Apply(HouseholdWorkoutPlan plan, MoveGroup group, List<Relocation> relocated, List<string> unresolved, KeyAssignments? assignments)
    {
        var leaving = group.Moves.Where(move => InWeek(plan, move.FromDay)).ToList();
        var arriving = group.Moves.Where(move => InWeek(plan, move.Day)).ToList();
        if (leaving.Count == 0 && arriving.Count == 0)
        {
            return plan;
        }

        var found = new Dictionary<SessionMoved, PlannedSession>();
        foreach (var move in leaving)
        {
            var old = Resolve(plan, move, assignments);
            if (old is null)
            {
                unresolved.Add($"{move.Person}: no {move.Key} in the week of {plan.WeekStart:yyyy-MM-dd}");
                return plan;
            }

            found[move] = old;
        }

        var arrived = arriving.Select(move => (Move: move, New: Arrive(move, found.GetValueOrDefault(move)))).ToList();
        foreach (var (move, made) in arrived)
        {
            if (found.TryGetValue(move, out var old))
            {
                relocated.Add(new Relocation(old, made));
            }
        }

        var plans = plan.Plans.Select(weekly =>
        {
            var gone = leaving.Where(move => Same(move.Person, weekly.Person)).Select(move => found[move]).ToList();
            var coming = arrived.Where(item => Same(item.Move.Person, weekly.Person)).Select(item => item.New).ToList();
            if (gone.Count == 0 && coming.Count == 0)
            {
                return weekly;
            }

            var sessions = weekly.Sessions
                .Where(session => !gone.Any(old => ReferenceEquals(old, session)))
                .Concat(coming)
                .OrderBy(session => session.Start)
                .ToList();
            return weekly with { Sessions = sessions };
        }).ToList();
        return plan with { Plans = plans };
    }

    private static PlannedSession? Resolve(HouseholdWorkoutPlan plan, SessionMoved move, KeyAssignments? assignments)
    {
        if (plan.PlanFor(move.Person) is null)
        {
            return null;
        }

        return WorkoutKeys.Of(plan, move.Person, assignments).FirstOrDefault(pair => pair.Value.Value == move.Key).Key;
    }

    // The session as it was, now where the owner put it; or, in a week that only receives it, the session as the record carried it.
    private static PlannedSession Arrive(SessionMoved move, PlannedSession? old) =>
        old is null
            ? new PlannedSession(move.Day, move.Start, move.Gap, move.Session, [move.Person], Together.None)
            : old with { Day = move.Day, Start = move.Start, Gap = move.Gap };

    private static bool InWeek(HouseholdWorkoutPlan plan, DateOnly day) => day >= plan.WeekStart && day < plan.WeekStart.AddDays(7);

    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Where the owner's moves are kept between runs. The tab adds to it as the owner acts; the run's caller reads it and hands
/// the result to the run in <c>WorkoutWeekRequest.PlanOverrides</c>.
/// </summary>
public interface IWorkoutOverrides
{
    PlanOverrides Load();

    /// <summary>Keeps these after everything already kept; a skip already kept is not kept twice.</summary>
    void Add(PlanOverrides more);
}
