namespace Constitution.Domain.Workouts;

/// <summary>
/// What decides the phase of a person's week (C-5.1.2): when an assessment week last began, and the last day a
/// session was done. Both are dates, so the record needs no clock; the caller keeps it (the last done day comes from
/// <see cref="SkipLedger.LastDoneDay"/>) and writes <see cref="AssessmentWeekStart"/> when a week is planned as an
/// assessment. A person nobody has any record of is <see cref="Fresh"/>, which is the first install.
/// </summary>
public sealed record TrainingHistory
{
    /// <summary>The first day of the week the assessment was last planned for, or null when it never has been.</summary>
    public DateOnly? AssessmentWeekStart { get; init; }

    /// <summary>The most recent day any session was done, or null when none ever has been.</summary>
    public DateOnly? LastDoneDay { get; init; }

    public static TrainingHistory Fresh => new();

    /// <summary>The history once a week starting <paramref name="weekStart"/> has been planned as the assessment.</summary>
    public TrainingHistory WithAssessmentWeek(DateOnly weekStart) => this with { AssessmentWeekStart = weekStart };
}

/// <summary>
/// Which week this is. The assessment comes first on a first install and again after two whole weeks without a done
/// session; the week after it, the plan proper begins ("moderate sessions start in week two"). Nothing else moves a
/// person into or out of the assessment, so a hard week or a skipped one never does.
///
/// <para><b>Counting weeks off.</b> Weeks are counted in sevens back from the week being planned, the week a session
/// was done in not being off. A session done 14 days before the week starts is in the week two back, so one week
/// (the last) is empty; 15 days back is in the week three back, so two are. After an assessment with nothing done,
/// the weeks since it began are the weeks off, and a re-run starts that count again whatever was done before it.</para>
/// </summary>
public static class WorkoutPhaseRules
{
    /// <summary>Whole weeks with no done session that send a person back to the assessment.</summary>
    public const int WeeksOffThatReRun = 2;

    private const int DaysInWeek = 7;

    public static PhaseDecision Decide(TrainingHistory history, DateOnly weekStart)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (history.AssessmentWeekStart is null && history.LastDoneDay is null)
        {
            return new PhaseDecision(WorkoutPhase.Assessment, PhaseReason.FirstInstall);
        }

        if (history.AssessmentWeekStart == weekStart)
        {
            return new PhaseDecision(WorkoutPhase.Assessment, PhaseReason.AssessmentUnderway);
        }

        return WeeksOff(history, weekStart) >= WeeksOffThatReRun
            ? new PhaseDecision(WorkoutPhase.Assessment, PhaseReason.TwoWeeksOff)
            : new PhaseDecision(WorkoutPhase.Moderate, PhaseReason.Established);
    }

    private static int WeeksOff(TrainingHistory history, DateOnly weekStart)
    {
        // A session done on or after the assessment began is the latest sign of life; one before it was already
        // answered by the re-run, so the count starts at the assessment instead.
        if (history.LastDoneDay is { } done && (history.AssessmentWeekStart is not { } started || done >= started))
        {
            var days = weekStart.DayNumber - done.DayNumber;
            return days <= 0 ? 0 : CeilingWeeks(days) - 1;
        }

        var sinceStart = weekStart.DayNumber - history.AssessmentWeekStart!.Value.DayNumber;
        return sinceStart <= 0 ? 0 : CeilingWeeks(sinceStart);
    }

    private static int CeilingWeeks(int days) => (days + DaysInWeek - 1) / DaysInWeek;
}
