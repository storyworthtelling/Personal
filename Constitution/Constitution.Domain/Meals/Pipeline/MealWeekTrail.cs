using Constitution.Domain.Workouts;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// The weekly meal run's words in the Body trail: fixed phrases, counts and nothing else, never a title, a name or a date. Every
/// word is in the trail vocabulary (<c>TrailVocabularyTests</c> reads the literals returned below), all lower case because the export's
/// filter reads a capital as a possible name.
/// </summary>
public static class MealWeekTrail
{
    /// <summary>The line for a whole run.</summary>
    public static string LogPhrase(MealWeekOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome)
        {
            case MealWeekOutcome.NotPlanned notPlanned:
                return NotPlannedPhrase(notPlanned.Because);
            case MealWeekOutcome.Planned planned:
                var report = planned.Report;
                var written = report.Placement?.Created.Count ?? 0;
                var failed = report.Placement?.Failed.Count ?? 0;
                return $"meal week planned, {written} events written, {failed} events failed, record {SaveWord(report.RecordSave)}{BlockedTail(report)}{EarlierTail(report)}{DecisionsTail(report)}";
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "This outcome has no trail phrase; add one here.");
        }
    }

    /// <summary>The line for the author a week came from.</summary>
    public static string LogPhrase(PlanOrigin origin, AuthorUnavailable? reason)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "There is no such plan origin.");
        }

        if (origin == PlanOrigin.Authored)
        {
            return "meal plan: authored";
        }

        return reason is null ? "meal plan: rules only" : $"meal plan: rules only ({ReasonWords(reason.Value)})";
    }

    private static string NotPlannedPhrase(MealNotPlannedBecause because)
    {
        switch (because)
        {
            case MealNotPlannedBecause.NotThePlanner:
                return "meal week not planned, not the planner this week";
            case MealNotPlannedBecause.NoOneToPlan:
                return "meal week not planned, no one to plan";
            case MealNotPlannedBecause.NotConfigured:
                return "meal week not planned, not configured";
            case MealNotPlannedBecause.RecipesUnavailable:
                return "meal week not planned, recipe box unreadable";
            case MealNotPlannedBecause.BoxTooSmall:
                return "meal week not planned, recipe box too small";
            case MealNotPlannedBecause.HistoryUnreadable:
                return "meal week not planned, history unreadable";
            case MealNotPlannedBecause.TasteUnreadable:
                return "meal week not planned, taste profile unreadable";
            case MealNotPlannedBecause.CalendarsUnreadable:
                return "meal week not planned, calendars unreadable";
            default:
                throw new ArgumentOutOfRangeException(nameof(because), because, "This reason has no trail phrase; add one here.");
        }
    }

    private static string SaveWord(MealRecordSave save)
    {
        switch (save)
        {
            case MealRecordSave.Saved:
                return "saved";
            case MealRecordSave.Unchanged:
                return "unchanged";
            default:
                throw new ArgumentOutOfRangeException(nameof(save), save, "This save has no trail word; add one here.");
        }
    }

    private static string BlockedTail(MealWeekReport report)
    {
        if (report.PlacementBlocked)
        {
            return ", placement blocked";
        }

        return string.Empty;
    }

    private static string EarlierTail(MealWeekReport report)
    {
        if (report.EarlierRecordUnreadable)
        {
            return ", earlier record unreadable";
        }

        return string.Empty;
    }

    private static string DecisionsTail(MealWeekReport report) =>
        report.ExtraLinesUnreadable is null ? string.Empty : ", decisions unreadable";

    private static string ReasonWords(AuthorUnavailable reason)
    {
        switch (reason)
        {
            case AuthorUnavailable.NotConfigured:
                return "not configured";
            case AuthorUnavailable.Offline:
                return "offline";
            case AuthorUnavailable.TimedOut:
                return "timed out";
            case AuthorUnavailable.KeyRejected:
                return "key rejected";
            case AuthorUnavailable.ReplyUnusable:
                return "reply unusable";
            case AuthorUnavailable.BrokeTheRules:
                return "broke the rules";
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "There is no such reason.");
        }
    }
}
