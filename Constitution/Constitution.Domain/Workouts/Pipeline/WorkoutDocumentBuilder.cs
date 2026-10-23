using System.Globalization;
using Codex.Domain.Music;
using Constitution.Domain.Documents;
using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// Turns one planned session into the model <see cref="WorkoutDocument"/> draws (book p.5, p.6). The plan says which
/// exercises, how long and when; everything else a page prints is filled in here from the ladder and from the kind of
/// session, and every figure is a named choice rather than a number in a page.
///
/// <para><b>Doses.</b> A strength session is dosed from the progression ladder (<see cref="ProgressionPlan.Prescribe"/>:
/// "2 × 8", with pounds for a dumbbell or kettlebell lift only, D22). Everything else is dosed in minutes that add up to
/// the session: a walk or mobility sequence is split evenly (the last exercise takes the remainder); a three-part cardio
/// session (warm-up, the walk itself, cool-down) gives the warm-up and cool-down <see cref="WarmUpMinutes"/> each when the
/// session is long enough to carry them, else splits evenly.</para>
///
/// <para><b>Rest</b> is printed for strength only (<see cref="StrengthRestSeconds"/>); nothing else has sets to rest
/// between. <b>The Score setting</b> is one number per exercise (D25): the middle of the Steady band for strength and the
/// walk itself, the middle of the Easy band for warm-ups, cool-downs, mobility and gentle walks. The page clamps it to Easy
/// on an injury day.</para>
///
/// <para>No figure is asked for (<c>ShowFigure</c> is false): the illustration policy (C-2.3.3) does not exist yet, and a
/// page that keeps room for a figure nobody draws would leave a gap.</para>
///
/// <para>Every one of the numbers named here (the rest, the warm-up, the bands each kind of effort takes) is the agent's
/// reading of a beginner's session, not the owner's or a coach's.</para>
/// </summary>
public static class WorkoutDocumentBuilder
{
    /// <summary>Rest between sets on a strength page.</summary>
    public const int StrengthRestSeconds = 60;

    /// <summary>The warm-up and the cool-down of a three-part cardio session, each, when the session is long enough.</summary>
    public const int WarmUpMinutes = 5;

    private const int ThreeParts = 3;
    private const string NoEquipment = "No equipment needed";

    /// <param name="planned">The session as it stands now (after every adaptation).</param>
    /// <param name="start">The slot as it is on the calendar, which is the time the page names.</param>
    /// <param name="zone">The household's zone: the page names the hour on the owner's own clock.</param>
    /// <param name="strengthDoses">One prescription per exercise, required for a strength session and ignored for any other.</param>
    /// <param name="encouragement">One line (or null to hide the seat) per exercise, in order; null for none.</param>
    /// <param name="soreness">The open soreness note for the person today, if any.</param>
    /// <param name="substitutions">What stood in for what, from the soreness plan.</param>
    public static WorkoutDocumentModel Build(
        PlannedSession planned,
        DateTimeOffset start,
        TimeZoneInfo zone,
        IReadOnlyList<ExercisePrescription>? strengthDoses = null,
        IReadOnlyList<string?>? encouragement = null,
        Soreness? soreness = null,
        IReadOnlyList<ExerciseSubstitution>? substitutions = null)
    {
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(zone);

        var session = planned.Session;
        var count = session.Exercises.Count;
        if (session.Kind == SessionKind.Strength && (strengthDoses is null || strengthDoses.Count != count))
        {
            throw new ArgumentException(
                $"A strength session of {count} exercises needs {count} prescriptions; it was given {strengthDoses?.Count ?? 0}.", nameof(strengthDoses));
        }

        if (encouragement is not null && encouragement.Count != count)
        {
            throw new ArgumentException($"{count} exercises need {count} encouragement entries (null to hide one); {encouragement.Count} were given.", nameof(encouragement));
        }

        var minutes = Minutes(session);
        var pages = new List<ExercisePageModel>();
        for (var index = 0; index < count; index++)
        {
            var exercise = session.Exercises[index];
            var strength = session.Kind == SessionKind.Strength;
            var dose = strength ? strengthDoses![index].Dose : null;

            pages.Add(new ExercisePageModel(
                exercise.Name,
                dose is null ? $"{minutes[index].ToString(CultureInfo.InvariantCulture)} min" : dose.Text,
                new ExerciseLoad(dose is { IsWeighted: true } ? dose.Pounds : null, LoadWords(exercise)),
                strength ? StrengthRestSeconds : null,
                false,
                ScoreFor(session.Kind, index, count),
                encouragement?[index],
                substitutions?.FirstOrDefault(item => item.Replacement == exercise.Name)?.Text));
        }

        return new WorkoutDocumentModel(
            Title(session.Kind, planned.Together),
            TimeZoneInfo.ConvertTime(start, zone).DateTime,
            (int)session.Duration.TotalMinutes,
            EquipmentLine(session),
            pages,
            soreness);
    }

    /// <summary>The same model with every voiced line hidden: what the page says apart from the seat, which is what a change is judged on.</summary>
    public static WorkoutDocumentModel WithoutVoice(WorkoutDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model with { Exercises = model.Exercises.Select(item => item with { Encouragement = null }).ToList() };
    }

    private static string Title(SessionKind kind, Together together) => kind switch
    {
        SessionKind.GentleWalk => together == Together.FamilyWalk ? "Family walk" : "Easy walk",
        _ => (kind switch
        {
            SessionKind.Strength => "Strength",
            SessionKind.OutdoorCardio => "Outdoor cardio",
            SessionKind.Treadmill => "Treadmill",
            SessionKind.Mobility => "Mobility",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "There is no title for this kind of session."),
        }) + (together == Together.AfterBedtime ? " together" : string.Empty),
    };

    private static string EquipmentLine(Session session)
    {
        if (session.EquipmentNeeded.Count == 0)
        {
            return NoEquipment;
        }

        var names = session.EquipmentNeeded
            .Select(id => EquipmentCatalogue.All.First(item => item.Id == id).Name.ToLowerInvariant());
        return "Needs " + string.Join(", ", names);
    }

    private static string LoadWords(Exercise exercise) => exercise.EquipmentId switch
    {
        null => "bodyweight",
        EquipmentCatalogue.DumbbellsId => "dumbbell or kettlebell",
        EquipmentCatalogue.BandsId => "resistance band",
        EquipmentCatalogue.BenchId => "bench",
        EquipmentCatalogue.PullUpBarId => "pull-up bar",
        EquipmentCatalogue.MatId => "mat",
        EquipmentCatalogue.TreadmillId => "treadmill",
        _ => throw new ArgumentOutOfRangeException(nameof(exercise), exercise.EquipmentId, "There are no words for this piece of equipment."),
    };

    private static int ScoreFor(SessionKind kind, int index, int count)
    {
        var easy = TempoBands.Default(TempoBand.Easy);
        var steady = TempoBands.Default(TempoBand.Steady);
        return kind switch
        {
            SessionKind.Strength => steady,
            SessionKind.OutdoorCardio or SessionKind.Treadmill => count == ThreeParts && index != 1 ? easy : steady,
            _ => easy,
        };
    }

    // Whole minutes for each exercise of a session that is dosed by time, adding up to the session.
    private static int[] Minutes(Session session)
    {
        var total = (int)session.Duration.TotalMinutes;
        var count = session.Exercises.Count;
        var cardio = session.Kind is SessionKind.OutdoorCardio or SessionKind.Treadmill;
        if (cardio && count == ThreeParts && total >= 4 * WarmUpMinutes)
        {
            return [WarmUpMinutes, total - (2 * WarmUpMinutes), WarmUpMinutes];
        }

        var each = total / count;
        var minutes = Enumerable.Repeat(each, count).ToArray();
        minutes[^1] += total - (each * count);
        return minutes;
    }
}
