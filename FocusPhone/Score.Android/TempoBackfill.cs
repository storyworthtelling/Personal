using Android.Content;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Infrastructure.Storage;

namespace Score;

/// <summary>
/// Fills in the tempos the files themselves do not carry.
///
/// Most consumer audio has no BPM tag — the tag is the cheap half of the answer and
/// it covers a fraction of a real library, which would leave the workout pool empty
/// however good the matching is. This walks the tracks nobody has measured and asks a
/// public catalogue, writing each answer back to the same cache column the tags fill,
/// so the pool cannot tell where a number came from.
///
/// Resumable by construction: a track only gets looked up while its tempo is still
/// zero, so a run that is interrupted picks up exactly where it stopped. Writes land
/// in batches rather than one at a time, because the cache is a single SQLite
/// connection and a batch is one transaction.
/// </summary>
public sealed class TempoBackfill
{
    // Small enough to stay a polite guest of a free service, and to keep a partial
    // run's work — each batch is committed before the next starts.
    private const int Batch = 10;

    private readonly Context context;
    private readonly ITempoSource source;

    public TempoBackfill(Context context, ITempoSource? source = null)
    {
        this.context = context;
        this.source = source ?? new DeezerTempoClient(
            new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler()));
    }

    /// <summary>
    /// Looks up every track with no tempo, reporting (done, total) as it goes.
    /// Returns how many it actually found — most libraries have a long tail nobody
    /// has measured anywhere, and saying so is better than implying it failed.
    /// </summary>
    public async Task<int> RunAsync(
        IReadOnlyList<Track> catalog, Action<int, int> onProgress, CancellationToken cancellationToken)
    {
        var unknown = catalog.Where(track => track.Bpm <= 0).ToList();
        var total = unknown.Count;
        onProgress(0, total);

        var found = 0;
        var done = 0;
        foreach (var chunk in unknown.Chunk(Batch))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var measured = new List<Track>();
            foreach (var track in chunk)
            {
                var bpm = await source.LookupAsync(track.DisplayArtist, track.DisplayTitle, cancellationToken);
                if (bpm > 0)
                {
                    measured.Add(track with { Bpm = bpm });
                }
            }

            if (measured.Count > 0)
            {
                using var database = MusicDatabase.Open(context);
                database.Catalog.Upsert(measured);
                found += measured.Count;
            }

            done += chunk.Length;
            onProgress(done, total);
        }

        return found;
    }
}
