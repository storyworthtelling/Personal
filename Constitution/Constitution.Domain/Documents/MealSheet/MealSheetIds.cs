using System.Globalization;
using Constitution.Domain.Groceries;

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

    // Pages 2 and 3. A weekday line is answered under its slot in the page's order (the first person listed is slot 1), the weekend's
    // two lines and the lunchbox's five parts under their own words. Literals, so a stored reply finds its row by them.
    public const string WeekendBreakfastYes = "weekend-breakfast-yes";
    public const string WeekendBreakfastReason = "weekend-breakfast-why";
    public const string WeekendLunchYes = "weekend-lunch-yes";
    public const string WeekendLunchReason = "weekend-lunch-why";

    public const string BoxSandwichYes = "box-sandwich-yes";
    public const string BoxSandwichReason = "box-sandwich-why";
    public const string BoxFruitYes = "box-fruit-yes";
    public const string BoxFruitReason = "box-fruit-why";
    public const string BoxSnackYes = "box-snack-yes";
    public const string BoxSnackReason = "box-snack-why";
    public const string SnackFruitYes = "snack-fruit-yes";
    public const string SnackFruitReason = "snack-fruit-why";
    public const string SnackBarYes = "snack-bar-yes";
    public const string SnackBarReason = "snack-bar-why";

    /// <summary>The most weekday lines one group can number; the page has room for five between breakfast and lunch.</summary>
    public const int WeekdaySlots = 5;

    private static readonly string[] BreakfastYesIds = ["breakfast-1-yes", "breakfast-2-yes", "breakfast-3-yes", "breakfast-4-yes", "breakfast-5-yes"];
    private static readonly string[] BreakfastReasonIds = ["breakfast-1-why", "breakfast-2-why", "breakfast-3-why", "breakfast-4-why", "breakfast-5-why"];
    private static readonly string[] LunchYesIds = ["lunch-1-yes", "lunch-2-yes", "lunch-3-yes", "lunch-4-yes", "lunch-5-yes"];
    private static readonly string[] LunchReasonIds = ["lunch-1-why", "lunch-2-why", "lunch-3-why", "lunch-4-why", "lunch-5-why"];

    /// <summary>The YES box of the weekday breakfast in this slot (1 to <see cref="WeekdaySlots"/>).</summary>
    public static string BreakfastYes(int slot) => BreakfastYesIds[Slot(slot)];

    /// <summary>The "No, because" field of the weekday breakfast in this slot.</summary>
    public static string BreakfastReason(int slot) => BreakfastReasonIds[Slot(slot)];

    /// <summary>The YES box of the weekday lunch in this slot.</summary>
    public static string LunchYes(int slot) => LunchYesIds[Slot(slot)];

    /// <summary>The "No, because" field of the weekday lunch in this slot.</summary>
    public static string LunchReason(int slot) => LunchReasonIds[Slot(slot)];

    private static int Slot(int slot) =>
        slot is >= 1 and <= WeekdaySlots ? slot - 1 : throw new ArgumentOutOfRangeException(nameof(slot), slot, $"A weekday group has slots 1 to {WeekdaySlots}.");

    // The groceries page. A staple's box is answered under its aisle's frozen word (Aisles.Words) and its place in that aisle's run, in name
    // order; the blank lines for new staples under their number. A box is "need more": ticked is a need, blank is none.
    public const int NewStapleLines = 2;

    /// <summary>The box of the standing staple in this slot (1 and up) of this aisle, as "staple-dry-goods-2".</summary>
    public static string StapleNeed(Aisle aisle, int slot) =>
        slot >= 1 ? "staple-" + Aisles.Words.Word(aisle) + "-" + slot.ToString(CultureInfo.InvariantCulture)
            : throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slots count from 1.");

    /// <summary>The blank line for a new staple (1 to <see cref="NewStapleLines"/>), as "staple-new-1".</summary>
    public static string NewStaple(int line) =>
        line is >= 1 and <= NewStapleLines
            ? "staple-new-" + line.ToString(CultureInfo.InvariantCulture)
            : throw new ArgumentOutOfRangeException(nameof(line), line, $"There are {NewStapleLines} blank lines.");

    /// <summary>The YES box of the dinner on this weekday.</summary>
    public static string DinnerYes(DayOfWeek day) => Yes[Checked(day)];

    /// <summary>The shaded "No, because" field of the dinner on this weekday.</summary>
    public static string DinnerReason(DayOfWeek day) => Reason[Checked(day)];

    private static int Checked(DayOfWeek day) =>
        Enum.IsDefined(day) ? (int)day : throw new ArgumentOutOfRangeException(nameof(day), day, "A week has seven days.");
}
