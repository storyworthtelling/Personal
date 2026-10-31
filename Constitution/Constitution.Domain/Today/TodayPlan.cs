using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Voice;

namespace Constitution.Domain.Today;

/// <summary>
/// What Today knows about the day, handed to the page when a plan exists (nothing does at Release A).
/// The workout is all of its parts or none of them: none is a rest day. Dinner is null until the meal
/// plan has one; the top line then stays neutral rather than guess.
/// </summary>
public sealed record TodayPlan
{
    public TodayPlan(TimeOnly? workoutStart, string? workoutKind, int? workoutMinutes, DinnerPlan? dinner)
    {
        var parts = new[] { workoutStart is not null, !string.IsNullOrWhiteSpace(workoutKind), workoutMinutes is not null };
        if (parts.Any(part => part) && !parts.All(part => part))
        {
            throw new ArgumentException("A workout has a start, a kind and a length, or none of them (a rest day).");
        }

        if (workoutMinutes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workoutMinutes), workoutMinutes, "A workout runs for some minutes.");
        }

        WorkoutStart = workoutStart;
        WorkoutKind = workoutKind?.Trim();
        WorkoutMinutes = workoutMinutes;
        Dinner = dinner;
    }

    public TimeOnly? WorkoutStart { get; }

    public string? WorkoutKind { get; }

    public int? WorkoutMinutes { get; }

    public DinnerPlan? Dinner { get; }

    /// <summary>The one Score setting the workout runs at, in beats a minute, or null while none is chosen.</summary>
    public int? ScoreBpm { get; init; }

    /// <summary>Tonight's dinner as its scrap says it (C-4.1.3), or null while the meal week has none to show.</summary>
    public DinnerClipping? DinnerCard { get; init; }

    public bool IsRestDay => WorkoutStart is null;

    /// <summary>The same day with its dinner filled in from the meal week: the dish for the top line and the card for the scrap.</summary>
    public TodayPlan WithDinner(DinnerPlan? dinner, DinnerClipping? card) =>
        new(WorkoutStart, WorkoutKind, WorkoutMinutes, dinner) { ScoreBpm = ScoreBpm, DinnerCard = card };
}
