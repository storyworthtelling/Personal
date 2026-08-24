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
    private const int MaxPicks = 20;

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
        var selection = taste.FridayPicksAsync(catalog, MaxPicks, CancellationToken.None).GetAwaiter().GetResult();
        if (selection.TrackIds.Count > 0)
        {
            store.Save(selection, DateTimeOffset.Now);
        }

        return Result.InvokeSuccess();
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
