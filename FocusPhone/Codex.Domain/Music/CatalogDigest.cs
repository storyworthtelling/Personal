namespace Codex.Domain.Music;

/// <summary>
/// What the library looks like to a model that cannot be handed all of it.
///
/// The prompt has to be bounded, but <i>how</i> it is bounded decides what the model
/// can possibly choose. Taking the head of a catalog that is sorted by artist shows
/// it the letter A and tells it that is the library — so a generated playlist could
/// only ever be drawn from the front of the alphabet, and a request the rest of the
/// library answers perfectly comes back thin or empty.
///
/// So the sample is taken across artists rather than off the top: one track from each
/// artist in turn, going round again for a second only once every artist has had a
/// first. Breadth is the property worth guaranteeing — the taste profile, not the
/// digest, is what shapes the pick, and it cannot shape a pick over music it was
/// never shown.
/// </summary>
public static class CatalogDigest
{
    /// <summary>
    /// How many tracks a prompt will carry. High enough that a real library goes in
    /// whole (the owner's is ~1,300), bounded because a library only grows.
    /// </summary>
    public const int MaxTracks = 1_500;

    /// <summary>
    /// A representative slice of the catalog, at most <paramref name="limit"/> tracks.
    /// A catalog that already fits is returned untouched, in its own order.
    /// </summary>
    public static IReadOnlyList<Track> Sample(IReadOnlyList<Track> catalog, int limit = MaxTracks)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (limit <= 0)
        {
            return [];
        }

        if (catalog.Count <= limit)
        {
            return catalog;
        }

        var byArtist = new List<List<Track>>();
        var index = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in catalog)
        {
            if (!index.TryGetValue(track.DisplayArtist, out var run))
            {
                run = [];
                index[track.DisplayArtist] = run;
                byArtist.Add(run);
            }

            run.Add(track);
        }

        var sample = new List<Track>(limit);
        for (var round = 0; sample.Count < limit; round++)
        {
            var placed = false;
            foreach (var run in byArtist)
            {
                if (round >= run.Count)
                {
                    continue;
                }

                sample.Add(run[round]);
                placed = true;
                if (sample.Count == limit)
                {
                    break;
                }
            }

            // Every run is exhausted — only reachable if the catalog is smaller than
            // the limit, which the early return already handles, but the loop should
            // not depend on that to terminate.
            if (!placed)
            {
                break;
            }
        }

        return sample;
    }
}
