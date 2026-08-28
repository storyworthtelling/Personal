using Codex.Domain.Design;

namespace Codex.Domain.Music;

/// <summary>
/// When two names are the same song.
///
/// It exists because three separate features ask this question — importing a shared
/// playlist, merging a streaming catalogue into the owner's own library, and telling
/// a track that is unavailable from one that is simply absent — and they must not
/// answer it differently. A song that the import finds but the merge treats as new
/// would appear twice in the library, which is precisely the seam the merged-library
/// rule exists to remove.
///
/// The hard part is that the two sides name the same song differently. A streaming
/// catalogue carries edition noise the owner's files do not (<c>- 2011 Remaster</c>,
/// <c>(Radio Edit)</c>, <c>[Live]</c>, <c>(feat. …)</c>) and credits every performer
/// where a tag usually credits one.
/// </summary>
public static class TrackIdentity
{
    /// <summary>
    /// The same song by the same act, allowing for how differently the two sides name
    /// it. Both halves have to agree, which is what keeps the loose artist comparison
    /// from pairing unrelated records.
    /// </summary>
    public static bool SameSong(string? titleA, string? artistA, string? titleB, string? artistB) =>
        SameArtist(artistA, artistB)
        && (FilterMatch.Equals(titleA, titleB ?? string.Empty)
            || FilterMatch.Equals(Simplify(titleA), Simplify(titleB)));

    /// <summary>
    /// One list credits "Pat Barrett, Chris Tomlin" where the tag says "Pat Barrett",
    /// and occasionally the reverse — so either name containing the other counts.
    /// Containment is loose enough to pair a first name with a fuller credit, which is
    /// the trade for catching collaborations; the title still has to agree, so a wrong
    /// pairing needs both halves to be wrong.
    /// </summary>
    public static bool SameArtist(string? a, string? b) =>
        FilterMatch.Equals(a, b ?? string.Empty)
        || (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && (FilterMatch.Matches(a, b) || FilterMatch.Matches(b, a)));

    /// <summary>
    /// Drops the edition noise: every parenthesised or bracketed aside, and anything
    /// after a spaced dash. Applied to <b>both</b> sides, because either can be the
    /// variant — a catalogue may say "Dare You To Move - 2011 Remaster" for a plain
    /// file, or say "Oh! Gravity" for a file tagged "Oh! Gravity (Acoustic)".
    ///
    /// Deliberately blunt: "Song - Part 1" simplifies to "Song" too. That only ever
    /// loosens a match, and every caller tries an exact comparison first, so a title
    /// that agrees exactly is never given away to a simplification.
    /// </summary>
    public static string Simplify(string? title)
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
