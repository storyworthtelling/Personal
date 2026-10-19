using System.Text.RegularExpressions;

namespace Constitution.Domain.Workouts;

/// <summary>
/// Where it is sore, as far as the plan needs to know (C-5.2.3). <see cref="Unknown"/> is a place the note names that
/// none of the others covers (or several of them at once): the plan cannot work around what it cannot place, so it
/// rests the hard days instead.
/// </summary>
public enum SoreArea
{
    Unknown,

    Neck,

    Shoulder,

    Arm,

    UpperBack,

    LowerBack,

    Hip,

    Thigh,

    Knee,

    Foot,
}

public static class SoreAreas
{
    // Stems matched at the start of a word of the note ("kneecap" is a knee); short stems must be the whole word or its plural.
    private static readonly (string Stem, bool Whole, SoreArea Area)[] Words =
    [
        ("knee", false, SoreArea.Knee),
        ("shoulder", false, SoreArea.Shoulder),
        ("rotator", false, SoreArea.Shoulder),
        ("neck", false, SoreArea.Neck),
        ("back", false, SoreArea.LowerBack),
        ("lumbar", false, SoreArea.LowerBack),
        ("sciatic", false, SoreArea.LowerBack),
        ("hip", true, SoreArea.Hip),
        ("glute", false, SoreArea.Hip),
        ("groin", false, SoreArea.Hip),
        ("thigh", false, SoreArea.Thigh),
        ("quad", false, SoreArea.Thigh),
        ("hamstring", false, SoreArea.Thigh),
        ("leg", true, SoreArea.Thigh),
        ("ankle", false, SoreArea.Foot),
        ("foot", false, SoreArea.Foot),
        ("feet", false, SoreArea.Foot),
        ("heel", false, SoreArea.Foot),
        ("calf", false, SoreArea.Foot),
        ("calves", false, SoreArea.Foot),
        ("shin", false, SoreArea.Foot),
        ("achilles", false, SoreArea.Foot),
        ("toe", true, SoreArea.Foot),
        ("elbow", false, SoreArea.Arm),
        ("wrist", false, SoreArea.Arm),
        ("forearm", false, SoreArea.Arm),
        ("hand", true, SoreArea.Arm),
        ("arm", true, SoreArea.Arm),
        ("bicep", false, SoreArea.Arm),
        ("tricep", false, SoreArea.Arm),
    ];

    private static readonly Regex UpperBackPhrase = new(@"upper\s+back|shoulder\s+blades?|thoracic", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    private static readonly Regex WordSplit = new(@"[^a-z]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The area a margin note's place words name ("my left knee" is <see cref="SoreArea.Knee"/>). Nothing recognised, or
    /// more than one area named at once, is <see cref="SoreArea.Unknown"/>: guessing which of two to work around is how a
    /// session ends up loading the one that hurts.
    /// </summary>
    public static SoreArea Read(string? place)
    {
        if (string.IsNullOrWhiteSpace(place))
        {
            return SoreArea.Unknown;
        }

        var found = new HashSet<SoreArea>();
        var text = place.ToLowerInvariant();
        if (UpperBackPhrase.IsMatch(text))
        {
            found.Add(SoreArea.UpperBack);
            text = UpperBackPhrase.Replace(text, " ");
        }

        foreach (var word in WordSplit.Split(text).Where(item => item.Length > 0))
        {
            foreach (var (stem, whole, area) in Words)
            {
                if (whole ? word == stem || word == stem + "s" : word.StartsWith(stem, StringComparison.Ordinal))
                {
                    found.Add(area);
                    break;
                }
            }
        }

        return found.Count == 1 ? found.Single() : SoreArea.Unknown;
    }

    internal static string StoredName(SoreArea area) => area switch
    {
        SoreArea.Unknown => "unknown",
        SoreArea.Neck => "neck",
        SoreArea.Shoulder => "shoulder",
        SoreArea.Arm => "arm",
        SoreArea.UpperBack => "upper-back",
        SoreArea.LowerBack => "lower-back",
        SoreArea.Hip => "hip",
        SoreArea.Thigh => "thigh",
        SoreArea.Knee => "knee",
        SoreArea.Foot => "foot",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "That is not an area the plan knows."),
    };

    internal static SoreArea? FromStoredName(string? name) => name switch
    {
        "unknown" => SoreArea.Unknown,
        "neck" => SoreArea.Neck,
        "shoulder" => SoreArea.Shoulder,
        "arm" => SoreArea.Arm,
        "upper-back" => SoreArea.UpperBack,
        "lower-back" => SoreArea.LowerBack,
        "hip" => SoreArea.Hip,
        "thigh" => SoreArea.Thigh,
        "knee" => SoreArea.Knee,
        "foot" => SoreArea.Foot,
        _ => null,
    };
}

/// <summary>
/// Which areas of the body an exercise loads (C-5.2.3). Public so the tests, and the page's substitution line, read the
/// same table. A name the table does not hold is <i>treated as loading everything</i>: an exercise nobody has described
/// cannot be promised safe, so it is stood in for rather than trusted.
/// </summary>
public static class ExerciseLoads
{
    private static readonly SoreArea[] Legs = [SoreArea.Hip, SoreArea.Thigh, SoreArea.Knee, SoreArea.Foot];
    private static readonly SoreArea[] AllAreas = Enum.GetValues<SoreArea>().Where(area => area != SoreArea.Unknown).ToArray();

    private static readonly Dictionary<string, SoreArea[]> Table = new(StringComparer.Ordinal)
    {
        // the planner's strength patterns
        ["Squat holding a weight at your chest"] = [SoreArea.Knee, SoreArea.Hip, SoreArea.Thigh, SoreArea.LowerBack],
        ["Step-ups onto a bench"] = [SoreArea.Knee, SoreArea.Hip, SoreArea.Thigh, SoreArea.Foot],
        ["Sit to stand from a chair"] = [SoreArea.Knee, SoreArea.Hip, SoreArea.Thigh],
        ["Push-ups with your hands on a bench"] = [SoreArea.Shoulder, SoreArea.Arm],
        ["Band chest press"] = [SoreArea.Shoulder, SoreArea.Arm],
        ["Push-ups against a wall"] = [SoreArea.Shoulder, SoreArea.Arm],
        ["One-arm weight row"] = [SoreArea.Shoulder, SoreArea.Arm, SoreArea.UpperBack, SoreArea.LowerBack],
        ["Band row"] = [SoreArea.Shoulder, SoreArea.Arm, SoreArea.UpperBack],
        ["Hang from the bar"] = [SoreArea.Shoulder, SoreArea.Arm, SoreArea.UpperBack],
        ["Wall angels"] = [SoreArea.Shoulder, SoreArea.UpperBack, SoreArea.Neck],
        ["Hip hinge holding a weight"] = [SoreArea.Hip, SoreArea.LowerBack, SoreArea.Thigh],
        ["Glute bridge on the mat"] = [SoreArea.Hip, SoreArea.LowerBack],
        ["Hip hinge with hands on hips"] = [SoreArea.Hip, SoreArea.LowerBack],
        ["Dead bug on the mat"] = [SoreArea.Hip],
        ["Bird dog on the mat"] = [SoreArea.Shoulder, SoreArea.LowerBack],
        ["Standing knee lift"] = [SoreArea.Hip, SoreArea.Knee],

        // mobility
        ["Cat and cow"] = [SoreArea.UpperBack, SoreArea.LowerBack, SoreArea.Neck],
        ["Child's pose"] = [SoreArea.Knee, SoreArea.Hip, SoreArea.LowerBack, SoreArea.Shoulder],
        ["Hip flexor stretch"] = [SoreArea.Hip, SoreArea.Knee],
        ["Seated hamstring stretch"] = [SoreArea.Thigh, SoreArea.LowerBack],
        ["Lying spine twist"] = [SoreArea.LowerBack, SoreArea.UpperBack],
        ["Neck and shoulder rolls"] = [SoreArea.Neck, SoreArea.Shoulder],
        ["Standing hip circles"] = [SoreArea.Hip],
        ["Standing hamstring stretch"] = [SoreArea.Thigh, SoreArea.LowerBack],
        ["Doorway chest stretch"] = [SoreArea.Shoulder],
        ["Calf stretch against a wall"] = [SoreArea.Foot],
        ["Seated toe reach"] = [SoreArea.LowerBack, SoreArea.Thigh],

        // walking, outdoors and on the treadmill
        ["Warm-up walk"] = Legs,
        ["Brisk walk"] = Legs,
        ["Cool-down walk"] = Legs,
        ["Easy walk"] = Legs,
        ["Easy family walk"] = Legs,
        ["Warm-up walk on the treadmill"] = Legs,
        ["Steady brisk walk on the treadmill"] = Legs,
        ["Cool-down walk on the treadmill"] = Legs,
        ["Walk or jog for 10 minutes and note the distance"] = Legs,

        // what soreness stands in with (SorenessPlan)
        ["Seated march in a chair"] = [SoreArea.Hip, SoreArea.Thigh],
        ["Standing calf raise holding a counter"] = [SoreArea.Foot],
        ["Seated knee extension"] = [SoreArea.Knee, SoreArea.Thigh],
        ["Wrist circles and fist squeezes"] = [SoreArea.Arm],
        ["Seated chin tuck"] = [SoreArea.Neck],
        ["Seated arm raise to the side"] = [SoreArea.Shoulder],
        ["Seated shoulder blade squeeze"] = [SoreArea.UpperBack, SoreArea.Shoulder],
        ["Seated side reach"] = [SoreArea.UpperBack, SoreArea.LowerBack],
    };

    /// <summary>The areas the exercise loads; every area when the table has no row for its name.</summary>
    public static IReadOnlySet<SoreArea> Of(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        return Lookup(exercise.Name) is { } areas ? areas.ToHashSet() : AllAreas.ToHashSet();
    }

    public static bool IsKnown(string exerciseName)
    {
        ArgumentNullException.ThrowIfNull(exerciseName);
        return Lookup(exerciseName) is not null;
    }

    // The assessment prints its sit to stand and push-ups with the test's instruction after a comma.
    private static SoreArea[]? Lookup(string name)
    {
        if (Table.TryGetValue(name, out var exact))
        {
            return exact;
        }

        var comma = name.IndexOf(',', StringComparison.Ordinal);
        return comma > 0 && Table.TryGetValue(name[..comma], out var prefixed) ? prefixed : null;
    }
}
