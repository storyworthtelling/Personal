using Constitution.Domain.Documents;
using Storyworthtelling.Household;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// One kept week as the household file carries it for the other phone (the <c>week-plan</c> record, D126): the week as the run kept it, the moment its sheet's silence starts
/// to approve, the shopping lines a denied evening added, and the breakfasts, lunches and lunchbox the rotating templates chose. It holds what the other phone needs to print
/// the same sheet and the recipes of its own adult's dinners, and nothing else: no birthdate, no calendar entry, no key.
/// </summary>
public sealed record SharedWeekPlan(StoredMealWeek Stored, DateTimeOffset Deadline, IReadOnlyList<string> ExtraLines, MealSheetDailyMeals? Daily)
{
    public DateOnly WeekStart => Stored.WeekStart;
}

/// <summary>A plan as the file holds it, with the install id of the phone that wrote it (the lease holder's).</summary>
public sealed record HeldWeekPlan(SharedWeekPlan Plan, string Writer);

/// <summary>Why a week-plan record was set aside rather than read. The record is never guessed at.</summary>
public enum WeekPlanSkipReason
{
    /// <summary>A writer of a newer format left it; this build does not know how to read it.</summary>
    UnknownVersion,

    /// <summary>A field is missing or does not say what the record needs.</summary>
    Unreadable,
}

/// <summary>A week-plan record that was not read, and why. The sentence is fixed words and quotes nothing the record held.</summary>
public sealed record WeekPlanSkipped(RecordKey Key, WeekPlanSkipReason Reason)
{
    public const string UnknownVersionSentence = "A week plan was written by a newer version of the app, so this phone left it alone.";
    public const string UnreadableSentence = "A week plan in the household file could not be read, so this phone left it alone.";

    public string Sentence => Reason == WeekPlanSkipReason.UnknownVersion ? UnknownVersionSentence : UnreadableSentence;
}

/// <summary>The plans a household file held, in week order, and the records that were not read.</summary>
public sealed record WeekPlanSet(IReadOnlyList<HeldWeekPlan> Plans, IReadOnlyList<WeekPlanSkipped> Skipped);

/// <summary>What looking at the household file for other phones' plans gave: the plans, or the file's own reason it cannot be read now.</summary>
public abstract record WeekPlansRead
{
    private WeekPlansRead()
    {
    }

    /// <summary>The plans, and <paramref name="Me"/>, the install id of this phone (its own records are its mirror, not the other phone's word).</summary>
    public sealed record Held(WeekPlanSet Set, string Me) : WeekPlansRead;

    /// <summary>The file will not open or this phone's copy is damaged; nothing was read.</summary>
    public sealed record Blocked(string Sentence) : WeekPlansRead;
}

/// <summary>Where the plans other phones left in the household file are read from. Reads only.</summary>
public interface IWeekPlanSource
{
    WeekPlansRead Read();
}

/// <summary>Why a kept week was not put in the household file. A closed list.</summary>
public enum WeekPlanNotPublishedBecause
{
    /// <summary>The file will not open (no key, the wrong key, a damaged or newer file); nothing was written.</summary>
    FileUnreadable,

    /// <summary>This phone's own copy of the household is damaged and was left exactly as it was.</summary>
    CopyDamaged,

    /// <summary>The phone's copy could not be written.</summary>
    NotStored,
}

/// <summary>What the lease holder's publish of a kept week came to. A publish that did not happen is a typed answer, never a failed run.</summary>
public abstract record WeekPlanPublication
{
    private WeekPlanPublication()
    {
    }

    /// <summary>The run was given nothing to publish with.</summary>
    public sealed record NotAttempted : WeekPlanPublication;

    /// <summary>The week is now in this phone's copy of the household file; the next sync carries it to Drive.</summary>
    public sealed record Published : WeekPlanPublication;

    /// <summary>The file already held exactly this week.</summary>
    public sealed record Unchanged : WeekPlanPublication;

    /// <summary>Nothing was written. <paramref name="Sentence"/> is fixed words; <paramref name="Failure"/> keeps what was thrown, stack and all, for a log.</summary>
    public sealed record NotPublished(WeekPlanNotPublishedBecause Because, string Sentence, Exception? Failure = null) : WeekPlanPublication;
}

/// <summary>Puts a kept week into the household file for the other phone. Called only by the run that holds the lease.</summary>
public interface IWeekPlanPublisher
{
    Task<WeekPlanPublication> PublishAsync(SharedWeekPlan plan, CancellationToken cancellation);
}
