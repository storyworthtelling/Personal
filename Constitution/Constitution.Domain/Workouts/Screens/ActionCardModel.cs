namespace Constitution.Domain.Workouts.Screens;

public enum WorkoutActionId
{
    SkipThisDay,
    SwapWith,
    MoveWorkoutHere,
}

public sealed record WorkoutAction(WorkoutActionId Id, string Label);

/// <summary>The card a long-press on a day raises (C-5.4.2): its heading and exactly the actions that make sense for that day.</summary>
public sealed class ActionCardModel
{
    private ActionCardModel(string title, string? subtitle, IReadOnlyList<WorkoutAction> actions)
    {
        Title = title;
        Subtitle = subtitle;
        Actions = actions;
    }

    public string Title { get; }

    public string? Subtitle { get; }

    public IReadOnlyList<WorkoutAction> Actions { get; }

    public static ActionCardModel For(DayClipping day)
    {
        ArgumentNullException.ThrowIfNull(day);
        if (!day.CanPress)
        {
            throw new InvalidOperationException("This day has nothing to act on, so it has no card.");
        }

        if (day.State == DayState.Rest)
        {
            return new ActionCardModel(
                $"{day.LongDay} · Rest",
                "No workout planned",
                [new WorkoutAction(WorkoutActionId.MoveWorkoutHere, "Move a workout here…")]);
        }

        return new ActionCardModel(
            $"{day.LongDay} · {day.KindWord} · {day.Minutes} min",
            day.OnCalendar ? $"{day.StartText} · on your calendar" : day.StartText,
            [
                new WorkoutAction(WorkoutActionId.SkipThisDay, "Skip this day"),
                new WorkoutAction(WorkoutActionId.SwapWith, "Swap with…"),
            ]);
    }
}
