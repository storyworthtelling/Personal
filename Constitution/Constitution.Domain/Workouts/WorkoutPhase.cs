namespace Constitution.Domain.Workouts;

/// <summary>Which kind of week a person is in: the gentle assessment week, or the ordinary plan (C-5.1.2).</summary>
public enum WorkoutPhase
{
    Assessment,

    Moderate,
}

/// <summary>Why a week is the phase it is. A closed list, so Diagnostics can say it without a free sentence.</summary>
public enum PhaseReason
{
    FirstInstall,

    TwoWeeksOff,

    AssessmentUnderway,

    Established,
}

public sealed record PhaseDecision(WorkoutPhase Phase, PhaseReason Reason);
