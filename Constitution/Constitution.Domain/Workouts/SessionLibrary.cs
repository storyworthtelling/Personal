using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>
/// The exercises and the lengths. Pure data and arithmetic: which exercises a session holds for a given
/// checklist, and how long it is for a given gap. Every pattern keeps a bodyweight option that needs no
/// equipment, so a checklist with nothing ticked still gets a full session, and an exercise whose
/// equipment is unticked is never a candidate at all (D72).
/// </summary>
internal static class SessionLibrary
{
    private const double RoundTo = 5;

    // The beginner sizes, in minutes: what a session would like to be. A gap shorter than this shrinks it, and
    // the planner only ever offers a gap of thirty minutes or more, so none shrinks below thirty except mobility,
    // which is twenty to begin with.
    private const double StrengthMinutes = 40;
    private const double CardioMinutes = 35;
    private const double MobilityMinutes = 20;
    private const double WalkMinutes = 30;
    private const double FamilyWalkMinutes = 40;

    /// <summary>The longest strength session carries a fifth exercise; a shorter one drops the core work.</summary>
    private const double FiveExerciseMinutes = 40;

    // Each pattern, richest option first, bodyweight last.
    private static readonly Exercise[] Squat =
    [
        new("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId),
        new("Step-ups onto a bench", EquipmentCatalogue.BenchId),
        new("Sit to stand from a chair"),
    ];

    private static readonly Exercise[] Push =
    [
        new("Push-ups with your hands on a bench", EquipmentCatalogue.BenchId),
        new("Band chest press", EquipmentCatalogue.BandsId),
        new("Push-ups against a wall"),
    ];

    private static readonly Exercise[] Pull =
    [
        new("One-arm weight row", EquipmentCatalogue.DumbbellsId),
        new("Band row", EquipmentCatalogue.BandsId),
        new("Hang from the bar", EquipmentCatalogue.PullUpBarId),
        new("Wall angels"),
    ];

    private static readonly Exercise[] Hinge =
    [
        new("Hip hinge holding a weight", EquipmentCatalogue.DumbbellsId),
        new("Glute bridge on the mat", EquipmentCatalogue.MatId),
        new("Hip hinge with hands on hips"),
    ];

    private static readonly Exercise[] Core =
    [
        new("Dead bug on the mat", EquipmentCatalogue.MatId),
        new("Bird dog on the mat", EquipmentCatalogue.MatId),
        new("Standing knee lift"),
    ];

    private static readonly Exercise[] MobilityOnMat =
    [
        new("Cat and cow", EquipmentCatalogue.MatId),
        new("Child's pose", EquipmentCatalogue.MatId),
        new("Hip flexor stretch", EquipmentCatalogue.MatId),
        new("Seated hamstring stretch", EquipmentCatalogue.MatId),
        new("Lying spine twist", EquipmentCatalogue.MatId),
    ];

    private static readonly Exercise[] MobilityStanding =
    [
        new("Neck and shoulder rolls"),
        new("Standing hip circles"),
        new("Standing hamstring stretch"),
        new("Doorway chest stretch"),
        new("Calf stretch against a wall"),
    ];

    /// <summary>The length of a session of this kind in a gap of this length: its preferred size, rounded down to five minutes, never past the gap.</summary>
    public static TimeSpan DurationFor(SessionKind kind, Together together, TimeSpan gapLength)
    {
        var minutes = Math.Floor(Math.Min(PreferredMinutes(kind, together), gapLength.TotalMinutes) / RoundTo) * RoundTo;
        return TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// A session of this kind. <paramref name="variant"/> turns the exercises over from one session of a kind to the
    /// next in the same week (so two strength days are not the same day twice); the same variant always gives the same session.
    /// </summary>
    public static Session Build(SessionKind kind, Together together, TimeSpan duration, EquipmentSet equipment, int variant)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        return kind switch
        {
            SessionKind.Strength => new Session(kind, duration, false, StrengthExercises(duration, equipment, variant)),
            SessionKind.OutdoorCardio => new Session(kind, duration, true, [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")]),
            SessionKind.Treadmill => new Session(
                kind,
                duration,
                true,
                [
                    new("Warm-up walk on the treadmill", EquipmentCatalogue.TreadmillId),
                    new("Steady brisk walk on the treadmill", EquipmentCatalogue.TreadmillId),
                    new("Cool-down walk on the treadmill", EquipmentCatalogue.TreadmillId),
                ]),
            SessionKind.Mobility => new Session(kind, duration, false, MobilityExercises(equipment, variant)),
            SessionKind.GentleWalk => new Session(kind, duration, false, [new(together == Together.FamilyWalk ? "Easy family walk" : "Easy walk")]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The planner does not know this kind of session."),
        };
    }

    private static double PreferredMinutes(SessionKind kind, Together together) => kind switch
    {
        SessionKind.Strength => StrengthMinutes,
        SessionKind.OutdoorCardio or SessionKind.Treadmill => CardioMinutes,
        SessionKind.Mobility => MobilityMinutes,
        SessionKind.GentleWalk => together == Together.FamilyWalk ? FamilyWalkMinutes : WalkMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The planner does not know this kind of session."),
    };

    private static List<Exercise> StrengthExercises(TimeSpan duration, EquipmentSet equipment, int variant)
    {
        var patterns = duration.TotalMinutes >= FiveExerciseMinutes
            ? new[] { Squat, Push, Pull, Hinge, Core }
            : [Squat, Push, Pull, Hinge];
        return patterns.Select(pattern => Pick(pattern, equipment, variant)).ToList();
    }

    private static List<Exercise> MobilityExercises(EquipmentSet equipment, int variant)
    {
        var pool = equipment.Has(EquipmentCatalogue.MatId) ? MobilityOnMat : MobilityStanding;
        // Four of the five, starting one further round each time, so two mobility days differ.
        return Enumerable.Range(0, 4).Select(index => pool[(variant + index) % pool.Length]).ToList();
    }

    // The options this checklist allows (the last of every pattern needs nothing, so there is always one), turned over by variant.
    private static Exercise Pick(Exercise[] pattern, EquipmentSet equipment, int variant)
    {
        var allowed = pattern.Where(exercise => exercise.EquipmentId is null || equipment.Has(exercise.EquipmentId)).ToList();
        return allowed[variant % allowed.Count];
    }
}
