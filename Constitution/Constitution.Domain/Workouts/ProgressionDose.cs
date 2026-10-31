using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>What the owner circled on an exercise page (p.5): the three words of D23.</summary>
public enum ExerciseRating
{
    Easy,

    Right,

    Hard,
}

public static class ExerciseRatings
{
    /// <summary>The rating a circled word means, or null for a word that is not one of the three (never guessed at).</summary>
    public static ExerciseRating? FromWord(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "easy" => ExerciseRating.Easy,
        "right" => ExerciseRating.Right,
        "hard" => ExerciseRating.Hard,
        _ => null,
    };
}

/// <summary>
/// What one exercise asks for in one session: sets, reps, and the weight in pounds (D22) for the one kind of exercise
/// that has one, a dumbbell or kettlebell lift. A bodyweight or band exercise has <see cref="Pounds"/> of zero.
/// </summary>
public sealed record ExerciseDose(int Sets, int Reps, int Pounds)
{
    public bool IsWeighted => Pounds > 0;

    /// <summary>All the reps in the session: the one number that goes up whichever way the dose went up.</summary>
    public int TotalReps => Sets * Reps;

    /// <summary>The dose as the page prints it large: "2 × 8".</summary>
    public string Text => $"{Sets} × {Reps}";
}

/// <summary>
/// The ladder an exercise climbs (C-5.1.3). A beginner's plan is too hard before it is too easy (the phase's named
/// risk), so every step is small, and a rung is only taken after the same exercise was rated easy twice running.
///
/// <para><b>Weighted</b> exercises (a dumbbell or kettlebell lift) hold at two sets of eight and move in
/// <see cref="PoundStep"/> pounds. <b>Everything else</b> climbs a ladder of reps and sets whose total strictly
/// rises, so "up" means the same thing on every rung.</para>
///
/// <para>Every number here is a plan decision with no source in the book; the constants are named so a change is one
/// edit and one test.</para>
/// </summary>
internal static class DoseLadder
{
    /// <summary>Consecutive easy ratings of one exercise that move it up.</summary>
    public const int EasyRunToProgress = 2;

    public const int WeightedSets = 2;

    public const int WeightedReps = 8;

    public const int StartingPounds = 10;

    public const int PoundStep = 5;

    public const int MinimumPounds = 5;

    /// <summary>Bodyweight and band rungs, easiest first. The totals are 12, 16, 20, 24, 30, 36.</summary>
    public static readonly (int Sets, int Reps)[] Rungs = [(2, 6), (2, 8), (2, 10), (2, 12), (3, 10), (3, 12)];

    public static int TopRung => Rungs.Length - 1;

    public static bool IsWeighted(Exercise exercise) => exercise.EquipmentId == EquipmentCatalogue.DumbbellsId;

    public static ExerciseDose DoseOf(bool weighted, int rung, int pounds) =>
        weighted
            ? new ExerciseDose(WeightedSets, WeightedReps, pounds)
            : new ExerciseDose(Rungs[rung].Sets, Rungs[rung].Reps, 0);
}
