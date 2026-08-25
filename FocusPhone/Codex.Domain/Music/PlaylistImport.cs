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
        var available = catalog.Where(track =>
            !taken.Contains(track.Id) && ArtistMatches(track, wanted.Artist)).ToList();

        return available.FirstOrDefault(track => FilterMatch.Equals(track.Title, wanted.Title))
            ?? available.FirstOrDefault(track =>
                FilterMatch.Equals(Simplify(track.Title), Simplify(wanted.Title)));
    }

    /// <summary>
    /// The shared list credits "Pat Barrett, Chris Tomlin" where the tag says
    /// "Pat Barrett", and occasionally the reverse — so either name containing the
    /// other counts. Containment is loose enough to pair a first name with a fuller
    /// credit, which is the trade for catching the collaborations; the title still
    /// has to agree, so a wrong pairing needs both halves to be wrong.
    /// </summary>
    private static bool ArtistMatches(Track track, string artist) =>
        Credited(track.ArtistName, artist) || Credited(track.AlbumArtist, artist);

    private static bool Credited(string? tagged, string listed) =>
        FilterMatch.Equals(tagged, listed)
        || (!string.IsNullOrWhiteSpace(tagged)
            && (FilterMatch.Matches(listed, tagged) || FilterMatch.Matches(tagged, listed)));

    /// <summary>
    /// Drops the edition noise: every parenthesised or bracketed aside, and anything
    /// after a spaced dash. Both are stripped from <b>both</b> sides, because either
    /// side can be the variant — the shared list may say "Dare You To Move - 2011
    /// Remaster" for a plain file, or say "Oh! Gravity" for a file tagged
    /// "Oh! Gravity (Acoustic)".
    ///
    /// It is deliberately blunt: "Song - Part 1" simplifies to "Song" too. That only
    /// ever loosens a match, and the exact pass runs first, so a title that agrees
    /// exactly is never given away to a simplification.
    /// </summary>
    private static string Simplify(string title)
    {
        var text = title ?? string.Empty;
        var builder = new System.Text.StringBuilder(text.Length);
        var depth = 0;

        foreach (var character in text)
        {
            if (character is '(' or '[')
            {
                depth++;
            }
            else if (character is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0)
            {
                builder.Append(character);
            }
        }

        var stripped = builder.ToString();
        var dash = stripped.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0)
        {
            stripped = stripped[..dash];
        }

        return stripped.Trim();
    }
}
