using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>Why a run planned nothing. A closed list: the head says it plainly on the Waiting page, and the trail prints the word.</summary>
public enum NotPlannedBecause
{
    /// <summary>A record the plan learns from (a ledger) could not be read, and an unreadable record is not an empty one.</summary>
    RecordsUnreadable,

    /// <summary>No adult's calendars could be read in full, so nobody's free time is known.</summary>
    CalendarsUnreadable,

    /// <summary>The rules name no adult to plan for.</summary>
    NoOneToPlan,
}

/// <summary>What became of one document in a run.</summary>
public enum DocumentVerdict
{
    /// <summary>Sent to the tablet for the first time.</summary>
    Delivered,

    /// <summary>The page on the tablet was replaced, before its slot, under the same title.</summary>
    Replaced,

    /// <summary>The session was called off before its slot; its page was taken off the tablet.</summary>
    Withdrawn,

    /// <summary>Already on the tablet and saying the same thing.</summary>
    Unchanged,

    /// <summary>It would change (or be sent) but the slot has begun (or is over): nothing was sent and nothing was changed.</summary>
    TooLate,

    /// <summary>The session is not on the calendar (the owner deleted it, or it could not be placed or written), so no page was made for it.</summary>
    NotOnCalendar,

    /// <summary>The page could not be drawn. The reason is kept with its stack in the outcome.</summary>
    NotBuilt,

    /// <summary>The tablet did not take it (or the old page could not be taken off first). A notice is on the board where the tablet refused; the next run tries again.</summary>
    NotDelivered,
}

/// <summary>One document's outcome. <see cref="Failure"/> is kept with its stack for the verdicts that failed; null otherwise.</summary>
public sealed record DocumentOutcome(string Name, string Person, DateOnly Day, DocumentVerdict Verdict, Exception? Failure = null);

/// <summary>
/// What a run that planned the week did. <see cref="Plan"/> is the week as the paper and the Workouts tab read it (every
/// adaptation applied); <see cref="PlacedPlan"/> is what was handed to the calendar, which is the week as planned unless the
/// request asked for the adapted one (see <c>WorkoutWeekRequest.PlaceAdaptedWeek</c>). <see cref="Request"/> is the planner's
/// request as it stood for the adapters (the real clock in it), which is what a change made later on the Workouts tab is held to.
/// </summary>
public sealed record WorkoutWeekReport(
    WorkoutPlanRequest Request,
    HouseholdWorkoutPlan Plan,
    HouseholdWorkoutPlan PlacedPlan,
    IReadOnlyDictionary<string, PhaseDecision> Phases,
    PlanOrigin? Origin,
    IReadOnlyList<RecoverySwapNote> RecoverySwaps,
    IReadOnlyList<SessionAdjustment> SorenessAdjustments,
    IReadOnlyList<ConditionChange> ConditionChanges,
    bool ConditionsWereRead,
    IReadOnlyList<TravelChange> TravelChanges,
    IReadOnlyList<DayNote> DayNotes,
    IReadOnlyList<string> PeopleWithUnreadableCalendars,
    PlacementRun Placement,
    IReadOnlyList<DocumentOutcome> Documents);

/// <summary>The typed result of one run: it planned the week, or it planned nothing and says why.</summary>
public abstract record WorkoutWeekOutcome
{
    private WorkoutWeekOutcome()
    {
    }

    /// <summary>Nothing was planned, placed or sent. <see cref="Unreadable"/> names the calendars at fault (empty for the other reasons).</summary>
    public sealed record NotPlanned(NotPlannedBecause Because, IReadOnlyList<UnreadableCalendar> Unreadable, string Detail) : WorkoutWeekOutcome;

    public sealed record Planned(WorkoutWeekReport Report) : WorkoutWeekOutcome;
}
