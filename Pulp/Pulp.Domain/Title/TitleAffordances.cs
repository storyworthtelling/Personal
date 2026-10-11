using Pulp.Domain.Children;
using Pulp.Domain.ChildView;

namespace Pulp.Domain.Title;

/// <summary>
/// What a title's page offers, by who is watching (mockup 06, D161, D23, G13). An adult's page has
/// everything. A child's has the rating chip, Play and the child's own star; a title their filter hides
/// gets one quiet sentence in place of the page. "Open there instead" and Add to… are an adult's
/// household edits; the trailer opens another app outside the child gate; the why-this line is an
/// adult's taste (a child has none, D23); marking episodes writes an adult's record.
/// </summary>
public sealed record TitleAffordances(
    bool Visible,
    string? HiddenSentence,
    bool OpenThereInstead,
    bool AddTo,
    bool Trailer,
    bool WhyThis,
    bool MarkEpisodes,
    bool Star)
{
    /// <summary>A child's view whose children could not be resolved offers nothing, never the adult's page.</summary>
    public static TitleAffordances Unresolved { get; } =
        new(false, ChildViewText.TitleNotHere, false, false, false, false, false, false);

    /// <param name="audience">The children watching, or null for the device owner.</param>
    /// <param name="facts">The title as the child's filter reads it; null (unknown) is hidden from a child as unrated is (D128).</param>
    public static TitleAffordances For(ChildAudience? audience, TitleFacts? facts)
    {
        if (audience is null)
        {
            return new TitleAffordances(true, null, true, true, true, true, true, false);
        }

        return audience.Shows(facts)
            ? new TitleAffordances(true, null, false, false, false, false, false, true)
            : Unresolved;
    }
}
