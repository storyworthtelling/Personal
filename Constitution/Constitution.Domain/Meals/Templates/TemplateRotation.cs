using Constitution.Domain.Calendar;
using Constitution.Domain.Documents;

namespace Constitution.Domain.Meals.Templates;

/// <summary>
/// What the rotation needs to know of the table: the labels of the weekday breakfast lines and of the weekday lunch lines (the people
/// whose meals differ, as the sheet prints them), one line each, at most five. <see cref="LastWeek"/> is what the previous sheet printed,
/// so a template is not picked on the same line two weeks running.
/// </summary>
public sealed record TemplateRotationFacts(IReadOnlyList<string> BreakfastLabels, IReadOnlyList<string> LunchLabels)
{
    public IReadOnlyList<TemplatePrint> LastWeek { get; init; } = [];
}

/// <summary>How far the rotation got.</summary>
public enum TemplatePickStatus
{
    /// <summary>Every line of pages 2 and 3 has words.</summary>
    Ready,

    /// <summary>Some lines have nothing to print yet, so the pages are not printed.</summary>
    Incomplete,

    /// <summary>The library holds nothing approved and nothing to ask about.</summary>
    NoneYet,
}

/// <summary>The rotation's answer: the pages' meals when every line has words, what is missing when not, and what this sheet prints.</summary>
public sealed record TemplatePicks(
    TemplatePickStatus Status,
    MealSheetDailyMeals? Daily,
    IReadOnlyList<MealTemplateSlot> Missing,
    IReadOnlyList<TemplatePrint> Prints);

/// <summary>Where a template's line sits on the sheet: the frozen id of its YES box.</summary>
public static class TemplateItems
{
    /// <summary>The YES box of the <paramref name="line"/>th (from 0) line of the slot. Only the weekday slots have more than one line.</summary>
    public static string YesId(MealTemplateSlot slot, int line)
    {
        var weekday = slot is MealTemplateSlot.WeekdayBreakfast or MealTemplateSlot.WeekdayLunch;
        if (line < 0 || line >= (weekday ? MealSheetIds.WeekdaySlots : 1))
        {
            throw new ArgumentOutOfRangeException(nameof(line), line, "That slot has no such line.");
        }

        return slot switch
        {
            MealTemplateSlot.WeekdayBreakfast => MealSheetIds.BreakfastYes(line + 1),
            MealTemplateSlot.WeekdayLunch => MealSheetIds.LunchYes(line + 1),
            MealTemplateSlot.WeekendBreakfast => MealSheetIds.WeekendBreakfastYes,
            MealTemplateSlot.WeekendLunch => MealSheetIds.WeekendLunchYes,
            MealTemplateSlot.BoxSandwich => MealSheetIds.BoxSandwichYes,
            MealTemplateSlot.BoxFruit => MealSheetIds.BoxFruitYes,
            MealTemplateSlot.BoxSnack => MealSheetIds.BoxSnackYes,
            MealTemplateSlot.SnackFruitOrVeg => MealSheetIds.SnackFruitYes,
            MealTemplateSlot.SnackBar => MealSheetIds.SnackBarYes,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "No such slot."),
        };
    }
}

/// <summary>
/// Chooses the week's breakfasts, lunches and lunchbox from the approved templates (D110). Pure and deterministic: the library, the week and
/// the table give one answer, however often it is asked.
///
/// <para><b>Rotation.</b> Approved templates of a slot, in id order, are walked by the planning week's number; a second weekday line starts one
/// further along, so two people's lines differ while the pool allows. A line never takes the template it printed last week (the caller passes
/// last week's prints) when another exists, so a changed pool cannot repeat a line either. Rejected templates are never read.</para>
///
/// <para><b>Proposals.</b> A proposed template is not in the rotation. It is printed once, on the first line of its slot, in the first week that asks
/// for it, and again only if that same week is built again (a revised sheet); the sheet's answers then settle it (<see cref="TemplateFeedback"/>).</para>
///
/// <para><b>Complete or nothing.</b> Pages 2 and 3 need every line to have words, so one slot with nothing to print leaves both pages out, names the
/// slot, and marks no proposal as printed.</para>
/// </summary>
public static class TemplateRotation
{
    public static TemplatePicks Pick(TemplateLibrary library, DateOnly week, TemplateRotationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(facts);
        PlanningWeek.RequireStart(week, nameof(week));
        var breakfastLabels = Labels(facts.BreakfastLabels, "breakfast");
        var lunchLabels = Labels(facts.LunchLabels, "lunch");
        var number = PlanningWeek.IndexOf(week);

        var prints = new List<TemplatePrint>();
        var missing = new List<MealTemplateSlot>();
        var texts = new Dictionary<MealTemplateSlot, List<string>>();
        var anyDue = false;
        foreach (var slot in Enum.GetValues<MealTemplateSlot>())
        {
            var lines = slot switch
            {
                MealTemplateSlot.WeekdayBreakfast => breakfastLabels.Count,
                MealTemplateSlot.WeekdayLunch => lunchLabels.Count,
                _ => 1,
            };
            var pool = library.Approved.Where(template => template.Slot == slot).ToList();
            var due = library.Proposed
                .Where(template => template.Slot == slot && (library.LastPrintedWeek(template.Id) is not { } printed || printed == week))
                .FirstOrDefault();
            anyDue |= due is not null;

            var picked = new List<MealTemplate>();
            for (var line = 0; line < lines; line++)
            {
                var template = line == 0 && due is not null ? due : FromPool(pool, number + line, facts.LastWeek, TemplateItems.YesId(slot, line));
                if (template is null)
                {
                    break;
                }

                picked.Add(template);
            }

            if (picked.Count < lines)
            {
                missing.Add(slot);
                continue;
            }

            texts[slot] = picked.Select(template => template.Words).ToList();
            prints.AddRange(picked.Select((template, line) => new TemplatePrint(week, TemplateItems.YesId(slot, line), template.Id)));
        }

        if (missing.Count > 0)
        {
            var status = library.Approved.Count == 0 && !anyDue ? TemplatePickStatus.NoneYet : TemplatePickStatus.Incomplete;
            return new TemplatePicks(status, null, missing, []);
        }

        var daily = new MealSheetDailyMeals(
            Lines(breakfastLabels, texts[MealTemplateSlot.WeekdayBreakfast]),
            Lines(lunchLabels, texts[MealTemplateSlot.WeekdayLunch]),
            texts[MealTemplateSlot.WeekendBreakfast][0],
            texts[MealTemplateSlot.WeekendLunch][0],
            new MealSheetLunchbox(
                texts[MealTemplateSlot.BoxSandwich][0],
                texts[MealTemplateSlot.BoxFruit][0],
                texts[MealTemplateSlot.BoxSnack][0],
                texts[MealTemplateSlot.SnackFruitOrVeg][0],
                texts[MealTemplateSlot.SnackBar][0]));
        return new TemplatePicks(TemplatePickStatus.Ready, daily, [], prints);
    }

    private static MealTemplate? FromPool(IReadOnlyList<MealTemplate> pool, int turn, IReadOnlyList<TemplatePrint> lastWeek, string itemId)
    {
        if (pool.Count == 0)
        {
            return null;
        }

        var index = turn % pool.Count;
        var before = lastWeek.FirstOrDefault(print => print.ItemId == itemId)?.TemplateId;
        if (pool.Count > 1 && pool[index].Id == before)
        {
            index = (index + 1) % pool.Count;
        }

        return pool[index];
    }

    private static IReadOnlyList<MealSheetLine> Lines(IReadOnlyList<string> labels, IReadOnlyList<string> texts) =>
        labels.Select((label, line) => new MealSheetLine(label, texts[line])).ToList();

    private static List<string> Labels(IReadOnlyList<string>? labels, string meal)
    {
        if (labels is null || labels.Count == 0 || labels.Count > MealSheetIds.WeekdaySlots || labels.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"The weekday {meal} lines are one to {MealSheetIds.WeekdaySlots}, each with a name.", nameof(labels));
        }

        return labels.Select(label => label.Trim()).ToList();
    }
}
