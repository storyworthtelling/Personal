namespace Constitution.Domain.Documents;

/// <summary>
/// The ids of the targets on the dinners page (book p.7). A row is answered under its weekday, so the ids are
/// literals (listed in the planning week's order, Sunday first): they are the words <c>MealItemRule(subject, Yes, Reason, deadline)</c> names, and a stored reply
/// finds its row by them, so a page never invents its own and the set is frozen.
/// </summary>
public static class MealSheetIds
{
    public const string DinnerYesSunday = "dinner-sun-yes";
    public const string DinnerYesMonday = "dinner-mon-yes";
    public const string DinnerYesTuesday = "dinner-tue-yes";
    public const string DinnerYesWednesday = "dinner-wed-yes";
    public const string DinnerYesThursday = "dinner-thu-yes";
    public const string DinnerYesFriday = "dinner-fri-yes";
    public const string DinnerYesSaturday = "dinner-sat-yes";

    public const string DinnerReasonSunday = "dinner-sun-why";
    public const string DinnerReasonMonday = "dinner-mon-why";
    public const string DinnerReasonTuesday = "dinner-tue-why";
    public const string DinnerReasonWednesday = "dinner-wed-why";
    public const string DinnerReasonThursday = "dinner-thu-why";
    public const string DinnerReasonFriday = "dinner-fri-why";
    public const string DinnerReasonSaturday = "dinner-sat-why";

    // Indexed by DayOfWeek's own numbering (Sunday is 0, the way .NET defines it), which is not the planning week's: a row is
    // answered under its weekday, whichever day the week opens on.
    private static readonly string[] Yes =
    [
        DinnerYesSunday,
        DinnerYesMonday,
        DinnerYesTuesday,
        DinnerYesWednesday,
        DinnerYesThursday,
        DinnerYesFriday,
        DinnerYesSaturday,
    ];

    private static readonly string[] Reason =
    [
        DinnerReasonSunday,
        DinnerReasonMonday,
        DinnerReasonTuesday,
        DinnerReasonWednesday,
        DinnerReasonThursday,
        DinnerReasonFriday,
        DinnerReasonSaturday,
    ];

    /// <summary>The YES box of the dinner on this weekday.</summary>
    public static string DinnerYes(DayOfWeek day) => Yes[Checked(day)];

    /// <summary>The shaded "No, because" field of the dinner on this weekday.</summary>
    public static string DinnerReason(DayOfWeek day) => Reason[Checked(day)];

    private static int Checked(DayOfWeek day) =>
        Enum.IsDefined(day) ? (int)day : throw new ArgumentOutOfRangeException(nameof(day), day, "A week has seven days.");
}
