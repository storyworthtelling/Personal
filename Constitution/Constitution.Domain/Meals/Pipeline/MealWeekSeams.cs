using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// Where the cooked history is kept. <see cref="Load"/> never throws for a file it cannot read: it says so, and the run plans
/// nothing (<see cref="MealNotPlannedBecause.HistoryUnreadable"/>). The run never writes here: only a dinner somebody confirmed cooked belongs in it.
/// </summary>
public interface IMealHistoryStore
{
    MealHistoryRead Load();

    /// <summary>
    /// Adds confirmed dinners (a union: the same card on the same evening is one entry) and drops those more than
    /// <see cref="MealHistoryRetention.Days"/> days before <paramref name="today"/>. Refused with <see cref="InvalidOperationException"/>
    /// when the saved file cannot be read, and left exactly as it was.
    /// </summary>
    void Append(IEnumerable<CookedMeal> meals, DateOnly today);
}

/// <summary>How long the history is kept: the stretch dish looks back eight weeks, so twice that is room to spare.</summary>
public static class MealHistoryRetention
{
    public const int Days = 112;
}

/// <summary>What reading the saved weeks gave: none yet, the weeks, or a file that is there and cannot be read (never an empty record).</summary>
public abstract record MealWeekRecordRead
{
    private MealWeekRecordRead()
    {
    }

    public sealed record NothingYet : MealWeekRecordRead;

    public sealed record Held(MealWeekRecord Record) : MealWeekRecordRead;

    public sealed record Unreadable(string Reason) : MealWeekRecordRead;
}

/// <summary>Where the planned weeks are kept for Today's dinner card and the Meals tab.</summary>
public interface IMealWeekRecords
{
    MealWeekRecordRead Load();

    /// <summary>Keeps the weeks. A file already there that cannot be read is set aside first, never written over.</summary>
    void Save(MealWeekRecord record);
}
