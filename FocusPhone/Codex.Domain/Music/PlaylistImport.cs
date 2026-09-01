using Codex.Domain.Design;

namespace Codex.Domain.Music;

/// <summary>
/// One track as a shared playlist names it — a title and whoever the sharer's
/// service credits, which is not necessarily what the file's tags say.
/// </summary>
public sealed record ImportedTrack(string Title, string Artist, string? Album = null);

/// <summary>
/// What an imported list came to in this library: the tracks found, in the order
/// the list gave them, and the ones that were not found, by name.
/// </summary>
public sealed record PlaylistImportResult(
    IReadOnlyList<Track> Matched,
    IReadOnlyList<ImportedTrack> Missing);

/// <summary>
/// Resolves a shared playlist against the merged library.
///
/// The hard part is not fetching the list, it is that the two sides name the same
/// song differently: a streaming catalogue carries edition noise the owner's files
/// do not (<c>- 2011 Remaster</c>, <c>(Radio Edit)</c>, <c>[Live]</c>,
/// <c>(feat. …)</c>) and credits every performer where a tag usually credits one.
/// A naive comparison therefore misses songs the owner actually owns, and a partial
/// result is the normal case — so what did not resolve comes back by name rather
/// than being dropped quietly.
/// </summary>
public static class PlaylistImport
{
    /// <summary>
    /// Matches each wanted track against the catalog, keeping the list's order —
    /// a setlist arrives in the order it will be played and that order is the point.
    ///
    /// Two passes per track: an exact title first, then a simplified one, so a
    /// library holding both "Song" and "Song (Acoustic)" gives up the plain one for
    /// a plain request. A catalog track is consumed once, so a list naming the same
    /// song twice reports the second as missing rather than repeating it.
    /// </summary>
    public static PlaylistImportResult Resolve(
        IReadOnlyList<ImportedTrack> wanted, IReadOnlyList<Track> catalog)
    {
        var matched = new List<Track>();
        var missing = new List<ImportedTrack>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var track in wanted)
        {
            var found = Find(track, catalog, taken);
            if (found is null)
            {
                missing.Add(track);
                continue;
            }

            taken.Add(found.Id);
            matched.Add(found);
        }

        return new PlaylistImportResult(matched, missing);
    }

    private static Track? Find(
        ImportedTrack wanted, IReadOnlyList<Track> catalog, HashSet<string> taken)
    {
        // The artist has to agree either way; what the two passes choose between is
        // which of the surviving candidates is meant.
        var available = catalog.Where(track =>
            !taken.Contains(track.Id) && Credited(track, wanted.Artist)).ToList();

        return available.FirstOrDefault(track => FilterMatch.Equals(track.Title, wanted.Title))
            ?? available.FirstOrDefault(track =>
                FilterMatch.Equals(TrackIdentity.Simplify(track.Title), TrackIdentity.Simplify(wanted.Title)));
    }

    // A tag credits one performer where a shared list credits all of them, and the
    // album artist is sometimes the only one that matches.
    private static bool Credited(Track track, string artist) =>
        TrackIdentity.SameArtist(track.ArtistName, artist)
        || TrackIdentity.SameArtist(track.AlbumArtist, artist);
}
