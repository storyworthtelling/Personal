namespace Constitution.Domain.Documents;

/// <summary>
/// The ids of the targets on the workout and assessment pages (book p.5 and p.6). They are what the
/// ink reader's rules name (<c>ExercisePageRule(subject, Done, Rating)</c>, <c>StillSoreRule(subject, StillSore)</c>,
/// <c>NumberRule(subject, Result)</c>), so they are frozen words and a page never invents its own.
/// A lane's id is the group and its place: <c>feel-1</c> is "easy", <c>feel-3</c> is "hard".
/// </summary>
public static class WorkoutIds
{
    /// <summary>The large Done tick fixed at the foot of every page.</summary>
    public const string Done = "done";

    /// <summary>The easy / right / hard circle on an exercise page (a lane group).</summary>
    public const string Rating = "feel";

    /// <summary>The 91 point notes column.</summary>
    public const string Notes = "notes";

    /// <summary>The injury-day "Still sore?" tick; a mark means still sore, blank means better (D28).</summary>
    public const string StillSore = "sore";

    /// <summary>The one open number box on an assessment test page.</summary>
    public const string Result = "result";

    /// <summary>The circle-a-word choice on an assessment mobility page (a lane group).</summary>
    public const string Choice = "choice";

    /// <summary>Room kept on a page for the exercise's figure (C-2.3.3 draws into it); nothing is printed in it here.</summary>
    public const string FigureSlot = "figure-slot";

    public static IReadOnlyList<string> RatingWords { get; } = ["easy", "right", "hard"];
}
