using Constitution.Domain.Documents;

namespace Constitution.Tests.Documents;

/// <summary>
/// Invented workouts the document tests draw, shaped like the pages in book p.5 and p.6. Every name,
/// weight and sentence here is made up.
/// </summary>
internal static class WorkoutSamples
{
    public static readonly DateTime Tuesday = new(2026, 10, 20, 17, 30, 0);
    public static readonly DateTime AssessmentMonday = new(2026, 11, 2, 17, 30, 0);

    public static ExercisePageModel Squat(bool figure = true, string? voice = "Five more pounds today. You were ready for it last week.") =>
        new("Goblet squat", "3 × 10", new ExerciseLoad(25, "kettlebell"), 60, figure, 132, voice);

    public static ExercisePageModel Row() =>
        new("Band row", "3 × 12", new ExerciseLoad(null, "medium band"), 45, false, 118, "Same band, two more reps. Smooth beats fast.");

    public static ExercisePageModel Walk() =>
        new("Treadmill walk", "5 min", new ExerciseLoad(null, "easy pace"), null, false, 112, null);

    public static ExercisePageModel Bridge(string? substitution = null) =>
        new("Glute bridge", "3 × 12", new ExerciseLoad(null, "bodyweight"), null, false, 110,
            "Slow and steady today. The knee gets a quiet week.", substitution);

    public static IReadOnlyList<ExercisePageModel> Seven() =>
    [
        Walk(), Squat(), Row(),
        new("Incline push-up", "3 × 8", new ExerciseLoad(null, "hands on the bench"), 45, false, 124, "Slow down, then up."),
        new("Glute bridge", "3 × 12", new ExerciseLoad(null, "bodyweight"), 45, false, 120, "Squeeze at the top."),
        new("Dead bug", "2 × 8", new ExerciseLoad(null, "each side"), 30, false, 118, "Low back stays flat."),
        new("Stretch", "5 min", new ExerciseLoad(null, "hips, chest, calves"), null, false, 100, null),
    ];

    public static WorkoutDocumentModel Workout(Soreness? soreness = null, IReadOnlyList<ExercisePageModel>? exercises = null) =>
        new("Strength", Tuesday, 40, "Kettlebell, band, bench, mat", exercises ?? Seven(), soreness);

    public static Soreness Sore(bool doctor = false, int days = 3) => new("left knee", "Monday", days, doctor);

    public static WorkoutDocumentModel InjuryDay(bool doctor = false)
    {
        var pages = new List<ExercisePageModel>
        {
            Bridge("In place of reverse lunges: no load through the knee."),
            new("Band row", "3 × 12", new ExerciseLoad(null, "medium band"), 45, false, 150, "Smooth beats fast.", "In place of lunge holds: seated, both feet down."),
            new("Dead bug", "2 × 8", new ExerciseLoad(null, "each side"), 30, false, 118, null),
        };
        return new("Strength", new DateTime(2026, 10, 22, 17, 30, 0), 30, "Band, mat", pages, Sore(doctor, doctor ? 10 : 3));
    }

    public static IReadOnlyList<AssessmentTest> Tests() =>
    [
        new("Push-ups in a row", "As many as you can with good form, then stop.", "Knees down is fine; say so in the notes.", "reps", null, true, "Just a starting line. Whatever it is, it is yours."),
        new("Bodyweight squats in a minute", "Sit back and stand tall, as many as you can in one minute.", null, "reps", null, false, null),
        new("Plank hold", "Hold a straight line on your forearms for as long as you can.", "Stop when your hips drop.", "seconds", null, false, "One steady breath at a time."),
        new("Toe reach", "Stand tall, knees straight, and fold forward slowly.", "No bouncing. Circle where your fingertips reach.", null, ["knees", "shins", "ankles", "floor"], false, null),
    ];

    public static AssessmentDocumentModel Assessment() =>
        new(AssessmentMonday, 1, 3, Tests(), "Wednesday: a timed one-mile walk. Friday: the same four, again.");

    /// <summary>Every page of every sample document, for the rules that must hold on all of them.</summary>
    public static IReadOnlyList<InkPageLayout> AllPages() =>
        [.. WorkoutDocument.Build(Workout()), .. WorkoutDocument.Build(InjuryDay(true)), .. AssessmentDocument.Build(Assessment())];
}
