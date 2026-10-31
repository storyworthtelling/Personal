using Constitution.Domain.Equipment;

namespace Constitution.Domain.Workouts;

/// <summary>One exercise stood in for, in the plain words p.6 prints under it ("In place of reverse lunges").</summary>
/// <param name="Replacement">What stands in, or null when nothing could and the exercise was left out.</param>
public sealed record ExerciseSubstitution(string Original, string? Replacement, string Text);

/// <summary>What soreness did to one person's session on one day.</summary>
/// <param name="EasyTempo">The tempo drops to Easy on the page (p.6).</param>
/// <param name="Removed">True when nothing could be done around the area and the session became a rest day.</param>
/// <param name="Reason">One plain sentence for the Workouts tab.</param>
public sealed record SessionAdjustment(
    string Person,
    DateOnly Day,
    IReadOnlyList<ExerciseSubstitution> Substitutions,
    bool EasyTempo,
    bool Removed,
    string Reason);

public sealed record SorenessAdjusted(HouseholdWorkoutPlan Plan, IReadOnlyList<SessionAdjustment> Adjustments);

/// <summary>
/// Works the week around what is sore (C-5.2.3). Only injury changes the plan's shape: with nothing open the plan comes
/// back as the same object. For a session on or after the day a note was made:
/// <list type="bullet">
/// <item>a strength or mobility exercise that loads the area is stood in for by the first gentle exercise that loads none
/// of the sore areas, needs only ticked equipment (D72) and is not already in the session, else left out; a session left
/// with nothing is a rest day;</item>
/// <item>a walk (outdoors, treadmill, the Sunday walk) that loads the area is a rest day, and one that does not is kept;</item>
/// <item>a place the plan cannot place rests every strength and cardio session and eases the rest;</item>
/// <item>every session kept runs at Easy tempo.</item>
/// </list>
/// A session shared by several is worked around for all of them (it is one object, and the soreness of any of them is
/// every one's business). Nothing here reads a Body value.
/// </summary>
public static class SorenessPlan
{
    private static readonly SoreArea[] UpperAreas = [SoreArea.Shoulder, SoreArea.Arm, SoreArea.UpperBack, SoreArea.Neck];

    private sealed record Candidate(string Name, string? EquipmentId);

    // Gentle, seated or standing, nothing that needs more than the mat; in the order they are tried.
    private static readonly Candidate[] LowerPool =
    [
        new("Glute bridge on the mat", EquipmentCatalogue.MatId),
        new("Seated march in a chair", null),
        new("Standing calf raise holding a counter", null),
        new("Seated knee extension", null),
        new("Standing hip circles", null),
    ];

    private static readonly Candidate[] UpperPool =
    [
        new("Wrist circles and fist squeezes", null),
        new("Seated chin tuck", null),
        new("Seated arm raise to the side", null),
        new("Seated shoulder blade squeeze", null),
        new("Seated side reach", null),
    ];

    public static SorenessAdjusted Apply(HouseholdWorkoutPlan plan, SorenessLedger ledger, EquipmentSet equipment)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(equipment);
        if (ledger.Notes.Count == 0)
        {
            return new SorenessAdjusted(plan, []);
        }

        var adjustments = new List<SessionAdjustment>();
        var rewritten = RecoveryPlanEdit.Rewrite(plan, "Resting while something is sore.", session => Around(session, ledger, equipment, adjustments));
        return new SorenessAdjusted(rewritten, adjustments.OrderBy(item => item.Day).ThenBy(item => item.Person, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static PlannedSession? Around(PlannedSession planned, SorenessLedger ledger, EquipmentSet equipment, List<SessionAdjustment> adjustments)
    {
        var sore = planned.Participants
            .Select(person => (Person: person, Notes: ledger.OpenFor(person).Where(note => note.Noted <= planned.Day).ToList()))
            .Where(item => item.Notes.Count > 0)
            .ToList();
        if (sore.Count == 0)
        {
            return planned;
        }

        var areas = sore.SelectMany(item => item.Notes).Select(note => note.Area).Where(area => area != SoreArea.Unknown).ToHashSet();
        var hasUnplaced = sore.SelectMany(item => item.Notes).Any(note => note.Area == SoreArea.Unknown);
        var session = planned.Session;

        var substitutions = new List<ExerciseSubstitution>();
        Session? kept = null;
        switch (session.Kind)
        {
            case SessionKind.Strength or SessionKind.Mobility:
                kept = hasUnplaced && session.Kind == SessionKind.Strength ? null : WorkAround(session, areas, equipment, substitutions);
                break;
            case SessionKind.OutdoorCardio or SessionKind.Treadmill or SessionKind.GentleWalk:
                var hardUnplaced = hasUnplaced && SessionKinds.IsHard(session.Kind);
                kept = hardUnplaced || session.Exercises.Any(exercise => ExerciseLoads.Of(exercise).Overlaps(areas)) ? null : session;
                break;
        }

        foreach (var (person, notes) in sore)
        {
            adjustments.Add(new SessionAdjustment(person, planned.Day, substitutions, true, kept is null, ReasonFor(notes, kept is null)));
        }

        return kept is null ? null : ReferenceEquals(kept, session) ? planned : planned with { Session = kept };
    }

    private static Session? WorkAround(Session session, HashSet<SoreArea> areas, EquipmentSet equipment, List<ExerciseSubstitution> substitutions)
    {
        var used = session.Exercises.Select(exercise => exercise.Name).ToHashSet(StringComparer.Ordinal);
        var result = new List<Exercise>();
        foreach (var exercise in session.Exercises)
        {
            if (!ExerciseLoads.Of(exercise).Overlaps(areas))
            {
                result.Add(exercise);
                continue;
            }

            var stand = Candidates(exercise)
                .FirstOrDefault(candidate => !used.Contains(candidate.Name)
                    && (candidate.EquipmentId is null || equipment.Has(candidate.EquipmentId))
                    && !ExerciseLoads.Of(new Exercise(candidate.Name, candidate.EquipmentId)).Overlaps(areas));
            if (stand is null)
            {
                substitutions.Add(new ExerciseSubstitution(exercise.Name, null, $"Left out: {LowerFirst(exercise.Name)}"));
                continue;
            }

            used.Add(stand.Name);
            result.Add(new Exercise(stand.Name, stand.EquipmentId));
            substitutions.Add(new ExerciseSubstitution(exercise.Name, stand.Name, $"In place of {LowerFirst(exercise.Name)}"));
        }

        if (result.Count == 0)
        {
            return null;
        }

        return substitutions.Count == 0 ? session : new Session(session.Kind, session.Duration, session.IsSweaty, result);
    }

    // The same sort of movement first (upper body for an upper-body exercise, else lower), then the other pool.
    private static IEnumerable<Candidate> Candidates(Exercise exercise)
    {
        var upper = ExerciseLoads.IsKnown(exercise.Name) && ExerciseLoads.Of(exercise).Overlaps(UpperAreas);
        return upper ? UpperPool.Concat(LowerPool) : LowerPool.Concat(UpperPool);
    }

    private static string ReasonFor(List<SorenessNote> notes, bool resting)
    {
        var places = notes.Where(note => note.Area != SoreArea.Unknown).Select(note => note.Place).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (places.Count == 0)
        {
            return resting ? "Resting while something is sore." : "Taking it easy while something is sore.";
        }

        var named = string.Join(" and ", places.Select(place => $"your {place}"));
        return resting ? $"Resting {named}." : $"Working around {named}.";
    }

    private static string LowerFirst(string text) => text.Length == 0 ? text : string.Concat(char.ToLowerInvariant(text[0]).ToString(), text.AsSpan(1));
}
