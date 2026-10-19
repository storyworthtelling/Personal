using Constitution.Domain.Voice;

namespace Constitution.Domain.Workouts;

/// <summary>One exercise as prescribed for a person today: the dose, and the news for the encouragement seat if the load went up.</summary>
public sealed record ExercisePrescription(Exercise Exercise, ExerciseDose Dose, ProgressionFacts? News);

/// <summary>
/// Puts what the pages have taught onto the week (C-5.1.3). It adds to the planner's output and never reaches into the
/// planner: <see cref="Prescribe"/> says what each exercise asks of the person (and hands the encouragement seat its
/// news), and <see cref="ApplyCaps"/> shortens a strength session after one ran long.
/// </summary>
public static class ProgressionPlan
{
    /// <summary>
    /// The dose of every exercise in the session, in its order, for one person. <see cref="ExercisePrescription.News"/> is
    /// present only when the last settled session moved the exercise up, and is already in the shape
    /// <see cref="SeatFacts.WorkoutEncouragement"/> takes: pounds for a weighted lift ("15 lb today, up from 10."), all the
    /// session's reps for anything else ("16 reps today, up from 12.").
    /// </summary>
    public static IReadOnlyList<ExercisePrescription> Prescribe(ProgressionLedger ledger, string person, Session session, AssessmentBaselines? baselines = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(session);

        return session.Exercises.Select(exercise =>
        {
            var state = ledger.StateOf(person, exercise, baselines);
            var dose = DoseLadder.DoseOf(DoseLadder.IsWeighted(exercise), state.Rung, state.Pounds);
            ProgressionFacts? news = state.UpFrom is { } from
                ? new ProgressionFacts(exercise.Name, dose.IsWeighted ? dose.Pounds : dose.TotalReps, from, dose.IsWeighted ? "lb" : "reps")
                : null;
            return new ExercisePrescription(exercise, dose, news);
        }).ToList();
    }

    /// <summary>
    /// A strength session after one ran long holds only as many exercises as were done, for as long as the ledger caps the
    /// person; a session shared by several takes the lowest cap among them. The kept exercises are the session's first ones
    /// and its length shrinks with them (to a whole five minutes, never under twenty). A plan nobody is capped in comes back
    /// as the same object.
    /// </summary>
    public static HouseholdWorkoutPlan ApplyCaps(HouseholdWorkoutPlan plan, ProgressionLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ledger);

        return RecoveryPlanEdit.Rewrite(plan, "Nothing is planned.", session =>
        {
            if (session.Session.Kind != SessionKind.Strength)
            {
                return session;
            }

            var caps = session.Participants.Select(ledger.ExerciseCap).OfType<int>().ToList();
            if (caps.Count == 0 || session.Session.Exercises.Count <= caps.Min())
            {
                return session;
            }

            return session with { Session = Trimmed(session.Session, caps.Min()) };
        });
    }

    private const double LeastMinutes = 20;
    private const double Round = 5;

    private static Session Trimmed(Session session, int keep)
    {
        var scaled = session.Duration.TotalMinutes * keep / session.Exercises.Count;
        var minutes = Math.Min(session.Duration.TotalMinutes, Math.Max(LeastMinutes, Math.Floor(scaled / Round) * Round));
        return new Session(session.Kind, TimeSpan.FromMinutes(minutes), session.IsSweaty, session.Exercises.Take(keep).ToList());
    }
}
