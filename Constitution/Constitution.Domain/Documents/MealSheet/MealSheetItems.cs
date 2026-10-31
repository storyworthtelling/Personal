namespace Constitution.Domain.Documents;

/// <summary>One approvable line of pages 2 and 3, with the ids its two targets are answered under and the words a rule names it by.</summary>
internal sealed record MealSheetItem(string Key, string Label, string Text, string YesId, string ReasonId, string Subject)
{
    /// <summary>How the row reads aloud when a tick says it is fine.</summary>
    public string Meaning => $"yes, {Label}: {Text} is fine";
}

/// <summary>A heading and the lines under it.</summary>
internal sealed record MealSheetGroup(string Heading, IReadOnlyList<MealSheetItem> Items);

/// <summary>
/// The lines of pages 2 and 3 (book p.7, D30 and D31), in the order they are drawn. The painter and the expected document both read
/// them from here, so the page draws the instances the ink reader is told about and a row can never be added to one and not the other.
/// </summary>
internal static class MealSheetItems
{
    public const string BreakfastTitle = "Breakfast and lunch";
    public const string LunchboxTitle = "Lunchboxes";
    public const string BreakfastIntro = "Easy, uncooked weekday prep. Weekends are planned the same way.";
    public const string LunchboxIntro = "For the children, Monday to Friday. Nothing needs heating.";

    /// <summary>The sheet number of the breakfast and lunch page, and of the lunchbox page.</summary>
    public const int BreakfastSheetNumber = 2;

    public const int LunchboxSheetNumber = 3;

    public static IReadOnlyList<MealSheetGroup> BreakfastPage(MealSheetDailyMeals daily)
    {
        ArgumentNullException.ThrowIfNull(daily);
        Require(daily.WeekendBreakfast, "The weekend breakfast");
        Require(daily.WeekendLunch, "The weekend lunch");
        return
        [
            new("WEEKDAY BREAKFAST · MON–FRI", Weekday("breakfast", daily.WeekdayBreakfasts, MealSheetIds.BreakfastYes, MealSheetIds.BreakfastReason)),
            new("WEEKDAY LUNCH · MON–FRI", Weekday("lunch", daily.WeekdayLunches, MealSheetIds.LunchYes, MealSheetIds.LunchReason)),
            new(
                "WEEKEND · SAT–SUN",
                [
                    Item("weekend-breakfast", "Breakfast", daily.WeekendBreakfast, MealSheetIds.WeekendBreakfastYes, MealSheetIds.WeekendBreakfastReason, "Weekend breakfast"),
                    Item("weekend-lunch", "Lunch", daily.WeekendLunch, MealSheetIds.WeekendLunchYes, MealSheetIds.WeekendLunchReason, "Weekend lunch"),
                ]),
        ];
    }

    public static IReadOnlyList<MealSheetGroup> LunchboxPage(MealSheetDailyMeals daily)
    {
        ArgumentNullException.ThrowIfNull(daily);
        var box = daily.Lunchbox ?? throw new ArgumentException("The lunchbox page needs a lunchbox.", nameof(daily));
        return
        [
            new(
                "IN THE BOX",
                [
                    Item("box-sandwich", "Sandwich", box.Sandwich, MealSheetIds.BoxSandwichYes, MealSheetIds.BoxSandwichReason, "Lunchbox sandwich"),
                    Item("box-fruit", "Fruit", box.Fruit, MealSheetIds.BoxFruitYes, MealSheetIds.BoxFruitReason, "Lunchbox fruit"),
                    Item("box-snack", "Snack", box.Snack, MealSheetIds.BoxSnackYes, MealSheetIds.BoxSnackReason, "Lunchbox snack"),
                ]),
            new(
                "SEPARATE SNACK",
                [
                    Item("snack-fruit", "Fruit or veg", box.SeparateFruitOrVeg, MealSheetIds.SnackFruitYes, MealSheetIds.SnackFruitReason, "Lunchbox separate fruit or veg"),
                    Item("snack-bar", "Bar", box.SeparateBar, MealSheetIds.SnackBarYes, MealSheetIds.SnackBarReason, "Lunchbox separate bar"),
                ]),
        ];
    }

    /// <summary>Every line of the sheet's pages 2 and 3, page by page; empty when the model carries no daily meals.</summary>
    public static IReadOnlyList<(int SheetNumber, IReadOnlyList<MealSheetItem> Items)> Pages(MealSheetModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Daily is not { } daily)
        {
            return [];
        }

        return
        [
            (BreakfastSheetNumber, BreakfastPage(daily).SelectMany(g => g.Items).ToList()),
            (LunchboxSheetNumber, LunchboxPage(daily).SelectMany(g => g.Items).ToList()),
        ];
    }

    private static List<MealSheetItem> Weekday(string kind, IReadOnlyList<MealSheetLine> lines, Func<int, string> yes, Func<int, string> reason)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException($"The weekday {kind} has no lines; plan at least one.", nameof(lines));
        }

        if (lines.Count > MealSheetIds.WeekdaySlots)
        {
            throw new ArgumentException($"The weekday {kind} has {lines.Count} lines and a group numbers at most {MealSheetIds.WeekdaySlots}.", nameof(lines));
        }

        var items = new List<MealSheetItem>();
        for (var index = 0; index < lines.Count; index++)
        {
            Require(lines[index].Label, $"A weekday {kind} line's person");
            items.Add(Item($"{kind}-{index + 1}", lines[index].Label.Trim(), lines[index].Text, yes(index + 1), reason(index + 1), $"Weekday {kind}, {lines[index].Label.Trim()}"));
        }

        return items;
    }

    private static MealSheetItem Item(string key, string label, string text, string yesId, string reasonId, string subject)
    {
        Require(text, subject);
        return new MealSheetItem(key, label, text.Trim(), yesId, reasonId, $"{subject}: {text.Trim()}");
    }

    private static void Require(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{what} is blank; say what is planned.");
        }
    }
}
