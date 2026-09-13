using global::Android.Content;
using AndroidX.Work;
using Score.Google;
using Codex.Domain.Music;

namespace Score;

/// <summary>
/// The weekly "New for you" trigger. Runs on WorkManager's daily schedule but only acts on Fridays, at most
/// once per ISO week (<see cref="FridayPicksSchedule"/> decides). When due it asks the Discovery engine for
/// a small set from the owner's own library and stashes the raw selection for the quiet shelf to show —
/// never a push or a badge. Best-effort: no account, no taste yet, or any failure just leaves last week's
/// shelf (or none) in place. Device-gated; the schedule/plan logic it leans on is unit-tested.
/// </summary>
[global::Android.Runtime.Register("com.storyworthtelling.focusphone.audio.FridayPicksWorker")]
public class FridayPicksWorker : Worker
{
    private const string WorkName = "focusphone-audio-friday-picks";

    public FridayPicksWorker(Context context, WorkerParameters parameters)
        : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        var context = ApplicationContext!;

        if (!MusicSettingsStore.ListeningGeniusEnabled || string.IsNullOrEmpty(MusicSettingsStore.GoogleAccountEmail))
        {
            return Result.InvokeSuccess();
        }

        var store = new FridayPicksStore(context);
        if (!FridayPicksSchedule.IsDue(DateTimeOffset.Now, store.GeneratedUtc))
        {
            return Result.InvokeSuccess();
        }

        IReadOnlyList<Track> catalog;
        using (var database = MusicDatabase.Open(context))
        {
            catalog = database.Catalog.LoadAll();
        }

        if (catalog.Count == 0)
        {
            return Result.InvokeSuccess();
        }

        var taste = new MusicTasteService(new DeviceAccountTokenProvider().GetAccessToken, "Score");

        // The week's strikes go in before the crate is replaced. This is the batching
        // that keeps a veto from being a rating: it lands once, on the stack's own
        // cycle, rather than the moment it is tapped.
        taste.FoldVetoesAsync(VetoedPicks(store), CancellationToken.None).GetAwaiter().GetResult();

        // What the workout ran short of, if anything. Read once and cleared, so a
        // tempo the owner tried twice does not stack up into a demand.
        var gap = MusicSettingsStore.TempoGap;
        MusicSettingsStore.TempoGap = null;

        // Followed shows are the podcast side's own exclusion list, the same role the track
        // catalog plays for songs and albums — without it a podcast pick could only ever
        // recommend a show already subscribed to.
        var subscribedShows = new SubscriptionStore(context).Load().Select(feed => feed.Title).ToList();

        // The catalog goes in as evidence and as an exclusion list, and what comes
        // back is records the owner does NOT have. It used to go in as the source, so
        // the crate could only ever offer to add something already on the shelf.
        var picks = taste.WeeklyDiscoveriesAsync(
            catalog, MusicSettingsStore.FridayStackSize, gap, subscribedShows, CancellationToken.None)
            .GetAwaiter().GetResult();
        if (picks.Count > 0)
        {
            store.Save(picks, DateTimeOffset.Now);
        }

        return Result.InvokeSuccess();
    }

    // The outgoing crate's struck records, named. They are read back out of the
    // crate itself rather than resolved against the library, because a struck record
    // is by definition one the owner does not have — there is nothing to resolve it
    // against, which is what the old lookup was quietly failing to do.
    private static IReadOnlyList<CratePick> VetoedPicks(FridayPicksStore store)
    {
        var struck = store.Vetoed.ToHashSet(StringComparer.Ordinal);
        if (struck.Count == 0)
        {
            return [];
        }

        return [.. store.LoadPicks()
            .Where(pick => struck.Contains(Discoveries.Key(pick)))
            .Select(pick => new CratePick(Discoveries.Key(pick), pick.Title, pick.Artist, Kind: pick.Kind))];
    }

    /// <summary>The daily backbone (network-connected); Keep so relaunches don't reset a pending run.</summary>
    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var request = new PeriodicWorkRequest.Builder(typeof(FridayPicksWorker), TimeSpan.FromHours(24))
            .SetConstraints(constraints)!
            .AddTag(WorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }
}
