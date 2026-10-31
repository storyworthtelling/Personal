using Constitution.Domain.Calendar;
using Constitution.Domain.Documents;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>What the template library gave the run. Never an empty library standing in for one that could not be read.</summary>
public abstract record TemplateLibraryRead
{
    private TemplateLibraryRead()
    {
    }

    /// <summary>The library as the household file holds it. <paramref name="UnreadableRecords"/> counts template records this build could not open.</summary>
    public sealed record Held(TemplateLibrary Library, int UnreadableRecords) : TemplateLibraryRead;

    /// <summary>No household file yet (the phone has not met one): there is nowhere to keep a library, so none is made.</summary>
    public sealed record NoneYet(string Sentence) : TemplateLibraryRead;

    /// <summary>The file is there and cannot be read. The sentence is fixed words and quotes nothing the household wrote.</summary>
    public sealed record Unreadable(string Reason) : TemplateLibraryRead;
}

/// <summary>What became of a save of the library.</summary>
public abstract record TemplateSave
{
    private TemplateSave()
    {
    }

    public sealed record Saved : TemplateSave;

    /// <summary>Nothing was written; the sentence says why.</summary>
    public sealed record Refused(string Sentence) : TemplateSave;
}

/// <summary>
/// Where the template library is kept: records in the household file. Any phone may <see cref="Load"/> it; <see cref="Save"/> is for the planner
/// alone (the weekly run reaches it only after the lease says this phone plans the week), so a partner's phone never writes a proposal or a print.
/// </summary>
public interface ITemplateLibraryStore
{
    TemplateLibraryRead Load();

    TemplateSave Save(TemplateLibrary library);
}

/// <summary>How far the templates step got.</summary>
public enum TemplateRunStatus
{
    /// <summary>Every line of pages 2 and 3 has words and they are on this sheet.</summary>
    Printed,

    /// <summary>Some slot has nothing to print yet: pages 2 and 3 are left out and nothing is marked printed.</summary>
    Incomplete,

    /// <summary>The library holds nothing to print and nothing was proposed.</summary>
    NothingYet,

    /// <summary>No household file yet.</summary>
    NoLibraryYet,

    /// <summary>The household file's library cannot be read.</summary>
    LibraryUnreadable,

    /// <summary>The sheet's deadline has passed and no template was ever printed for this week: a new proposal would be marked shown and never seen.</summary>
    PastDeadline,

    /// <summary>The library could not be saved, so nothing is printed: a line that cannot be remembered cannot be answered.</summary>
    NotSaved,
}

/// <summary>What the templates step did: the pages' meals when they are to be printed, and the counts the trail and the report speak in.</summary>
public sealed record MealTemplatesResult(TemplateRunStatus Status, int Proposed, int Printed, MealSheetDailyMeals? Daily)
{
    /// <summary>Why the author gave nothing, when it was asked and could not.</summary>
    public AuthorUnavailable? AuthorReason { get; init; }

    /// <summary>Template records that could not be read; while any exist the author is not asked, since it could not be told what is already known.</summary>
    public int UnreadableRecords { get; init; }
}

/// <summary>Which slots want more templates and how many to ask for.</summary>
public sealed record TemplateWant(IReadOnlyList<MealTemplateSlot> Slots, int Count);

/// <summary>The templates step's pure rules.</summary>
public static class TemplateRunRules
{
    /// <summary>A slot is kept stocked with at least this many approved templates (or one per weekday line, if more), so the rotation has something to turn. A guess.</summary>
    public const int ApprovedPerSlot = 3;

    /// <summary>The weekday lines' labels: the adults as the rules name them, the same names the rest of the sheet prints, at most the sheet's five.</summary>
    public static IReadOnlyList<string> Labels(PlanningRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Adults.Select(adult => adult.Name).Take(MealSheetIds.WeekdaySlots).ToList();
    }

    /// <summary>
    /// What to ask the author for, or a count of 0 when nothing is wanted. A slot is short when its approved and still-waiting templates together are fewer than
    /// <see cref="ApprovedPerSlot"/> (or its weekday lines, if more); the count is the shortfall, never more than the room left under
    /// <see cref="TemplateProposals.MaxOutstanding"/> waiting proposals.
    /// </summary>
    public static TemplateWant Wants(TemplateLibrary library, int breakfastLines, int lunchLines)
    {
        ArgumentNullException.ThrowIfNull(library);
        var room = TemplateProposals.MaxOutstanding - library.Proposed.Count;
        var slots = new List<MealTemplateSlot>();
        var shortfall = 0;
        foreach (var slot in Enum.GetValues<MealTemplateSlot>())
        {
            var lines = slot switch
            {
                MealTemplateSlot.WeekdayBreakfast => breakfastLines,
                MealTemplateSlot.WeekdayLunch => lunchLines,
                _ => 1,
            };
            var held = library.Templates.Count(template => template.Slot == slot && template.Status is TemplateStatus.Approved or TemplateStatus.Proposed);
            var shortBy = Math.Max(ApprovedPerSlot, lines) - held;
            if (shortBy > 0)
            {
                slots.Add(slot);
                shortfall += shortBy;
            }
        }

        return room <= 0 || slots.Count == 0 ? new TemplateWant([], 0) : new TemplateWant(slots, Math.Min(shortfall, room));
    }

    /// <summary>True when two libraries hold the same templates and prints, so a run that changed nothing writes nothing.</summary>
    public static bool Same(TemplateLibrary first, TemplateLibrary second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return first.Templates.SequenceEqual(second.Templates) && first.Prints.SequenceEqual(second.Prints);
    }
}

/// <summary>The templates step's words in the Body trail: fixed phrases and counts, never a dish, a name or a date. Lower case; every word is in the trail vocabulary.</summary>
public static class MealTemplateTrail
{
    public static string LogPhrase(MealTemplatesResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var tail = result.UnreadableRecords > 0 ? $", {result.UnreadableRecords} records unreadable" : string.Empty;
        return Phrase(result) + tail;
    }

    private static string Phrase(MealTemplatesResult result)
    {
        switch (result.Status)
        {
            case TemplateRunStatus.Printed:
                return $"meal templates: {result.Proposed} templates proposed, {result.Printed} templates printed";
            case TemplateRunStatus.Incomplete:
                return $"meal templates: {result.Proposed} templates proposed, none printed";
            case TemplateRunStatus.NothingYet:
                return "meal templates: nothing yet, none printed";
            case TemplateRunStatus.NoLibraryYet:
                return "meal templates: no library yet";
            case TemplateRunStatus.LibraryUnreadable:
                return "meal templates: library unreadable";
            case TemplateRunStatus.PastDeadline:
                return "meal templates: past deadline, none printed";
            case TemplateRunStatus.NotSaved:
                return "meal templates: library not saved, none printed";
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Status, "This status has no trail phrase; add one here.");
        }
    }
}
