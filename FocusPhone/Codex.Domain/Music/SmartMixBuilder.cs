namespace Codex.Domain.Music;

/// <summary>
/// Builds a "more like this" mix over the user's OWN library, seeded from a track.
/// No cloud service: similarity is scored from shared tags — same artist is the
/// strongest signal, then same album artist, then same genre. Tracks are grouped
/// by score (most similar first) and shuffled within each tier so repeated mixes
/// from the same seed still feel fresh. The seed itself leads the mix.
/// </summary>
public class SmartMixBuilder
{
    private const int ArtistScore = 3;
    private const int AlbumArtistScore = 2;
    private const int GenreScore = 1;

    private static readonly StringComparer Name = StringComparer.OrdinalIgnoreCase;

    private readonly ShufflePlanner shuffle;

    public SmartMixBuilder(ShufflePlanner shuffle) => this.shuffle = shuffle;

    public IReadOnlyList<Track> Build(Track seed, MusicCatalog catalog, int limit = 50)
    {
        var body = catalog.Songs()
            .Where(track => track.Id != seed.Id)
            .Select(track => (Track: track, Score: Similarity(seed, track)))
            .Where(scored => scored.Score > 0)
            .GroupBy(scored => scored.Score)
            .OrderByDescending(tier => tier.Key)
            .SelectMany(tier => shuffle.Shuffle(tier.Select(scored => scored.Track)))
            .Take(limit);

        return [seed, .. body];
    }

    private static int Similarity(Track seed, Track other)
    {
        var score = 0;
        if (Name.Equals(seed.DisplayArtist, other.DisplayArtist))
        {
            score += ArtistScore;
        }
        else if (Name.Equals(seed.DisplayAlbumArtist, other.DisplayAlbumArtist))
        {
            score += AlbumArtistScore;
        }

        if (Name.Equals(seed.DisplayGenre, other.DisplayGenre))
        {
            score += GenreScore;
        }

        return score;
    }
}
