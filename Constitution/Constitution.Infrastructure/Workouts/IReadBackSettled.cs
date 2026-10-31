using Constitution.Domain.ReadBack;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// Told by <see cref="ReadBackService"/> that a day's result is read and about to be kept, so whatever the pages taught
/// (the exercise ladders, the soreness, the assessment baselines) can be learned from it. It is called inside the same
/// step that saves the result and the skip ledger, before the document is marked saved: a failure here keeps the page on
/// the tablet and the next morning tries again, and learning from the same day twice changes nothing.
/// </summary>
public interface IReadBackSettled
{
    void Settled(ExpectedDocument document, DocumentResult result);
}
