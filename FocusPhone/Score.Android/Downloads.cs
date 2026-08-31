using Android.Content;
using AndroidX.Media3.Database;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.DataSource.Cache;
using AndroidX.Media3.ExoPlayer.Offline;
using Codex.Domain.Audio;
using Score.Google;

// AndroidX.Media3.ExoPlayer.Offline.DownloadManager, not Android.App's — both are in
// scope and the ambiguity is a compile error, not a warning.
using DownloadManager = AndroidX.Media3.ExoPlayer.Offline.DownloadManager;

namespace Score;

/// <summary>
/// The one place that owns the download cache, so playback and downloading cannot
/// disagree about where the files are.
///
/// The layering matters. <see cref="CacheDataSource"/> sits <b>above</b> the resolver
/// that turns <c>drive://{fileId}</c> into a signed Google URL, which is what makes
/// the cache key the stable <c>drive://</c> URI rather than a signed URL that differs
/// on every open — cache below the resolver and nothing would ever be found again.
/// Podcast http(s) URIs are already stable and pass through the same chain.
///
/// Eviction is <see cref="NoOpCacheEvictor"/> on purpose: nothing is thrown out to
/// make room. What leaves is decided by the ten-day sweep and by the owner, and a
/// cache quietly dropping the album they downloaded for a flight is exactly the
/// behaviour the budget exists to replace.
/// </summary>
public static class Downloads
{
    private const string CacheFolder = "downloads";
    private const int ParallelDownloads = 2;

    private static SimpleCache? cache;
    private static DownloadManager? manager;

    /// <summary>The download cache, opened once per process.</summary>
    public static SimpleCache Cache(Context context) =>
        cache ??= new SimpleCache(
            new Java.IO.File(context.FilesDir, CacheFolder),
            new NoOpCacheEvictor(),
            new StandaloneDatabaseProvider(context));

    /// <summary>
    /// The factory playback reads through: downloaded content plays from disk, and
    /// anything else streams as before.
    /// </summary>
    public static CacheDataSource.Factory ReadFactory(Context context, IDataSourceFactory upstream) =>
        new CacheDataSource.Factory()
            .SetCache(Cache(context))!
            .SetUpstreamDataSourceFactory(upstream)!
            // Read-only for playback: streaming a track must not quietly fill the
            // download budget with things nobody asked to keep.
            .SetCacheWriteDataSinkFactory(null)!;

    /// <summary>
    /// The manager the download service runs. It builds its own writing path over the
    /// same cache and the same upstream, which is why the budget can trust that what
    /// it counted is what playback will find.
    /// </summary>
    public static DownloadManager Manager(Context context)
    {
        return manager ??= new DownloadManager(
            context,
            new StandaloneDatabaseProvider(context),
            Cache(context),
            new ResolvingDataSource.Factory(
                new DefaultHttpDataSource.Factory(), new DriveMediaResolver(new DeviceAccountTokenProvider())),
            Java.Util.Concurrent.Executors.NewFixedThreadPool(ParallelDownloads)!);
    }

    /// <summary>
    /// Starts a download. The media id is the download id, so the budget ledger and
    /// the cache are talking about the same thing without a second mapping between
    /// them to fall out of step.
    /// </summary>
    public static void Add(Context context, string mediaId, string uri)
    {
        var request = new DownloadRequest.Builder(mediaId, Android.Net.Uri.Parse(uri)!).Build();
        DownloadService.SendAddDownload(
            context, Java.Lang.Class.FromType(typeof(ScoreDownloadService)), request, foreground: false);
    }

    public static void Remove(Context context, string mediaId) =>
        DownloadService.SendRemoveDownload(
            context, Java.Lang.Class.FromType(typeof(ScoreDownloadService)), mediaId, foreground: false);

    /// <summary>
    /// Takes everything past its ten days and returns what is left.
    ///
    /// Run on the way into the app rather than on a schedule: the sweep has nothing to
    /// do with what the app is doing, and a worker for it would be a worker that
    /// usually does nothing.
    /// </summary>
    public static DownloadBudget Sweep(Context context, DownloadStore store, DateTimeOffset now)
    {
        var budget = store.Load();
        var expired = budget.Expired(now);
        if (expired.Count == 0)
        {
            return budget;
        }

        foreach (var item in expired)
        {
            Remove(context, item.MediaId);
        }

        var swept = budget.Swept(now);
        store.Save(swept);
        return swept;
    }
}
