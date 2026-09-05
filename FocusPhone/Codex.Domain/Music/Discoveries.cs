namespace Codex.Domain.Music;

/// <summary>
/// A record the crate is recommending — one the owner does <b>not</b> have.
///
/// Named rather than referenced by id, because an id is a thing the library already
/// holds and the whole point of a recommendation is that the library does not hold it.
/// <see cref="Why"/> is the one line the crate shows: a pick with no reason is just a
/// name, and a name is not a recommendation.
/// </summary>
public sealed record Recommendation(string Title, string Artist, string? Why = null);

/// <summary>
/// Turning what a recommender proposed into what the crate may actually show.
///
/// The crate shipped resurfacing music from the owner's own library — the instruction
/// behind it said so in as many words — which is the Phase 7 "New for you" shelf, not
/// the Discover crate P6 designed. This is the rule the redesign needs: what the owner
/// owns is <b>evidence of taste and an exclusion list</b>, never the source.
///
/// The exclusion is enforced here rather than asked for in the prompt. Asking is worth
/// doing and is not worth trusting: a recommender handed a listener's favourite artists
/// will name their favourite records back, and a crate offering to add something already
/// on the shelf is the exact failure that was reported.
/// </summary>
public static class Discoveries
{
    /// <summary>
    /// The recommendations worth showing: those the library does not already hold,
    /// with repeats and half-named entries dropped, in the order they arrived.
    /// </summary>
    public static IReadOnlyList<Recommendation> Unowned(
        IEnumerable<Recommendation> recommended, IReadOnlyList<Track> owned)
    {
        var kept = new List<Recommendation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pick in recommended)
        {
            if (string.IsNullOrWhiteSpace(pick.Title) || string.IsNullOrWhiteSpace(pick.Artist))
            {
                continue; // half a name is not a record anyone can go and find
            }

            if (!seen.Add(Key(pick)))
            {
                continue;
            }

            if (owned.Any(track => TrackIdentity.SameSong(
                    track.Title, track.ArtistName, pick.Title, pick.Artist)))
            {
                continue;
            }

            kept.Add(pick);
        }

        return kept;
    }

    /// <summary>
    /// A pick's stable id. The crate outlives a launch and the veto ledger keys off
    /// it, so it has to survive the same record being spelled differently next week —
    /// which is what <see cref="TrackIdentity.Simplify"/> is already the answer to.
    /// </summary>
    public static string Key(Recommendation pick) =>
        $"{Fold(pick.Artist)}|{Fold(pick.Title)}";

    // Simplify drops the edition noise; Fold settles case, spacing and accents. Both,
    // in that order, or "Only Hope" and "only hope " are two different records.
    private static string Fold(string? value) =>
        Design.FilterMatch.Fold(TrackIdentity.Simplify(value));
}
