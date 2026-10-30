using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// What the deadline step settled for one week, kept so the week is never decided twice. Counts and card ids only: never a name, never the household's words.
/// </summary>
/// <param name="Week">The Sunday the sheet planned.</param>
/// <param name="DecidedAt">When the step decided it.</param>
/// <param name="Approved">Lines approved (ticked or by silence).</param>
/// <param name="Denied">Lines somebody said no to.</param>
/// <param name="ApprovedUnread">Lines approved at the deadline though no tablet was read for them (D103).</param>
/// <param name="NeedsRevisedSheet">A denied dinner or line still has an evening ahead, so the printed sheet no longer says what the household chose. Nothing here builds that sheet.</param>
/// <param name="ReplanWithout">The cards of the denied dinners that still have an evening ahead: the veto a replan of those evenings needs (<see cref="ReplanVeto"/>).</param>
/// <param name="EvidencePending">Some written words were not judged (no judge answered); the next run asks again.</param>
public sealed record MealDecisionEntry(
    DateOnly Week,
    DateTimeOffset DecidedAt,
    int Approved,
    int Denied,
    int ApprovedUnread,
    bool NeedsRevisedSheet,
    IReadOnlyList<RecipeId> ReplanWithout,
    bool EvidencePending);

/// <summary>The weeks the deadline step has decided, oldest first, the latest <see cref="Kept"/> only.</summary>
public sealed record MealDecisionRecord(IReadOnlyList<MealDecisionEntry> Entries)
{
    public const int Kept = 8;

    public static MealDecisionRecord Empty { get; } = new([]);

    public MealDecisionEntry? For(DateOnly week) => Entries.FirstOrDefault(entry => entry.Week == week);

    /// <summary>The entries with this one in place of any earlier entry for the same week.</summary>
    public MealDecisionRecord With(MealDecisionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new MealDecisionRecord(Entries.Where(item => item.Week != entry.Week).Append(entry).OrderBy(item => item.Week).TakeLast(Kept).ToList());
    }
}

/// <summary>What reading the decision record gave: none yet, the record, or a file that is there and cannot be read (never an empty record: an empty one would decide a week twice).</summary>
public abstract record MealDecisionRecordRead
{
    private MealDecisionRecordRead()
    {
    }

    public sealed record NothingYet : MealDecisionRecordRead;

    public sealed record Held(MealDecisionRecord Record) : MealDecisionRecordRead;

    public sealed record Unreadable(string Reason) : MealDecisionRecordRead;
}

/// <summary>Where the decided weeks are kept.</summary>
public interface IMealDecisionRecords
{
    MealDecisionRecordRead Load();

    /// <summary>Keeps the record. A file already there that cannot be read is set aside first, never written over.</summary>
    void Save(MealDecisionRecord record);
}
