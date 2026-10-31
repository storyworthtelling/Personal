using Constitution.Domain.Delivery;
using Constitution.Domain.Household;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;

namespace Constitution.Domain.Meals;

/// <summary>How the read-back's resolutions of a meal sheet become answers.</summary>
public static class MealAnswerInk
{
    /// <summary>
    /// The answer a meal line's resolution amounts to, or null when it is not an answer: a line the read could not settle
    /// (<see cref="InkResolution.NeedsAsking"/>, <see cref="InkResolution.NotRead"/>) says nothing, and is never read as silence.
    /// An untouched line is <see cref="MealVerdict.Unchanged"/> whether the read came before the deadline
    /// (<see cref="InkResolution.Undecided"/>) or after it (<see cref="InkResolution.ApprovedAtDeadline"/>): what silence comes to
    /// is the decision's to say, once, not the read's.
    /// </summary>
    public static MealAnswer? From(InkResolution resolution, PersonId person, DateOnly week, DateTimeOffset readAt)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return resolution switch
        {
            InkResolution.Ticked ticked => new MealAnswer(person, week, ticked.SubjectId, MealVerdict.Approved, null, readAt),
            InkResolution.Denied denied => new MealAnswer(person, week, denied.SubjectId, MealVerdict.Denied, denied.Reason, readAt),
            InkResolution.ApprovedAtDeadline silent => new MealAnswer(person, week, silent.SubjectId, MealVerdict.Unchanged, null, readAt),
            InkResolution.Undecided silent => new MealAnswer(person, week, silent.SubjectId, MealVerdict.Unchanged, null, readAt),
            _ => null,
        };
    }

    /// <summary>Every answer a read meal sheet holds, in page order; <paramref name="week"/> is the week the sheet plans.</summary>
    public static IReadOnlyList<MealAnswer> FromResult(DocumentResult result, PersonId person, DateOnly week)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Kind != DeliveryKind.Meals)
        {
            throw new ArgumentException($"Only a meal sheet holds meal answers; this document is a {result.Kind}.", nameof(result));
        }

        return result.All.Select(resolution => From(resolution, person, week, result.ReadAt)).OfType<MealAnswer>().ToList();
    }
}
