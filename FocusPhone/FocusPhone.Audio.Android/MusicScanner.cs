using FocusPhone.Audio.Google;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Codex.Infrastructure.Storage;

namespace FocusPhone.Audio;

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

    public MusicScanner(DriveMusicClient driveClient, DeviceAccountTokenProvider tokens, MusicTagReader tagReader)
    {
        this.driveClient = driveClient;
        this.tokens = tokens;
        this.tagReader = tagReader;
    }

    public async Task ScanAsync(
        string folderId, MusicCatalogCache cache, Action<int, int> onProgress, CancellationToken cancellationToken)
    {
        var files = await driveClient.ListAudioRecursiveAsync(folderId, cancellationToken);
        var filesById = files.ToDictionary(file => file.Id);
        var current = files.ToDictionary(
            file => file.Id, file => new MusicFileVersion(file.ModifiedTime.UtcTicks, file.Size));

        var plan = MusicScanPlan.Compute(current, cache.LoadVersions());

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
            return; // nothing new or changed — the cached catalog already matches Drive
        }

        using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
        var done = 0;
        foreach (var batch in toRead.Chunk(MaxConcurrentReads))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read this batch's tags concurrently (blocking TagLib/Drive work offloaded to the pool)...
            var reads = batch.Select(file => Task.Run(() => ReadTags(file, http), cancellationToken));
            var results = await Task.WhenAll(reads);

            // ...then write them in one serialized transaction (SQLite single-writer).
            cache.Upsert(results.Where(entry => entry is not null).Select(entry => entry!.Value));

            done += batch.Length;
            onProgress(done, total);
        }
    }

    private (Track Track, MusicFileVersion Version)? ReadTags(DriveAudioFile file, HttpClient http)
    {
        try
        {
            using var stream = new DriveSeekableStream(file.Id, file.Size, tokens, http);
            var track = tagReader.Read(file.Id, file.Name, stream);
            return (track, new MusicFileVersion(file.ModifiedTime.UtcTicks, file.Size));
        }
        catch
        {
            // Skip a file we can't read; the rest of the library still scans.
            return null;
        }
    }
}
