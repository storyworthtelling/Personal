namespace Codex.Domain.Music;

/// <summary>
/// The completions/skips/replays and album-vs-shuffle lean for one grouping key (a genre, artist,
/// or album artist), plus how many distinct listening sittings touched it. <see cref="Sessions"/>
/// is the load-bearing number — distinct sittings, not raw plays — so one repeat-all afternoon
/// can't outweigh a genre returned to across many days.
/// </summary>
public sealed record AffinityStat(
    string Key,
    int Sessions,
    int Completions,
    int Skips,
    int Replays,
    int AlbumPlays,
    int ShufflePlays);

/// <summary>
/// The reduced evidence the Claude engine turns into a narrative <see cref="TasteProfile"/>: ranked
/// affinities by genre / artist / album artist, a 24-bucket time-of-day histogram, and the distinct
/// session count. Pure aggregate — no model, no I/O.
/// </summary>
public sealed record TasteEvidence(
    IReadOnlyList<AffinityStat> Genres,
    IReadOnlyList<AffinityStat> Artists,
    IReadOnlyList<AffinityStat> AlbumArtists,
    IReadOnlyList<int> HourHistogram,
    int SessionsAnalyzed)
{
    public static TasteEvidence Empty { get; } = new([], [], [], new int[24], 0);

    public bool IsEmpty => SessionsAnalyzed <= 0;
}
