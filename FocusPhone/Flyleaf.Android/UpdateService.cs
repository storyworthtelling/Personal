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
/// </summary>
public static class UpdateService
{
    private const string Tag = "FocusPhoneUpdate";
    private const string WorkName = "focusphone-update-check";
    public const string Repo = "storyworthtelling/Personal";

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
        var result = await RunAsync(context, force);
        new UpdateStatusStore(context).Record(result.Summarize());
        return result;
    }

    private static async Task<UpdateRunResult> RunAsync(Context context, bool force)
    {
        var token = new UpdateSettingsStore(context).GitHubToken;
        if (string.IsNullOrEmpty(token))
        {
            Log.Info(Tag, "No update token configured; skipping check.");
            return UpdateRunResult.NoToken();
        }
        if (!PolicyManager.IsDeviceOwner(context))
        {
            Log.Info(Tag, "Not device owner; silent install unavailable, skipping check.");
            return UpdateRunResult.NotDeviceOwner();
        }

        using var http = new HttpClient();
        var client = new GitHubReleaseClient(http, Repo, token);
        var health = new CredentialHealthStore();
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
            return UpdateRunResult.FetchFailed($"{(int)ex.StatusCode} {ex.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            // Transient (network/5xx): report it so the worker retries, without flagging the token.
            Log.Warn(Tag, $"Release fetch failed: {ex.Message}");
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
            Log.Info(Tag, "No new release assets to consider.");
            return UpdateRunResult.Ran([]);
        }

        // Download each pending APK to the cache and read its package + version.
        var downloaded = new List<(ReleaseAsset Asset, string Path, InstallCandidate Candidate)>();
        var results = new List<AppInstallResult>();
        foreach (var asset in pending)
        {
            var path = System.IO.Path.Combine(context.CacheDir!.AbsolutePath!, $"update-{asset.Id}.apk");
            try
            {
                await DownloadAsync(http, asset, token, path);
            }
            catch (Exception e)
            {
                // A failed download isn't remembered, so it's retried next cycle.
                Log.Warn(Tag, $"Downloading {asset.Name} failed: {e.Message}");
                results.Add(new AppInstallResult(asset.Name, 0, InstallOutcome.Failed, e.Message));
                DeleteQuietly(path);
                continue;
            }

            var info = context.PackageManager!.GetPackageArchiveInfo(path, (PackageInfoFlags)0);
            if (info?.PackageName is null)
            {
                Log.Warn(Tag, $"{asset.Name} is not a readable APK; skipping.");
                DeleteQuietly(path);
                processed.Add(asset.Id); // don't keep re-downloading a bad asset
                results.Add(new AppInstallResult(asset.Name, 0, InstallOutcome.Unreadable));
                continue;
            }
            downloaded.Add((asset, path, new InstallCandidate(info.PackageName, info.LongVersionCode)));
        }

        var installedVersions = InstalledVersionCodes(context, downloaded.Select(d => d.Candidate.Package));
        // Flyleaf is the custodian on this device: as the device owner it is the only
        // thing that can install silently, so it installs for the whole suite.
        var toInstall = UpdateScope
            .Decide(UpdateRole.Custodian, PolicyContext.LauncherPackage,
                downloaded.Select(d => d.Candidate), installedVersions)
            .Select(c => c.Package)
            .ToHashSet();

        foreach (var item in downloaded)
        {
            var outcome = await InstallOneAsync(context, item, toInstall);
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
        HashSet<string> toInstall)
    {
        if (!toInstall.Contains(item.Candidate.Package))
        {
            return new AppInstallResult(item.Candidate.Package, item.Candidate.VersionCode, InstallOutcome.AlreadyCurrent);
        }

        try
        {
            Log.Info(Tag, $"Installing {item.Candidate.Package} v{item.Candidate.VersionCode} from {item.Asset.Name}.");
            using var apk = System.IO.File.OpenRead(item.Path);
            await ApkInstaller.InstallAsync(context, apk);
            return new AppInstallResult(item.Candidate.Package, item.Candidate.VersionCode, InstallOutcome.Installed);
        }
        catch (Exception e)
        {
            Log.Warn(Tag, $"Installing {item.Candidate.Package} failed: {e.Message}");
            return new AppInstallResult(item.Candidate.Package, item.Candidate.VersionCode, InstallOutcome.Failed, e.Message);
        }
    }

    private static async Task DownloadAsync(HttpClient http, ReleaseAsset asset, string token, string path)
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
                // Not installed: SiblingUpdatePlan reads the absence as "install".
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
            Log.Warn(Tag, $"Could not delete temp APK {path}: {e.Message}");
        }
    }
}

public class UpdateWorker : Worker
{
    public UpdateWorker(Context context, WorkerParameters workerParams) : base(context, workerParams)
    {
    }

    public override Result DoWork()
    {
        try
        {
            var result = UpdateService.CheckAndInstallAsync(ApplicationContext).GetAwaiter().GetResult();
            // Retry a transient fetch failure so recovery isn't gated on the full 6-hour
            // cadence; a failed install is already left unprocessed and retries next cycle.
            return result.State == UpdateRunState.FetchFailed
                ? Result.InvokeRetry()
                : Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            Log.Warn("FocusPhoneUpdate", $"Update check failed; will retry on next period: {e}");
            return Result.InvokeRetry();
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
