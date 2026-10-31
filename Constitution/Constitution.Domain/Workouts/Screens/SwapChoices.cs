using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>One line of a day-choosing card: the day, what is on it, and why it is greyed (null when it can be picked).</summary>
public sealed record ChoiceRow(DateOnly Day, string DayLabel, string Text, string? Reason)
{
    public bool IsEnabled => Reason is null;
}

/// <summary>A card of days to pick from: a title, a line under it, and every candidate day, none hidden (D51, D52).</summary>
public sealed record ChoiceCard(string Title, string Subtitle, IReadOnlyList<ChoiceRow> Rows);

/// <summary>One session going from where it was to where it is now, with the event it had on the calendar.</summary>
public sealed record SessionMove(PlannedSession Old, PlannedSession New, PlacedTime? OldPlaced);

/// <summary>The outcome of a swap or a move: the moves, and the weeks before and after, so the plan and the calendar can both follow.</summary>
public sealed record SwapResult(string Person, IReadOnlyList<SessionMove> Moves, IReadOnlyList<WorkoutWeek> Before, IReadOnlyList<WorkoutWeek> After)
{
    /// <summary>
    /// The record of issued workout numbers the change was checked against (the snapshot's), which is what names the events it moves.
    /// The calendar follow prefers the record as it stands when it acts, since a run may have issued more since the tab loaded.
    /// </summary>
    public KeyAssignments? Assignments { get; init; }
}

/// <summary>The answer to "may this day go there": a result, or the plain reason it may not.</summary>
public sealed record MoveCheck(SwapResult? Result, string? Reason)
{
    public bool IsAllowed => Result is not null;
}

/// <summary>Swap with… (C-5.4.3): the other days, each checked against the planner's own rules and the calendar's gaps.</summary>
public static class SwapChoices
{
    private const int DaysAhead = 7;

    public static ChoiceCard For(WorkoutsSnapshot snapshot, DateOnly from)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var mover = snapshot.SessionOn(from)
            ?? throw new InvalidOperationException("A rest day has nothing to swap; it offers Move a workout here instead.");
        var rows = Candidates(snapshot, from)
            .Select(day => RowFor(snapshot, from, day))
            .ToList();
        return new ChoiceCard(
            $"Swap {WorkoutWords.LongDay(from)}’s {WorkoutWords.Kind(mover).ToLowerInvariant()} with…",
            "Both move; the calendar follows",
            rows);
    }

    /// <summary>Moves the session on <paramref name="from"/> to <paramref name="to"/> and, if a session is there, the other way.</summary>
    public static MoveCheck Check(WorkoutsSnapshot snapshot, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return SessionMover.Check(snapshot, from, to);
    }

    /// <summary>The snapshot after the moves: the weeks replaced, and each session's Score setting and event time carried to its new day.</summary>
    public static WorkoutsSnapshot Apply(WorkoutsSnapshot snapshot, SwapResult result)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(result);
        var weeks = snapshot.Weeks
            .Select(week => result.After.FirstOrDefault(after => after.Plan.WeekStart == week.Plan.WeekStart) ?? week)
            .ToList();

        // A session's setting and its event go where it goes. A planner's reason ("moved indoors: rain") described
        // the session on that day and no longer holds, so it is dropped from both days.
        var facts = snapshot.Facts.ToDictionary(pair => pair.Key, pair => pair.Value);
        var carried = result.Moves.Select(move => (move, facts: snapshot.FactsOn(move.Old.Day))).ToList();
        foreach (var (move, _) in carried)
        {
            facts.Remove(move.Old.Day);
        }

        foreach (var (move, old) in carried)
        {
            facts[move.New.Day] = old with
            {
                ChangeReason = null,
                OnCalendar = old.OnCalendar is null ? null : new PlacedTime(move.New.Start, move.New.End),
            };
        }

        return snapshot with { Weeks = weeks, Facts = facts };
    }

    /// <summary>The seven days after today, which are the days a card may offer.</summary>
    internal static IEnumerable<DateOnly> Window(WorkoutsSnapshot snapshot) =>
        Enumerable.Range(1, DaysAhead).Select(snapshot.Today.AddDays);

    private static IEnumerable<DateOnly> Candidates(WorkoutsSnapshot snapshot, DateOnly from) =>
        Window(snapshot).Where(day => day != from);

    private static ChoiceRow RowFor(WorkoutsSnapshot snapshot, DateOnly from, DateOnly day)
    {
        var check = Check(snapshot, from, day);
        return new ChoiceRow(day, WorkoutWords.ShortDay(day), Describe(snapshot, day), check.Reason);
    }

    internal static string Describe(WorkoutsSnapshot snapshot, DateOnly day)
    {
        if (!SessionMover.IsPlanned(snapshot, day))
        {
            return "Not planned yet";
        }

        return snapshot.SessionOn(day) is { } session
            ? $"{WorkoutWords.Kind(session)} · {WorkoutWords.Minutes(session)} min"
            : "Rest";
    }
}
