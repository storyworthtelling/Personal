using System.Diagnostics;
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
///
/// Availability and discovery come from the same single Drive listing rather than two
/// passes: a file missing from it is dropped (<see cref="MusicScanPlan.ToDelete"/>)
/// before a single tag is read, and a file new to it joins the read queue
/// (<see cref="MusicScanPlan.ToRead"/>) alongside anything changed — so a library with
/// thousands of files gets both answers together rather than one waiting on the other.
///
/// A library too large to finish its tag reads inside one WorkManager execution window
/// is not a failure: <see cref="ScanAsync"/> takes a time budget and yields before the
/// system would kill it, having already committed every batch it read, and reports
/// <c>false</c> so the caller knows to run again. Because the plan is computed fresh
/// from the cache each time, the next run picks up exactly the files still left rather
/// than starting over.
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

    /// <returns>
    /// Whether this run drained the whole plan (or there was nothing to do), and what it
    /// actually changed while doing so — see <see cref="MusicScanRunResult"/>. A partial
    /// run has already committed each batch it managed, so the caller
    /// (<see cref="MusicScanWorker"/>) just needs to run again — the plan recomputed
    /// against the now-larger cache picks up only what's still left, and its own
    /// <see cref="MusicScanRunResult.Outcome"/> only ever describes what THIS run did.
    /// </returns>
    public async Task<MusicScanRunResult> ScanAsync(
        string folderId, MusicCatalogCache cache, Action<int, int> onProgress, CancellationToken cancellationToken,
        TimeSpan? timeBudget = null)
    {
        var files = await driveClient.ListAudioRecursiveAsync(folderId, cancellationToken);
        var filesById = files.ToDictionary(file => file.Id);
        var current = files.ToDictionary(
            file => file.Id, file => new MusicFileVersion(file.ModifiedTime.UtcTicks, file.Size));

        // A cache filled by an older reader is missing whatever this one learned to
        // read since; nothing about the files says so, so the scan is told directly.
        var rereadAll = cache.ReaderVersion < MusicTagReader.Version;
        var plan = MusicScanPlan.Compute(current, cache.LoadVersions(), rereadAll);

        // Named apart from ToRead so a successfully-processed id can be classified back
        // into "added" or "changed" for the RescanOutcome the greeting reads.
        var addedIds = plan.ToAdd.ToHashSet(StringComparer.Ordinal);
        var updatedIds = plan.ToUpdate.ToHashSet(StringComparer.Ordinal);
        var removed = plan.ToDelete.Count;

        // Availability first, and unconditionally: a file gone from Drive is dropped
        // whether or not there's time left to read anything new this run (replaces the
        // old clear-and-rebuild).
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
            // nothing new or changed — the cached catalog already matches Drive
            return new MusicScanRunResult(true, new RescanOutcome(0, 0, removed));
        }

        using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
        var elapsed = timeBudget is null ? null : Stopwatch.StartNew();
        var done = 0;
        var added = 0;
        var changed = 0;
        var fullyDrained = true;
        foreach (var batch in toRead.Chunk(MaxConcurrentReads))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (elapsed is not null && elapsed.Elapsed >= timeBudget!.Value)
            {
                // Out of time for this run, not out of files. Everything up to here is
                // already committed; stop short of the system doing it for us.
                fullyDrained = false;
                break;
            }

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

            // Only a file that actually succeeded counts toward the outcome — a read
            // exhausted after every retry (see ReadFileAsync) left the cache untouched
            // for it, so it is neither newly added nor actually changed this run.
            foreach (var (file, result) in batch.Zip(results))
            {
                if (result.Tracks.Count == 0)
                {
                    continue;
                }

                if (addedIds.Contains(file.Id))
                {
                    added++;
                }
                else if (updatedIds.Contains(file.Id))
                {
                    changed++;
                }
            }

            done += batch.Length;
            onProgress(done, total);
        }

        if (!fullyDrained)
        {
            return new MusicScanRunResult(false, new RescanOutcome(added, changed, removed));
        }

        // A song can end up duplicated across multiple physical files — repeated
        // downloads, an upgrade from MP3 to FLAC left sitting next to the old copy.
        // Runs over the WHOLE current catalog, not just what this pass touched, since
        // a duplicate can be introduced by any new file matching any track already
        // cached, not only ones against each other within the same batch.
        foreach (var id in DuplicateTracks.FindLosingIds(cache.LoadAll()))
        {
            cache.Delete(id);
        }

        // Only once the pass finished. A partial scan has read part of the library, and
        // marking it done would leave the rest missing the new field for good.
        cache.ReaderVersion = MusicTagReader.Version;
        return new MusicScanRunResult(true, new RescanOutcome(added, changed, removed));
    }

    // How many times a file's read is attempted before it is given up on for this run.
    // MusicScanPlan retries it again on the next scan regardless — this only bounds how often a
    // single transient hiccup (Drive rate-limiting, a timeout, a brief auth blip — all expected
    // and normal under MaxConcurrentReads-wide concurrency across a from-scratch scan of
    // thousands of files) costs the file its place in THIS run.
    private const int ReadAttempts = 2;

    // Empty Tracks means the file could not be read at all after every retry (skipped; the rest
    // of the library still scans). One Track is the ordinary case. More than one means the
    // hidden-track pass found — and split off — a bonus track.
    //
    // Every failed attempt used to be swallowed silently with no log and no retry — with 6400+
    // files read in bounded-parallel batches, that meant a normal transient failure vanished
    // with no trace, and which handful of files happened to be unlucky was effectively random
    // per run: a full rescan of unchanged Drive content landed a different subset every time.
    private async Task<(IReadOnlyList<Track> Tracks, MusicFileVersion Version)> ReadFileAsync(
        DriveAudioFile file, HttpClient http, CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= ReadAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }

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
                lastFailure = e;
            }
        }

        AudioDiagnostics.LogWarning(
            "MusicScanner", $"Could not read {file.Name} (id={file.Id}) after {ReadAttempts} attempts.", lastFailure);
        return ([], default);
    }
}
