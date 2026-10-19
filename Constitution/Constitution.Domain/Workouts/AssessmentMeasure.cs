namespace Constitution.Domain.Workouts;

/// <summary>The three tests of the assessment week (p.6). Each is one session; the walk is the kind of cardio the week has.</summary>
public enum AssessmentTest
{
    /// <summary>Counted bodyweight work: how many in thirty seconds.</summary>
    StrengthBaselines,

    /// <summary>A walk or jog timed at ten minutes: how far.</summary>
    TimedWalk,

    /// <summary>A toe reach, answered by circling one of four words.</summary>
    MobilityCheck,
}

/// <summary>
/// One thing an assessment page reads back. <see cref="Id"/> is the target id the page is painted with and the ink
/// is resolved against, so it is a stored identifier: literal, kebab-case, pinned by a test. There is no rating on a
/// test page (D27); the number, or the circled word, is the answer.
/// </summary>
public abstract record AssessmentMeasure(string Id, string Name);

/// <summary>A number in its one open box (D15), the unit printed outside it.</summary>
public sealed record NumberMeasure(string Id, string Name, string Unit) : AssessmentMeasure(Id, Name);

/// <summary>A circle-a-word answer: exactly one of <paramref name="Words"/>.</summary>
public sealed record CircleMeasure(string Id, string Name, IReadOnlyList<string> Words) : AssessmentMeasure(Id, Name);

/// <summary>
/// What the assessment asks. Every test is bodyweight and needs no equipment, so an assessment week never prescribes
/// an unticked item (D72) whatever the checklist says; the only equipment it can touch is the treadmill, and only
/// when the planner has already chosen the treadmill for the walk. Units are US (D22).
/// </summary>
public static class AssessmentCatalogue
{
    public const string SitToStandId = "assess-sit-to-stand";
    public const string WallPushUpsId = "assess-wall-push-ups";
    public const string TimedWalkId = "assess-timed-walk";
    public const string ToeReachId = "assess-toe-reach";

    private static readonly AssessmentMeasure[] Strength =
    [
        new NumberMeasure(SitToStandId, "Sit to stand in 30 seconds", "stands"),
        new NumberMeasure(WallPushUpsId, "Wall push-ups in 30 seconds", "push-ups"),
    ];

    private static readonly AssessmentMeasure[] Walk =
    [
        new NumberMeasure(TimedWalkId, "How far in 10 minutes", "miles"),
    ];

    // Four words in order of reach, so a lane's position says the same thing the word does.
    private static readonly AssessmentMeasure[] Mobility =
    [
        new CircleMeasure(ToeReachId, "Toe reach", ["Knees", "Shins", "Ankles", "Toes"]),
    ];

    public static IReadOnlyList<AssessmentMeasure> MeasuresOf(AssessmentTest test) => test switch
    {
        AssessmentTest.StrengthBaselines => Strength,
        AssessmentTest.TimedWalk => Walk,
        AssessmentTest.MobilityCheck => Mobility,
        _ => throw new ArgumentOutOfRangeException(nameof(test), test, "There is no such assessment test."),
    };

    /// <summary>The test a planned session of this kind is. A gentle walk is not one: the assessment week has none.</summary>
    public static AssessmentTest TestFor(SessionKind kind) => kind switch
    {
        SessionKind.Strength => AssessmentTest.StrengthBaselines,
        SessionKind.OutdoorCardio or SessionKind.Treadmill => AssessmentTest.TimedWalk,
        SessionKind.Mobility => AssessmentTest.MobilityCheck,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A session of this kind is not an assessment test."),
    };
}
