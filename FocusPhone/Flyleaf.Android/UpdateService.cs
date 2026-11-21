using Codex.Domain.Updates;
using Codex.Domain.Credentials;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Util;
using AndroidX.Work;
using Storyworthtelling.Updates;
using Codex.Domain.Policy;
using Codex.Infrastructure.Updates;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Health;
using Codex.Ui;
using Codex.Ui.Http;

namespace Flyleaf;

/// <summary>
/// The device-owner update channel: a periodic worker polls this repo's latest
/// GitHub Release and silent-installs the owner's own signed APKs from it. It is
/// per-app — every com.storyworthtelling.* APK on the release is installed when
/// its versionCode is newer than what's on the device, or when the package isn't
/// installed at all, so a brand-new sibling app lands on its first CI build
/// without the 72-hour admin wait. Downloads are deduplicated by GitHub asset id,
/// so an unchanged APK is never re-fetched (data-frugal on a phone); the
/// admin-mode "Install from GitHub now" action passes force to re-check every
/// asset. Every install session re-whitelists the hard-restricted call-log/SMS
/// permissions so the Communication app keeps that access across updates.
///
/// Phone APKs only (P14 W-3.1.1). A watch app carries its phone app's package and a
/// higher versionCode band, so the release's Dash would otherwise be Post's newest
/// build. Each asset is judged as itself by <see cref="SiblingUpdatePlan"/>, form
/// first: its sidecar's form and its name must agree before anything is downloaded,
/// and once downloaded the APK's own manifest has the last word. A watch APK is
/// settled as NotForThisDevice and never fetched again; ApkInstaller refuses one
/// whatever this decided.
/// </summary>
public static class UpdateService
{
    private const string Tag = "FocusPhoneUpdate";  // not copy: a stage, id or wire word

    // Flyleaf is a phone app: it installs phone APKs, never a watch app that shares a package with one.
    private const ApkForm ThisDevice = ApkForm.Phone;
    private const string WorkName = "focusphone-update-check";
    public const string Repo = "storyworthtelling/Personal";
    private static readonly UpdateRunGate RunGate = new();
    private static readonly UpdateRunGate WatchGate = new();

    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build();
        var request = new PeriodicWorkRequest.Builder(typeof(UpdateWorker), TimeSpan.FromHours(6))
            .SetConstraints(constraints)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(
            WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    // The periodic worker's entry point: only newly-changed release assets.
    public static Task<UpdateRunResult> CheckAndInstallAsync(Context context) =>
        CheckAndInstallAsync(context, force: false);

    /// <param name="force">
    /// true from the admin "Install from GitHub now" action: ignore the
    /// download-dedup cache and re-evaluate every asset on the release (a manual
    /// retry). The periodic worker passes false so it only fetches changes.
    /// </param>
    /// <returns>
    /// What actually happened this run, so the worker and the admin action can
    /// report it (installed / already current / failed) rather than assume a
    /// committed session succeeded. The last run's summary is also persisted for
    /// the settings panel to show.
    /// </returns>
    public static async Task<UpdateRunResult> CheckAndInstallAsync(Context context, bool force)
    {
        // The worker and the manual action can overlap; one run at a time keeps them off each
        // other's cache files and install sessions.
        var result = await RunGate.RunAsync(() => RunAsync(context, force));
        Report(context, result);
        return result;
    }

    /// <summary>
    /// The watch half of the run (P14 W-3.1.3): called once the phone's own run has finished and been
    /// recorded, so nothing it does can break or hold back the phone's installs or their line. Its own
    /// gate keeps two of it apart; every failure inside it is logged there and goes no further.
    /// </summary>
    public static Task SendToWatchAsync(Context context, bool force) =>
        WatchGate.RunAsync(async () =>
        {
            await WatchUpdates.RunQuietlyAsync(context, force);
            return true;
        });

    private static void Report(Context context, UpdateRunResult result)
    {
        new UpdateStatusStore(context).Record(result.Summarize());
        // The run's own sentence too, so a run with no per-app lines (no token, not device owner) still leaves one (audit 08 §16).
        if (result.AnyFailed || result.State is UpdateRunState.FetchFailed)
        {
            HeadDiagnostics.Current?.Warn("Update", result.Summarize());
        }
        else
        {
            HeadDiagnostics.Current?.Info("Update", result.Summarize());
        }
        // One line per app (LOG-LOOP-PLAN.md §4.5), not just the summary the settings panel shows.
        foreach (var line in result.Lines())
        {
            if (result.AnyFailed || result.State is UpdateRunState.FetchFailed)
            {
                HeadDiagnostics.Current?.Warn("Update", line);
            }
            else
            {
                HeadDiagnostics.Current?.Info("Update", line);
            }
        }
    }

    private static async Task<UpdateRunResult> RunAsync(Context context, bool force)
    {
        var token = new UpdateSettingsStore(context).GitHubToken;
        if (string.IsNullOrEmpty(token))
        {
            HeadLog.Info(Tag, "No update token configured; skipping check.");
            return UpdateRunResult.NoToken();
        }
        if (!PolicyManager.IsDeviceOwner(context))
        {
            HeadLog.Info(Tag, "Not device owner; silent install unavailable, skipping check.");
            return UpdateRunResult.NotDeviceOwner();
        }

        var http = HeadHttp.Shared;
        var client = new GitHubReleaseClient(http, Repo, token);
        var health = HeadCredentialHealth.Store();
        ReleaseInfo release;
        try
        {
            release = await client.GetLatestReleaseAsync();
            // A successful release fetch clears any prior update-token failure.
            health.MarkOk(CredentialIds.GitHubUpdateToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } status &&
            AuthFailure.IsHttpCredentialRejection((int)status, null))
        {
            // Only a rejected token (401/403) flags the credential; a transient failure retries silently.
            health.MarkFailing(CredentialIds.GitHubUpdateToken, $"{(int)ex.StatusCode} {ex.StatusCode}");
            HeadLog.Warn(Tag, $"The update token was rejected: {(int)ex.StatusCode} {ex.StatusCode}");
            return UpdateRunResult.FetchFailed($"{(int)ex.StatusCode} {ex.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            // Transient (network/5xx): report it so the worker retries, without flagging the token.
            HeadLog.Warn(Tag, $"Release fetch failed: {ex.Message}");
            return UpdateRunResult.FetchFailed(ex.Message);
        }

        var processed = new ProcessedAssetsStore(context);
        // Keep the dedup cache from growing without bound: forget assets that are
        // no longer on the release.
        processed.RetainOnly(release.ApkAssets.Select(a => a.Id));

        var pending = release.ApkAssets
            .Where(asset => force || !processed.Contains(asset.Id))
            .ToList();
        if (pending.Count == 0)
        {
            HeadLog.Info(Tag, "No new release assets to consider.");
            return UpdateRunResult.Ran([]);
        }

        // Each APK's sidecar names its package, version and form, so an APK this phone already has, or one built for
        // a watch, is not downloaded at all (efficiency audit §13). One with no readable sidecar is downloaded and read.
        var described = new Dictionary<long, InstallCandidate>();
        foreach (var asset in pending)
        {
            try
            {
                if (await client.ReadSidecarAsync(release, asset) is { } candidate)
                {
                    // The sidecar and the asset's name must say the same form, or it is neither.
                    described[asset.Id] = candidate with
                    {
                        Form = ApkForms.Agree(candidate.Form, ApkForms.FromAssetName(asset.Name)),
                    };
                }
            }
            catch (HttpRequestException e)
            {
                HeadLog.Info(Tag, $"The sidecar for {asset.Name} did not read ({e.GetType().Name}); downloading the APK to read it.");
            }
        }
        // Flyleaf is the custodian on this device: as the device owner it is the only
        // thing that can install silently, so it installs for the whole suite. Each
        // asset is decided as itself: a package being due never carries its watch APK along.
        var beforeDownload = SiblingUpdatePlan
            .Decide(UpdateRole.Custodian, PolicyContext.LauncherPackage, ThisDevice,
                described.Select(pair => new ReleaseApk(pair.Key, pair.Value)).ToList(),
                InstalledVersionCodes(context, described.Values.Select(c => c.Package)))
            .ToDictionary(decision => decision.Apk.AssetId);

        // Download each APK still wanted to the cache and read its package, version and form from the APK itself.
        var downloaded = new List<(ReleaseAsset Asset, string Path, InstallCandidate Candidate)>();
        var results = new List<AppInstallResult>();
        foreach (var asset in pending)
        {
            if (beforeDownload.TryGetValue(asset.Id, out var known) && known.Settled is { } settled)
            {
                results.Add(Settle(known.Apk.Candidate, settled));
                if (InstallOutcomes.ShouldMarkProcessed(settled))
                {
                    processed.Add(asset.Id);
                }
                continue;
            }

            var path = System.IO.Path.Combine(context.CacheDir!.AbsolutePath!, $"update-{asset.Id}.apk");
            try
            {
                await DownloadAsync(http, asset, token, path);
            }
            catch (Exception e)
            {
                // A failed download isn't remembered, so it's retried next cycle.
                HeadLog.Warn(Tag, $"Downloading {asset.Name} failed: {e.Message}");
                results.Add(new AppInstallResult(asset.Name, 0, InstallOutcome.Failed, e.Message));
                DeleteQuietly(path);
                continue;
            }

            if (ApkArchive.Read(context, path) is not { } read)
            {
                HeadLog.Warn(Tag, $"{asset.Name} is not a readable APK; skipping.");
                DeleteQuietly(path);
                processed.Add(asset.Id); // don't keep re-downloading a bad asset
                results.Add(new AppInstallResult(asset.Name, 0, InstallOutcome.Unreadable));
                continue;
            }

            // The APK's own manifest has the last word, and its name and sidecar (if any) must agree with it: a
            // sidecar that called a watch APK a phone APK leaves it of no form, and installed nowhere.
            var form = ApkForms.Agree(
                read.Form,
                ApkForms.FromAssetName(asset.Name),
                described.TryGetValue(asset.Id, out var said) ? said.Form : read.Form);
            if (form != read.Form)
            {
                HeadLog.Warn(Tag, $"{asset.Name} reads as {ApkForms.Word(read.Form)} but its name or sidecar says otherwise; not installing it.");
            }
            downloaded.Add((asset, path, read with { Form = form }));
        }

        var afterDownload = SiblingUpdatePlan
            .Decide(UpdateRole.Custodian, PolicyContext.LauncherPackage, ThisDevice,
                downloaded.Select(d => new ReleaseApk(d.Asset.Id, d.Candidate)).ToList(),
                InstalledVersionCodes(context, downloaded.Select(d => d.Candidate.Package)))
            .ToDictionary(decision => decision.Apk.AssetId);

        foreach (var item in downloaded)
        {
            var outcome = await InstallOneAsync(context, item, afterDownload[item.Asset.Id]);
            results.Add(outcome);
            // Remember every settled outcome so an unchanged asset isn't re-fetched;
            // a failed install is left unprocessed so the next poll retries it.
            if (InstallOutcomes.ShouldMarkProcessed(outcome.Outcome))
            {
                processed.Add(item.Asset.Id);
            }
            DeleteQuietly(item.Path);
        }

        return UpdateRunResult.Ran(results);
    }

    private static async Task<AppInstallResult> InstallOneAsync(
        Context context,
        (ReleaseAsset Asset, string Path, InstallCandidate Candidate) item,
        ApkDecision decision)
    {
        if (decision.Settled is { } settled)
        {
            return Settle(item.Candidate, settled);
        }

        try
        {
            HeadLog.Info(Tag, $"Installing {item.Candidate.Package} v{item.Candidate.VersionCode} from {item.Asset.Name}.");
            using var apk = System.IO.File.OpenRead(item.Path);
            await ApkInstaller.InstallAsync(context, apk);
            return new AppInstallResult(item.Candidate.Package, item.Candidate.VersionCode, InstallOutcome.Installed);
        }
        catch (Exception e)
        {
            HeadLog.Warn(Tag, $"Installing {item.Candidate.Package} failed: {e.Message}");
            return new AppInstallResult(item.Candidate.Package, item.Candidate.VersionCode, InstallOutcome.Failed, e.Message);
        }
    }

    // An APK not installed, and why; a watch APK's line names its form.
    private static AppInstallResult Settle(InstallCandidate candidate, InstallOutcome settled) =>
        new(candidate.Package, candidate.VersionCode, settled,
            settled == InstallOutcome.NotForThisDevice ? $"form {ApkForms.Word(candidate.Form)}" : null);

    /// <summary>One release asset to a file, with the update token; the watch half downloads its APKs the same way.</summary>
    internal static async Task DownloadAsync(HttpClient http, ReleaseAsset asset, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Downloading {asset.Name} failed with {(int)response.StatusCode} {response.StatusCode}.");
        }
        using var output = System.IO.File.Create(path);
        await response.Content.CopyToAsync(output);
    }

    private static Dictionary<string, long> InstalledVersionCodes(Context context, IEnumerable<string> packages)
    {
        var result = new Dictionary<string, long>();
        foreach (var package in packages.Distinct())
        {
            try
            {
                var info = context.PackageManager!.GetPackageInfo(package, (PackageInfoFlags)0);
                result[package] = info!.LongVersionCode;
            }
            catch (PackageManager.NameNotFoundException)
            {
                // Not installed: UpdateScope reads the absence as "install".
            }
        }
        return result;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception e)
        {
            HeadLog.Warn(Tag, $"Could not delete temp APK {path}: {e.Message}");
        }
    }
}

public class UpdateWorker : Worker
{
    public UpdateWorker(Context context, WorkerParameters workerParams) : base(context, workerParams)
    {
    }

    // A run stopped before it returned leaves a line, not a lone "started" (audit 08 §20).
    public override void OnStopped() => HeadDiagnostics.Stopped(nameof(UpdateWorker), StopReason);

    public override Result DoWork() => HeadDiagnostics.Worker(nameof(UpdateWorker), Check);

    private Result Check()
    {
        try
        {
            var result = UpdateService.CheckAndInstallAsync(ApplicationContext).GetAwaiter().GetResult();
            // The same six-hourly, network-gated run re-reads due Play ratings (new versions, month-old
            // reads, earlier failures), so an app re-rated M is caught without its own worker.
            RefreshRatings();
            // Then the watch's turn (P14 W-3.1.3), last and on its own: it logs its own outcome and
            // throws nothing, so the phone's result below is the run's result whatever the watch did.
            UpdateService.SendToWatchAsync(ApplicationContext, force: false).GetAwaiter().GetResult();
            // Retry a transient fetch failure so recovery isn't gated on the full 6-hour
            // cadence; a failed install is already left unprocessed and retries next cycle.
            return result.State == UpdateRunState.FetchFailed
                ? Result.InvokeRetry()
                : Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            HeadLog.Warn("FocusPhoneUpdate", $"Update check failed; will retry on next period: {e}");
            return Result.InvokeRetry();
        }
    }

    private void RefreshRatings()
    {
        try
        {
            PlayRatings.RefreshAsync(ApplicationContext).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            HeadLog.Warn("FlyleafRatings", "The rating refresh failed; it runs again next period", e);
        }
    }
}

public class UpdateSettingsStore
{
    private const string PreferencesName = "updateSettings";
    private const string TokenKey = "gitHubToken";

    private readonly ISharedPreferences preferences;

    public UpdateSettingsStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public string? GitHubToken
    {
        get => preferences.GetString(TokenKey, null);
        set => preferences.Edit()!.PutString(TokenKey, value)!.Apply();
    }
}

/// <summary>
/// Remembers which GitHub release-asset ids the update channel has already
/// downloaded, so an unchanged APK is not re-fetched on every 6-hour poll. A
/// fresh CI upload re-mints the asset id, which drops out of this set and gets
/// picked up; RetainOnly forgets ids no longer on the release so the set can't
/// grow forever.
/// </summary>
public class ProcessedAssetsStore
{
    private const string PreferencesName = "updateProcessedAssets";
    private const string Key = "assetIds";

    private readonly ISharedPreferences preferences;

    public ProcessedAssetsStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public bool Contains(long assetId) => Load().Contains(assetId.ToString());

    public void Add(long assetId)
    {
        var ids = Load();
        if (ids.Add(assetId.ToString()))
        {
            Save(ids);
        }
    }

    public void RetainOnly(IEnumerable<long> currentAssetIds)
    {
        var keep = currentAssetIds.Select(id => id.ToString()).ToHashSet();
        var ids = Load();
        if (ids.RemoveWhere(existing => !keep.Contains(existing)) > 0)
        {
            Save(ids);
        }
    }

    private HashSet<string> Load() => preferences.GetStringSet(Key, [])!.ToHashSet();

    private void Save(HashSet<string> ids) =>
        preferences.Edit()!.PutStringSet(Key, ids)!.Apply();
}

/// <summary>
/// Persists the last update run's time and outcome so the settings panel can show
/// whether the channel is looking, when it last ran, and what happened — making a
/// silent failure (a rejected token, a signature-mismatched install) visible
/// instead of a mystery.
/// </summary>
public class UpdateStatusStore
{
    private const string PreferencesName = "updateStatus";
    private const string SummaryKey = "lastSummary";
    private const string TimeKey = "lastRunUtcTicks";

    private readonly ISharedPreferences preferences;

    public UpdateStatusStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public void Record(string summary) =>
        preferences.Edit()!
            .PutString(SummaryKey, summary)!
            .PutLong(TimeKey, DateTime.UtcNow.Ticks)!
            .Apply();

    /// <summary>The last outcome sentence, or null if the channel has never run on this device.</summary>
    public string? LastSummary => preferences.GetString(SummaryKey, null);

    /// <summary>When the last run finished (UTC), or null if it has never run.</summary>
    public DateTime? LastRunUtc
    {
        get
        {
            var ticks = preferences.GetLong(TimeKey, 0);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }
}
