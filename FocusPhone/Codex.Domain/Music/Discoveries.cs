namespace Codex.Domain.Music;

/// <summary>
/// What kind of thing a crate pick names — a track, an album, or a podcast show. The crate
/// started track-only; a pick's kind decides both what "already owned/subscribed" means for it
/// (<see cref="Discoveries.Unowned"/>) and what tapping it does (Play for Song/Album, Subscribe
/// for Podcast — Score has no way to browse or preview an album or a show it doesn't already
/// hold, so those actions are what the app can actually do, not a full streaming browse).
/// </summary>
public enum DiscoveryKind
{
    Song,
    Album,
    Podcast,
}

/// <summary>
/// A record the crate is recommending — one the owner does <b>not</b> have.
///
/// Named rather than referenced by id, because an id is a thing the library already
/// holds and the whole point of a recommendation is that the library does not hold it.
/// <see cref="Why"/> is the one line the crate shows: a pick with no reason is just a
/// name, and a name is not a recommendation. For a <see cref="DiscoveryKind.Podcast"/> pick,
/// <see cref="Artist"/> holds the show's network/publisher when the recommender named one, and
/// is blank otherwise — a show does not always have one worth stating.
/// </summary>
public sealed record Recommendation(string Title, string Artist, string? Why = null, DiscoveryKind Kind = DiscoveryKind.Song);

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
    /// The recommendations worth showing: those the library (or, for a
    /// <see cref="DiscoveryKind.Podcast"/> pick, the subscription list) does not already hold,
    /// with repeats and half-named entries dropped, in the order they arrived.
    /// </summary>
    /// <param name="subscribedShows">
    /// Shows already subscribed to, by title — the podcast side's equivalent of
    /// <paramref name="owned"/>. Only checked against <see cref="DiscoveryKind.Podcast"/> picks;
    /// omit it when the caller has nothing to offer (it simply excludes nothing).
    /// </param>
    public static IReadOnlyList<Recommendation> Unowned(
        IEnumerable<Recommendation> recommended, IReadOnlyList<Track> owned,
        IReadOnlyList<string>? subscribedShows = null)
    {
        var kept = new List<Recommendation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pick in recommended)
        {
            // A podcast pick names a show, not an artist — a network/publisher is worth
            // showing when the recommender gave one, but its absence is not a half-named pick
            // the way a blank Artist would be for a Song or an Album.
            var needsArtist = pick.Kind != DiscoveryKind.Podcast;
            if (string.IsNullOrWhiteSpace(pick.Title) || (needsArtist && string.IsNullOrWhiteSpace(pick.Artist)))
            {
                continue; // half a name is not a record anyone can go and find
            }

            if (!seen.Add(Key(pick)))
            {
                continue;
            }

            if (IsAlreadyHeld(pick, owned, subscribedShows))
            {
                continue;
            }

            kept.Add(pick);
        }

        return kept;
    }

    /// <summary>
    /// The picks the owner asked for, by kind: at most <paramref name="music"/> songs and
    /// albums, and at most <paramref name="podcasts"/> shows, in the order they arrived.
    /// Anything over a count is dropped; a short delivery is returned as it was.
    /// </summary>
    public static IReadOnlyList<Recommendation> Shape(IEnumerable<Recommendation> picks, int music, int podcasts)
    {
        var musicLeft = Math.Max(0, music);
        var podcastsLeft = Math.Max(0, podcasts);
        var shaped = new List<Recommendation>();

        foreach (var pick in picks)
        {
            if (pick.Kind == DiscoveryKind.Podcast)
            {
                if (podcastsLeft == 0)
                {
                    continue;
                }

                podcastsLeft--;
            }
            else
            {
                if (musicLeft == 0)
                {
                    continue;
                }

                musicLeft--;
            }

            shaped.Add(pick);
        }

        return shaped;
    }

    private static bool IsAlreadyHeld(
        Recommendation pick, IReadOnlyList<Track> owned, IReadOnlyList<string>? subscribedShows) => pick.Kind switch
    {
        DiscoveryKind.Song => owned.Any(track =>
            TrackIdentity.SameSong(track.Title, track.ArtistName, pick.Title, pick.Artist)),

        // An album is "owned" once any of its tracks are — the library groups tracks into
        // albums by (album, album artist), the same pair a Recommendation for an album carries.
        DiscoveryKind.Album => owned.Any(track =>
            TrackIdentity.SameSong(track.AlbumName, track.AlbumArtist, pick.Title, pick.Artist)),

        DiscoveryKind.Podcast => subscribedShows?.Any(show =>
            Fold(show) == Fold(pick.Title)) ?? false,

        _ => false,
    };

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
