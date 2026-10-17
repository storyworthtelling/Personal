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

    public bool IsRestDay => WorkoutStart is null;
}
