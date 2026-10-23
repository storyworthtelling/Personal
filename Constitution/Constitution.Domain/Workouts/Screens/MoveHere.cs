namespace Constitution.Domain.Workouts.Screens;

/// <summary>Move a workout here… (C-5.4.5): from a rest day, the days whose session fits this day's gaps, the rest greyed with reasons.</summary>
public static class MoveHere
{
    public static ChoiceCard For(WorkoutsSnapshot snapshot, DateOnly restDay)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rows = SwapChoices.Window(snapshot)
            .Where(day => day != restDay && SessionMover.IsPlanned(snapshot, day) && snapshot.SessionOn(day) is not null)
            .Select(day => new ChoiceRow(
                day,
                WorkoutWords.ShortDay(day),
                SwapChoices.Describe(snapshot, day),
                SessionMover.Check(snapshot, day, restDay).Reason))
            .ToList();
        return new ChoiceCard($"Move a workout to {WorkoutWords.LongDay(restDay)}", "Pick the day it comes from", rows);
    }
}
