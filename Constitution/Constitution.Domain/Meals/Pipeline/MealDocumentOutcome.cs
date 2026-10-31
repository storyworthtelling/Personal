using Constitution.Domain.Delivery;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>What became of one meal document in a run.</summary>
public enum MealDocumentVerdict
{
    /// <summary>Sent to the tablet for the first time and recorded for the read-back.</summary>
    Delivered,

    /// <summary>The page on the tablet was replaced, before its slot, under the same title.</summary>
    Replaced,

    /// <summary>Already on the tablet and saying the same thing.</summary>
    Unchanged,

    /// <summary>A recipe whose hour has not come: nothing was sent, and <see cref="MealDocumentOutcome.At"/> says when it may be.</summary>
    NotYet,

    /// <summary>It would be sent or changed, but the slot has begun (the cook block, or the sheet's deadline): nothing was sent.</summary>
    TooLate,

    /// <summary>A recipe for the other adult's tablet. Each phone is paired to its own tablet (D85), so this install does not send it.</summary>
    NotHere,

    /// <summary>The document could not be drawn. The reason is kept, with its stack, in the outcome.</summary>
    NotBuilt,

    /// <summary>The tablet did not take it (or its record could not be kept). Nothing was recorded for the read-back; the next run tries again.</summary>
    NotDelivered,

    /// <summary>The page on the tablet was left exactly where it was, because the owner has ink on it or nobody could tell.</summary>
    Kept,
}

/// <summary>
/// One document's outcome. <see cref="Failure"/> is kept with its stack for the verdicts that failed. <see cref="At"/> is when a recipe may
/// first be sent (<see cref="MealDocumentVerdict.NotYet"/>). <see cref="Ink"/> is what the look at the page found (<see cref="MealDocumentVerdict.Kept"/>).
/// </summary>
public sealed record MealDocumentOutcome(
    string Name,
    DeliveryKind Kind,
    string Person,
    DateOnly Day,
    MealDocumentVerdict Verdict,
    Exception? Failure = null,
    DateTimeOffset? At = null,
    PageInk? Ink = null);

/// <summary>Why no document was made or sent. A closed list: the trail prints the word.</summary>
public enum MealDocumentsStoppedBecause
{
    /// <summary>The setup names no adult whose tablet this phone has, so there is nowhere to send anything.</summary>
    NoTablet,

    /// <summary>The roster cannot be read, so nobody knows how many are eating or whose ratings to ask for.</summary>
    RosterUnavailable,

    /// <summary>The pantry is there and cannot be read.</summary>
    PantryUnreadable,
}

/// <summary>The documents step of a run: stopped before any document, or the outcome of each one in the order they were tried.</summary>
public abstract record MealDocumentsResult
{
    private MealDocumentsResult()
    {
    }

    /// <summary>Nothing was built or sent. The sentence is fixed words and quotes nothing the household wrote.</summary>
    public sealed record Stopped(MealDocumentsStoppedBecause Because, string Sentence) : MealDocumentsResult;

    /// <param name="Documents">The sheet first, then each cooked dinner's recipe in the week's order.</param>
    /// <param name="PantrySentence">Set when the sheet was built on an empty pantry because there is none to read; null otherwise.</param>
    /// <param name="UnreadableStaples">How many staple records could not be read and were left out.</param>
    public sealed record Done(IReadOnlyList<MealDocumentOutcome> Documents, string? PantrySentence, int UnreadableStaples) : MealDocumentsResult;
}
