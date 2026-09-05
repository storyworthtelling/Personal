namespace Codex.Domain.Music;

/// <summary>
/// One library out of two sources.
///
/// The standing rule is that owned files and streamed tracks are
/// <b>indistinguishable</b> in the app — one list, one queue, one listening log — and
/// this is where that is made true. Nothing downstream knows or asks which is which.
///
/// <b>The local file always wins.</b> Drive is the durable library and streaming is a
/// layer over it, so a subscription that lapses, a track that is pulled, or a region
/// that changes must never take away music the owner actually has. That is also why
/// an owned file beats an <i>unavailable</i> stream of the same song: the merge
/// deciding otherwise would be the app removing their own record from the shelf.
///
/// Pure. Ordering is the catalog's business — this decides what is in the library,
/// not how it is sorted.
/// </summary>
public static class LibraryMerge
{
    public static IReadOnlyList<Track> Merge(
        IReadOnlyList<Track> owned, IReadOnlyList<Track> streamed)
    {
        var library = new List<Track>(owned);

        foreach (var track in streamed)
        {
            // Compared against what is already in — owned files first, then whatever
            // the stream has already contributed — so a catalogue listing the same
            // song twice (a single and an album cut) adds it once.
            if (library.Any(held => TrackIdentity.SameSong(
                held.Title, held.ArtistName, track.Title, track.ArtistName)))
            {
                continue;
            }

            library.Add(track);
        }

        return library;
    }
}
