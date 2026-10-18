namespace Constitution.Domain.Documents;

/// <summary>
/// What an exercise is lifted with. Weights are pounds (D22) and are stored as pounds, so no page can
/// print a kilogram by accident; an exercise without a weight (a band, the body) only has words.
/// </summary>
public sealed record ExerciseLoad(int? Pounds, string Description);

/// <summary>
/// An open soreness note (D28): the place, when it began, how long it has lasted, and whether the
/// calm doctor suggestion is due. <paramref name="DoctorNoteDue"/> is the planner's decision; the
/// document prints the note on one page only, in plain neutral type.
/// </summary>
public sealed record Soreness(string Place, string Since, int DaysSore, bool DoctorNoteDue);

/// <summary>
/// One exercise page's data (p.5). The planner's session says what the exercises are; the dose, the
/// load, the rest and the Score setting come from the plan around it, so this is a plain record the
/// plan fills in and the document only draws.
/// </summary>
/// <param name="Dose">The dose as printed large: "3 × 10", "5 min".</param>
/// <param name="ShowFigure">True while the exercise is new (C-2.3.3's policy decides); room is kept for the figure.</param>
/// <param name="ScoreBpm">The one recommended setting, printed with its band name (D25).</param>
/// <param name="Encouragement">The one voiced line, or null when the seat is hidden.</param>
/// <param name="Substitution">On an injury day, what stands in for the usual exercise, in plain words.</param>
public sealed record ExercisePageModel(
    string Name,
    string Dose,
    ExerciseLoad Load,
    int? RestSeconds,
    bool ShowFigure,
    int ScoreBpm,
    string? Encouragement,
    string? Substitution = null);

/// <summary>A workout: a cover and one page per exercise, with soreness carried onto every page when open.</summary>
public sealed record WorkoutDocumentModel(
    string Title,
    DateTime Start,
    int TotalMinutes,
    string EquipmentLine,
    IReadOnlyList<ExercisePageModel> Exercises,
    Soreness? Soreness = null);

/// <summary>
/// One assessment test (p.6): a number in one box (<paramref name="Unit"/> set) or a circled word
/// (<paramref name="Words"/> set), never both and never a rating (D27).
/// </summary>
public sealed record AssessmentTest(
    string Name,
    string Instruction,
    string? Hint,
    string? Unit,
    IReadOnlyList<string>? Words,
    bool ShowFigure,
    string? Encouragement);

/// <summary>Assessment week's session: its cover and one page per test.</summary>
public sealed record AssessmentDocumentModel(
    DateTime Start,
    int Day,
    int OfDays,
    IReadOnlyList<AssessmentTest> Tests,
    string Schedule);
