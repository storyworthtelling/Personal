namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>
/// The weekly run's words in the Body trail: fixed phrases, a few counts and an exception's type name, never a title, a
/// name, a date or an exception's message. Each goes through the export's filter whole (<c>TrailLines</c>), so every word
/// here is in its vocabulary and a new one is a decision (<c>TrailVocabularyTests</c> reads this file).
/// </summary>
public static class WorkoutWeekTrail
{
    /// <summary>A move the owner made that names a session the week's plan no longer has: it is left out, and said so once, never named.</summary>
    public const string MoveNotFoundPhrase = "workout week, owner session not found in the plan, left out";

    /// <summary>The line for a whole run.</summary>
    public static string LogPhrase(WorkoutWeekOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome)
        {
            case WorkoutWeekOutcome.NotPlanned notPlanned:
                return NotPlannedPhrase(notPlanned.Because);
            case WorkoutWeekOutcome.Planned planned:
                var report = planned.Report;
                var sessions = WeekRewrite.Distinct(report.Plan).Count;
                return $"workout week planned, {sessions} sessions, {report.Placement.Created.Count} events written, "
                    + $"{report.Placement.Failed.Count} events failed, {report.PeopleWithUnreadableCalendars.Count} people unreadable";
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "This outcome has no trail phrase; add one here.");
        }
    }

    /// <summary>The line for one document.</summary>
    public static string LogPhrase(DocumentOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome.Verdict)
        {
            case DocumentVerdict.Delivered:
                return "workout document delivered";
            case DocumentVerdict.Replaced:
                return "workout document replaced before the slot";
            case DocumentVerdict.Withdrawn:
                return "workout document withdrawn, session called off";
            case DocumentVerdict.Unchanged:
                return "workout document unchanged";
            case DocumentVerdict.TooLate:
                return "workout document left as it is, the slot has begun";
            case DocumentVerdict.NotOnCalendar:
                return "workout document not made, session not on the calendar";
            case DocumentVerdict.NotBuilt:
                return outcome.Failure is null
                    ? "workout document not built"
                    : $"workout document not built: {outcome.Failure.GetType().Name}";
            case DocumentVerdict.NotDelivered:
                return outcome.Failure is null
                    ? "workout document not delivered, will try again"
                    : $"workout document not delivered: {outcome.Failure.GetType().Name}, will try again";
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Verdict, "This verdict has no trail phrase; add one here.");
        }
    }

    private static string NotPlannedPhrase(NotPlannedBecause because)
    {
        switch (because)
        {
            case NotPlannedBecause.RecordsUnreadable:
                return "workout week not planned, records unreadable";
            case NotPlannedBecause.CalendarsUnreadable:
                return "workout week not planned, calendars unreadable";
            case NotPlannedBecause.NoOneToPlan:
                return "workout week not planned, no one to plan";
            default:
                throw new ArgumentOutOfRangeException(nameof(because), because, "This reason has no trail phrase; add one here.");
        }
    }
}
