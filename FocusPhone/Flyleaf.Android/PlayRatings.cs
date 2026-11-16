using Android.Content;
using Android.Content.PM;
using Codex.Domain.Apps;
using Codex.Infrastructure.Apps;
using Codex.Ui;
using Codex.Ui.Http;

namespace Flyleaf;

/// <summary>
/// Keeps the installed apps' Google Play ratings on the phone (<c>play-ratings.json</c>) so the policy
/// pass can hard-suspend anything rated M or above (<see cref="Codex.Domain.Policy.HardBlock"/>) without
/// touching the network. Every decision is in <see cref="RatingRefresh"/> and <see cref="RatingBook"/>;
/// this only reads the installed versions, fetches the due listings and saves the book. A run that finds
/// the mature set changed re-applies the policy at once.
/// </summary>
public static class PlayRatings
{
    private const string Tag = "FlyleafRatings";
    private const string FileName = "play-ratings.json";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object FileLock = new();

    public static RatingBook Load(Context context)
    {
        var path = PathIn(context);
        lock (FileLock)
        {
            return RatingBookCodec.Decode(File.Exists(path) ? File.ReadAllText(path) : null);
        }
    }

    /// <summary>Starts a refresh without waiting for it; an install change or app start calls this.</summary>
    public static void RefreshInBackground(Context context) =>
        Task.Run(async () =>
        {
            try
            {
                await RefreshAsync(context);
            }
            catch (Exception e)
            {
                HeadLog.Warn(Tag, "The rating refresh failed", e);
            }
        });

    public static async Task RefreshAsync(Context context)
    {
        // One run at a time; a second caller has nothing to add while one is already looking.
        if (!await Gate.WaitAsync(0))
        {
            return;
        }
        try
        {
            var installed = InstalledVersions(context);
            var before = Load(context).RetainOnly(installed.Select(app => app.Package));
            var due = RatingRefresh.Due(installed, before, DateTimeOffset.UtcNow);
            if (due.Count == 0)
            {
                return;
            }

            var client = new PlayListingClient(HeadHttp.WithTimeout(TimeSpan.FromSeconds(30)));
            var versions = installed.ToDictionary(app => app.Package, app => app.VersionCode);
            var book = before;
            var tally = new Dictionary<RatingLookupOutcome, int>();
            foreach (var package in due)
            {
                PlayLookup lookup;
                try
                {
                    lookup = await client.LookupAsync(package);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    lookup = new PlayLookup(RatingLookupOutcome.Failed, ContentRating.Unknown);
                }
                book = book.Record(package, versions[package], lookup, DateTimeOffset.UtcNow);
                tally[lookup.Outcome] = tally.GetValueOrDefault(lookup.Outcome) + 1;
            }
            Save(context, book);

            // Counts only, never package names: the log leaves the phone.
            var mature = book.MaturePackages();
            var line = $"Looked up {due.Count} Play ratings: " + string.Join(", ", tally.Select(t => $"{t.Value} {t.Key}"))
                + $"; {mature.Count} installed app(s) rated M or above.";
            if (tally.ContainsKey(RatingLookupOutcome.Unreadable))
            {
                HeadLog.Warn(Tag, line + " An unreadable listing may mean Play changed its page.");
            }
            else
            {
                HeadLog.Info(Tag, line);
            }

            if (!mature.SetEquals(before.MaturePackages()))
            {
                PolicyManager.ApplyCurrentPolicy(context);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static List<(string Package, long VersionCode)> InstalledVersions(Context context)
    {
        var apps = new List<(string, long)>();
        foreach (var package in PolicyManager.QueryLaunchablePackages(context))
        {
            try
            {
                apps.Add((package, context.PackageManager!.GetPackageInfo(package, (PackageInfoFlags)0)!.LongVersionCode));
            }
            catch (PackageManager.NameNotFoundException)
            {
                // Removed between the query and the read; the next run sees the new set.
            }
        }
        return apps;
    }

    private static void Save(Context context, RatingBook book)
    {
        var path = PathIn(context);
        lock (FileLock)
        {
            File.WriteAllText(path + ".tmp", RatingBookCodec.Encode(book));
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }

    private static string PathIn(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, FileName);
}
