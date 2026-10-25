namespace Constitution.Domain.Documents;

/// <summary>
/// The ids of the targets on the dinners page (book p.7). A row is answered under its weekday, so the ids are
/// literals: they are the words <c>MealItemRule(subject, Yes, Reason, deadline)</c> names, and a stored reply
/// finds its row by them, so a page never invents its own and the set is frozen.
/// </summary>
public static class MealSheetIds
{
    public const string DinnerYesMonday = "dinner-mon-yes";
    public const string DinnerYesTuesday = "dinner-tue-yes";
    public const string DinnerYesWednesday = "dinner-wed-yes";
    public const string DinnerYesThursday = "dinner-thu-yes";
    public const string DinnerYesFriday = "dinner-fri-yes";
    public const string DinnerYesSaturday = "dinner-sat-yes";
    public const string DinnerYesSunday = "dinner-sun-yes";

    public const string DinnerReasonMonday = "dinner-mon-why";
    public const string DinnerReasonTuesday = "dinner-tue-why";
    public const string DinnerReasonWednesday = "dinner-wed-why";
    public const string DinnerReasonThursday = "dinner-thu-why";
    public const string DinnerReasonFriday = "dinner-fri-why";
    public const string DinnerReasonSaturday = "dinner-sat-why";
    public const string DinnerReasonSunday = "dinner-sun-why";

    /// <summary>The YES box of the dinner on this weekday.</summary>
    public static string DinnerYes(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => DinnerYesMonday,
        DayOfWeek.Tuesday => DinnerYesTuesday,
        DayOfWeek.Wednesday => DinnerYesWednesday,
        DayOfWeek.Thursday => DinnerYesThursday,
        DayOfWeek.Friday => DinnerYesFriday,
        DayOfWeek.Saturday => DinnerYesSaturday,
        DayOfWeek.Sunday => DinnerYesSunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "A week has seven days."),
    };

    /// <summary>The shaded "No, because" field of the dinner on this weekday.</summary>
    public static string DinnerReason(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => DinnerReasonMonday,
        DayOfWeek.Tuesday => DinnerReasonTuesday,
        DayOfWeek.Wednesday => DinnerReasonWednesday,
        DayOfWeek.Thursday => DinnerReasonThursday,
        DayOfWeek.Friday => DinnerReasonFriday,
        DayOfWeek.Saturday => DinnerReasonSaturday,
        DayOfWeek.Sunday => DinnerReasonSunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "A week has seven days."),
    };
}
