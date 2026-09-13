using Folio.Domain.Analysis;

namespace Folio.Domain.Reading;

/// <summary>
/// Whether a reader's pass came back whole.
///
/// <para><b>A short read is a failed pass, not a shorter one</b> (F44, F46). That is the rule this
/// file exists for, and the reason it needs a rule at all is that the opposite is so easy to write:
/// the mapping this replaces coalesced every missing field to <c>string.Empty</c> or an empty list,
/// so a model that returned four of six parts produced a review that rendered perfectly and simply
/// said less. A persona that speaks only when it has something easy to say is a different
/// instrument, and the author would learn to skip it.</para>
///
/// <para>It is a function returning the reason rather than a throw, so the pipeline decides what a
/// failed pass costs — a retry, usually — instead of the parser deciding for it.</para>
/// </summary>
public static class ReaderRead
{
    /// <summary>The reason this read is incomplete, or null when it is whole.</summary>
    public static string? Incomplete(Review review)
    {
        if (review is null)
        {
            return "the read is missing entirely";
        }
        if (string.IsNullOrWhiteSpace(review.PersonaId))
        {
            return "the read is attributed to nobody";
        }

        var who = review.PersonaId;
        if (string.IsNullOrWhiteSpace(review.Verdict))
        {
            return $"{who} gave no verdict, so the readers' contents has nothing to print";
        }
        if (string.IsNullOrWhiteSpace(review.Assessment))
        {
            return $"{who} gave no review, only a verdict";
        }
        if (review.Strengths is not { Count: > 0 })
        {
            return $"{who} found nothing that works — which is a failed pass, not a harsh one";
        }
        if (review.Weaknesses is not { Count: > 0 })
        {
            return $"{who} found nothing that does not work — which is a failed pass, not a kind one";
        }
        if (string.IsNullOrWhiteSpace(review.Condition))
        {
            return $"{who} attached no condition, so the verdict is an opinion the author cannot act on";
        }
        return null;
    }

    public static bool IsWhole(Review review) => Incomplete(review) is null;
}
