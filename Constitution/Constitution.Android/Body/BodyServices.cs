using Android.Content;
using Codex.Ui.Voice;
using Constitution.Domain.Body;
using Constitution.Health;
using Constitution.Infrastructure.Body;
using Constitution.Infrastructure.SleepNumber;
using Constitution.Infrastructure.Vault;
using Constitution.Services;

namespace Constitution.Body;

/// <summary>
/// Builds Body's object graph on the device, once per caller: the worker builds one per run and the activity one per
/// open, which is why every store underneath re-reads its file rather than caching (see <see cref="JsonBodyStore"/>).
/// This is only wiring; the decisions are in Domain and Infrastructure and are tested there.
/// </summary>
public sealed class BodyServices
{
    private const string ReadingsFile = "body-readings.json";
    private const string SyncStatusFile = "body-sync.json";

    private static readonly TimeSpan MinimumGapBetweenOpenReads = TimeSpan.FromMinutes(2);
    private static long lastOpenReadTick;

    private BodyServices(IBodyStore store, IHealthRecordSource source, BodySync sync, BodySyncStatusFile status, HealthChoicesStore choices, BodyLog log)
    {
        Store = store;
        Source = source;
        Sync = sync;
        Status = status;
        Choices = choices;
        Log = log;
    }

    /// <summary>This person's readings.</summary>
    public IBodyStore Store { get; }

    /// <summary>Health Connect.</summary>
    public IHealthRecordSource Source { get; }

    /// <summary>When Body reads: the worker overnight, the app on open.</summary>
    public BodySync Sync { get; }

    /// <summary>The last sync, for Diagnostics.</summary>
    public BodySyncStatusFile Status { get; }

    /// <summary>Which signals this person has been asked for and has given.</summary>
    public HealthChoicesStore Choices { get; }

    /// <summary>The Body trail.</summary>
    public BodyLog Log { get; }

    /// <summary>Just the readings file's store, for a caller that needs nothing else of the graph.</summary>
    public static IBodyStore CreateStore(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var directory = appContext.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep Body's readings in.");
        return new JsonBodyStore(Path.Combine(directory, ReadingsFile));
    }

    /// <summary>The graph for this device.</summary>
    public static BodyServices Create(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var directory = appContext.NoBackupFilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no private files folder to keep Body's readings in.");
        var log = new BodyLog(appContext);
        var source = new HealthConnectSource(appContext, log);
        var store = CreateStore(appContext);
        var status = new BodySyncStatusFile(Path.Combine(directory, SyncStatusFile), () => DateTimeOffset.UtcNow);

        // The bed fills a night the watch missed, but only for someone who has said which side of it is theirs.
        var connections = new ConstitutionConnections(appContext);
        var bed = new SleepIqClient(
            SleepIqClient.CreateHttpClient(new Xamarin.Android.Net.AndroidMessageHandler(), new CredentialHealthStore()),
            connections,
            log);
        var filler = BedSleepGapFiller.Create(bed, new SleeperChoice(appContext), log, new BedLoginStanding(appContext));

        var reader = new SignalReader(store, source, () => DateOnly.FromDateTime(DateTime.Now), log, filler);
        var sync = new BodySync(reader, source, status, log);
        var choices = new HealthChoicesStore(connections, ConstitutionVaults.Create(appContext).PublishAsync, log);
        return new BodyServices(store, source, sync, status, choices, log);
    }

    /// <summary>
    /// The app opening: refresh whatever is granted, then bring the saved choices into line with what the platform
    /// says. Off the UI thread and never throws to it; a failure goes to the trail as its type name only.
    /// </summary>
    public static void RefreshAtOpen(Context context)
    {
        // The screen resumes whenever the owner comes back from another app; a read that finished moments ago is fresh enough.
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref lastOpenReadTick);
        if (last != 0 && now - last < MinimumGapBetweenOpenReads.TotalMilliseconds)
        {
            return;
        }

        Interlocked.Exchange(ref lastOpenReadTick, now);
        _ = Task.Run(async () =>
        {
            BodyServices? services = null;
            try
            {
                services = Create(context);
                var sync = await services.Sync.OpenTimeReadsAsync(CancellationToken.None);
                await services.Choices.ReconcileAsync(services.Source, CancellationToken.None);
                await BodyBackups.AfterOpenSyncAsync(context, sync, services.Store, CancellationToken.None);
            }
            catch (Exception e)
            {
                // Logged, not swallowed: the next open tries again, and the trail says why this one did not.
                (services?.Log ?? new BodyLog(context)).Outcome($"body open-time read failed: {e.GetType().Name}");
            }
        });
    }
}
