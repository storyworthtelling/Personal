namespace Codex.Domain.Music;

/// <summary>
/// Finds owned tracks that are duplicates of a better copy of the same song — same
/// artist, album, and title — so the library shows one entry per song rather than one
/// per file. Aimed at the Drive-side problem the repo has already run into once:
/// repeated downloads landing as <c>(1)</c>/<c>(2)</c>-suffixed duplicate files.
/// </summary>
public static class DuplicateTracks
{
    /// <summary>
    /// Groups by exact (case/whitespace-insensitive) artist, album, and title —
    /// deliberately <b>not</b> the loose edition-stripping match
    /// <see cref="TrackIdentity"/> uses for cross-service reconciliation, which would
    /// wrongly conflate a live recording or an acoustic take with the studio cut it
    /// merely shares a title with. A real duplicate is the same recording twice.
    ///
    /// Within a group, the survivor is the highest <see cref="Track.Bitrate"/> — the
    /// signal the owner asked to prefer. A tie (including two files TagLib could not
    /// measure) falls back to the longer <see cref="Track.Duration"/>, since a
    /// truncated rip is the likelier of two otherwise-identical copies to be the worse
    /// one; a further tie falls back to the lower Id, so the result is deterministic
    /// regardless of input order or which file Drive happened to list first — the
    /// same two duplicates must not appear to swap places on every rescan.
    /// </summary>
    public static IReadOnlyList<string> FindLosingIds(IReadOnlyList<Track> tracks) =>
        [.. FindLosers(tracks).Select(loser => loser.LosingId)];

    /// <summary>
    /// Each losing copy with the copy it lost to. The scan sets a loser aside against its
    /// winner rather than forgetting it — its file is still in Drive — so that when the
    /// winner leaves Drive the loser can come back as the song.
    /// </summary>
    public static IReadOnlyList<(string LosingId, string WinnerId)> FindLosers(IReadOnlyList<Track> tracks) =>
        [.. tracks
            .GroupBy(track => (Key(track.ArtistName), Key(track.AlbumName), Key(track.Title)))
            .Where(group => group.Count() > 1)
            .SelectMany(group =>
            {
                var ranked = group
                    .OrderByDescending(track => track.Bitrate)
                    .ThenByDescending(track => track.Duration)
                    .ThenBy(track => track.Id, StringComparer.Ordinal)
                    .ToList();
                return ranked.Skip(1).Select(track => (track.Id, ranked[0].Id));
            })];

    private static string Key(string value) => value.Trim().ToLowerInvariant();
}
