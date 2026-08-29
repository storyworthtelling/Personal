using Score.Google;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Infrastructure.Storage;

namespace Score;

/// <summary>
/// Incremental library scan: list every audio file under the chosen Drive folder,
/// diff it against what the cache already knows (by modified-time + size), then only
/// re-read the tags of files that are new or changed and drop tracks whose file is
/// gone. The per-file tag reads are network-bound and independent, so they run in
/// bounded-parallel batches off the UI thread; each batch's tracks are written in one
/// serialized transaction (a single SQLite connection is not safe for concurrent
/// writes). Progress is reported as (done, total) over the work actually needed — the
/// files being read — so a rescan that changed little finishes fast.
/// </summary>
public sealed class MusicScanner
{
    // Bounds how many Drive tag reads run at once: enough to hide per-file latency without hammering the
    // Drive API quota or the mobile radio. Also the write-batch size, so a batch of reads lands in one txn.
    private const int MaxConcurrentReads = 8;

    private readonly DriveMusicClient driveClient;
    private readonly DeviceAccountTokenProvider tokens;
    private readonly MusicTagReader tagReader;
    private readonly Lazy<HiddenTrackPass> hiddenTrackPass;

    public MusicScanner(DriveMusicClient driveClient, DeviceAccountTokenProvider tokens, MusicTagReader tagReader)
    {
        this.driveClient = driveClient;
        this.tokens = tokens;
        this.tagReader = tagReader;
        hiddenTrackPass = new Lazy<HiddenTrackPass>(() => new HiddenTrackPass(tokens));
    }

    public async Task ScanAsync(
        string folderId, MusicCatalogCache cache, Action<int, int> onProgress, CancellationToken cancellationToken)
    {
        var files = await driveClient.ListAudioRecursiveAsync(folderId, cancellationToken);
        var filesById = files.ToDictionary(file => file.Id);
        var current = files.ToDictionary(
            file => file.Id, file => new MusicFileVersion(file.ModifiedTime.UtcTicks, file.Size));

        // A cache filled by an older reader is missing whatever this one learned to
        // read since; nothing about the files says so, so the scan is told directly.
        var rereadAll = cache.ReaderVersion < MusicTagReader.Version;
        var plan = MusicScanPlan.Compute(current, cache.LoadVersions(), rereadAll);

        // Drop tracks whose file disappeared from Drive (replaces the old clear-and-rebuild).
        foreach (var id in plan.ToDelete)
        {
            cache.Delete(id);
        }

        var toRead = plan.ToRead.Select(id => filesById[id]).ToList();
        var total = toRead.Count;
        onProgress(0, total);
        if (total == 0)
        {
            cache.ReaderVersion = MusicTagReader.Version;
            return; // nothing new or changed — the cached catalog already matches Drive
        }

        using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
        var done = 0;
        foreach (var batch in toRead.Chunk(MaxConcurrentReads))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read this batch's tags (and, for a long track, run the hidden-track pass) concurrently...
            var reads = batch.Select(file => ReadFileAsync(file, http, cancellationToken)).ToList();
            var results = await Task.WhenAll(reads);

            // A file being re-read here may have split differently than last time (or
            // not at all any more) — clear its old row(s), split or not, before
            // writing the fresh ones, but only once the read actually succeeded; a
            // failed read leaves whatever was cached for it alone.
            foreach (var (file, result) in batch.Zip(results))
            {
                if (result.Tracks.Count > 0)
                {
                    cache.Delete(file.Id);
                }
            }

            // ...then write them in one serialized transaction (SQLite single-writer).
            cache.Upsert(results
                .Where(result => result.Tracks.Count > 0)
                .SelectMany(result => result.Tracks.Select(track => (track, result.Version))));

            done += batch.Length;
            onProgress(done, total);
        }

        // Only once the pass finished. A cancelled scan has read part of the library,
        // and marking it done would leave the rest missing the new field for good.
        cache.ReaderVersion = MusicTagReader.Version;
    }

    // Empty Tracks means the file could not be read at all (skipped; the rest of the
    // library still scans). One Track is the ordinary case. More than one means the
    // hidden-track pass found — and split off — a bonus track.
    private async Task<(IReadOnlyList<Track> Tracks, MusicFileVersion Version)> ReadFileAsync(
        DriveAudioFile file, HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            Track track;
            using (var stream = new DriveSeekableStream(file.Id, file.Size, tokens, http))
            {
                track = await Task.Run(() => tagReader.Read(file.Id, file.Name, stream), cancellationToken);
            }

            var version = new MusicFileVersion(file.ModifiedTime.UtcTicks, file.Size);
            if (!hiddenTrackPass.Value.IsWorthScanning(track))
            {
                return ([track], version);
            }

            using var decodeStream = new DriveSeekableStream(file.Id, file.Size, tokens, http);
            var tracks = await hiddenTrackPass.Value.RunAsync(track, decodeStream, cancellationToken);
            return (tracks, version);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ([], default);
        }
    }
}
