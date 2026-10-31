using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>Why a run planned nothing. A closed list: the trail prints the word and the Waiting page can say it plainly.</summary>
public enum MealNotPlannedBecause
{
    /// <summary>The lease says another phone holds this week's planning, or this phone cannot yet say it does. Not an error: the partner's install is meant to land here.</summary>
    NotThePlanner,

    /// <summary>The rules name no adult to plan for.</summary>
    NoOneToPlan,

    /// <summary>There is no household setup on this phone (not saved, or it cannot be opened), so there are no rules to plan by.</summary>
    NotConfigured,

    /// <summary>The recipe box cannot be read now (no household file yet, or one that will not open).</summary>
    RecipesUnavailable,

    /// <summary>The box holds fewer than ten cards (the planner's own stop).</summary>
    BoxTooSmall,

    /// <summary>The saved dinner history is there and cannot be read. An unreadable memory is not an empty one.</summary>
    HistoryUnreadable,

    /// <summary>The saved taste profile is there and cannot be read. Read as empty it would plan a dish somebody said never again to.</summary>
    TasteUnreadable,

    /// <summary>A calendar could not be read for the whole week (the planner's own stop). Failing closed: unreadable is never "nothing on".</summary>
    CalendarsUnreadable,
}

/// <summary>What the run did with the saved week.</summary>
public enum MealRecordSave
{
    /// <summary>The week was new, or it differed from what was saved, and was written.</summary>
    Saved,

    /// <summary>The saved week already said the same: nothing was written.</summary>
    Unchanged,
}

/// <summary>
/// What a run that planned the week did. <see cref="Events"/> is what was asked of the calendar (cook blocks, then any morning starts,
/// then the Groceries trip); <see cref="Placement"/> is what became of it, null when nothing was asked.
/// </summary>
/// <param name="GroceryTripAsked">False when no daytime was left between approval and the week, so no trip was asked for.</param>
/// <param name="EarlierRecordUnreadable">The saved weeks could not be read at the start; they were set aside when this week was saved, and the planner had no earlier weeks to look back at.</param>
public sealed record MealWeekReport(
    MealWeek Week,
    PlanOrigin Origin,
    AuthorUnavailable? Reason,
    IReadOnlyList<HouseholdEventRequest> Events,
    PlacementRun? Placement,
    bool GroceryTripAsked,
    MealRecordSave RecordSave,
    bool EarlierRecordUnreadable)
{
    /// <summary>True when the calendar side could not be used at all (a record unreadable) and nothing was written.</summary>
    public bool PlacementBlocked => Placement?.BlockedBecause is not null;

    /// <summary>What the documents step did (the sheet and the recipes), or null when the run was not given the means to deliver any.</summary>
    public MealDocumentsResult? Documents { get; init; }

    /// <summary>Why the denied evenings' shopping lines could not be read (D112); null when they read, or when the run was given no record. The sheet was built without them.</summary>
    public string? ExtraLinesUnreadable { get; init; }

    /// <summary>What the rotating templates step did, or null when the run was not given the means to rotate any.</summary>
    public MealTemplatesResult? Templates { get; init; }
}

/// <summary>The result of one run of the weekly meal run: planned, or the typed reason it was not.</summary>
public abstract record MealWeekOutcome
{
    private MealWeekOutcome()
    {
    }

    /// <summary>Nothing was planned or placed. The sentence is fixed words and quotes nothing the household wrote.</summary>
    public sealed record NotPlanned(MealNotPlannedBecause Because, string Sentence) : MealWeekOutcome;

    public sealed record Planned(MealWeekReport Report) : MealWeekOutcome;
}
