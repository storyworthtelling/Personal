namespace FocusPhone.Domain.Music;

/// <summary>The thing being shuffled — never a single global toggle.</summary>
public enum ShuffleScope
{
    Album,
    Playlist,
    Artist,
    Genre,
}

/// <summary>
/// Produces a shuffled play order for a set of tracks. The caller decides the
/// scope (an album's, a playlist's, an artist's whole discography, or a genre)
/// and hands the gathered tracks here. A supplied <see cref="Random"/> makes the
/// order deterministic under test.
/// </summary>
public class ShufflePlanner
{
    private readonly Random random;

    public ShufflePlanner(Random? random = null) => this.random = random ?? new Random();

    public IReadOnlyList<Track> Shuffle(IEnumerable<Track> tracks)
    {
        var shuffled = tracks.ToList();
        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled;
    }
}
