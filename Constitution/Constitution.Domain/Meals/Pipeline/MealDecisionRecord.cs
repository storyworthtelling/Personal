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
/// <param name="EvidencePending">Some written words were not judged (no judge answered); the next run asks again.</param>
public sealed record MealDecisionEntry(
    DateOnly Week,
    DateTimeOffset DecidedAt,
    int Approved,
    int Denied,
    int ApprovedUnread,
    bool EvidencePending)
{
    /// <summary>
    /// <b>Read for older records, never written (D111).</b> A denied dinner no longer asks for a revised sheet: its evening is left unplanned and a line goes on
    /// the shopping list. An entry the first build wrote still reads with this as it was.
    /// </summary>
    public bool NeedsRevisedSheet { get; init; }

    /// <summary>Read for older records, never written (D111): there is no replan, so no card needs vetoing.</summary>
    public IReadOnlyList<RecipeId> ReplanWithout { get; init; } = [];

    /// <summary>
    /// The evenings whose dinner the household denied while the evening was today or ahead, as frozen weekday words ("tuesday"), in week order (D111). Each was
    /// left unplanned in the kept week and is one plain line on the shopping list (<see cref="ExtraLines"/>).
    /// </summary>
    public IReadOnlyList<string> DeniedEvenings { get; init; } = [];

    /// <summary>
    /// The cooked evenings among them: the card and the cook block's planned times, so a judge that answers late still knows which dish the words were about and a
    /// calendar retry still knows where placement wrote the block (the kept week no longer says: the evening is unplanned).
    /// </summary>
    public IReadOnlyList<DeniedDish> DeniedDishes { get; init; } = [];

    /// <summary>The denied evenings whose cook block could not be taken off the calendar yet (it could not be looked into, or the edit failed); the next run tries again until the evening has passed.</summary>
    public IReadOnlyList<string> EventsPending { get; init; } = [];

    /// <summary>Whether a cook block still waits to come off the calendar for an evening that is today or ahead.</summary>
    public bool RemovalsDue(DateOnly today) =>
        EventsPending.Any(word => DeniedEveningWords.TryDay(word, out var day) && Week.AddDays((int)day) >= today);

    /// <summary>The shopping-list lines this week's denials add: "Tuesday dinner", one per denied evening. No dish and no ingredient.</summary>
    public IReadOnlyList<string> ExtraLines => DeniedEveningWords.Lines(DeniedEvenings);
}

/// <summary>A denied cooked evening: the weekday word, the card it had and where its cook block was placed. A card id and two moments; never a name.</summary>
public sealed record DeniedDish(string Evening, RecipeId Card, DateTimeOffset Start, DateTimeOffset End);

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
