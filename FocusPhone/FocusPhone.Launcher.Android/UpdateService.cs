using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Util;
using AndroidX.Work;
using FocusPhone.Domain.Updates;
using FocusPhone.Infrastructure.Updates;
using FocusPhone.Ui.Voice;
using Storyworthtelling.Voice.Health;

namespace FocusPhone.Launcher;

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
    public static Task CheckAndInstallAsync(Context context) =>
        CheckAndInstallAsync(context, force: false);

    /// <param name="force">
    /// true from the admin "Install from GitHub now" action: ignore the
    /// download-dedup cache and re-evaluate every asset on the release (a manual
    /// retry). The periodic worker passes false so it only fetches changes.
    /// </param>
    public static async Task CheckAndInstallAsync(Context context, bool force)
    {
        var token = new UpdateSettingsStore(context).GitHubToken;
        if (string.IsNullOrEmpty(token))
        {
            Log.Info(Tag, "No update token configured; skipping check.");
            return;
        }
        if (!PolicyManager.IsDeviceOwner(context))
        {
            Log.Info(Tag, "Not device owner; silent install unavailable, skipping check.");
            return;
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
            throw;
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
            return;
        }

        // Download each pending APK to the cache and read its package + version.
        var downloaded = new List<(ReleaseAsset Asset, string Path, InstallCandidate Candidate)>();
        foreach (var asset in pending)
        {
            var path = System.IO.Path.Combine(context.CacheDir!.AbsolutePath!, $"update-{asset.Id}.apk");
            await DownloadAsync(http, asset, token, path);

            var info = context.PackageManager!.GetPackageArchiveInfo(path, (PackageInfoFlags)0);
            if (info?.PackageName is null)
            {
                Log.Warn(Tag, $"{asset.Name} is not a readable APK; skipping.");
                DeleteQuietly(path);
                processed.Add(asset.Id); // don't keep re-downloading a bad asset
                continue;
            }
            downloaded.Add((asset, path, new InstallCandidate(info.PackageName, info.LongVersionCode)));
        }

        var installedVersions = InstalledVersionCodes(context, downloaded.Select(d => d.Candidate.Package));
        var toInstall = SiblingUpdatePlan.Decide(downloaded.Select(d => d.Candidate), installedVersions)
            .Select(c => c.Package)
            .ToHashSet();

        foreach (var item in downloaded)
        {
            try
            {
                if (toInstall.Contains(item.Candidate.Package))
                {
                    Log.Info(Tag, $"Installing {item.Candidate.Package} v{item.Candidate.VersionCode} from {item.Asset.Name}.");
                    using var apk = System.IO.File.OpenRead(item.Path);
                    await ApkInstaller.InstallAsync(context, apk);
                }
                // Mark processed whether installed or already-current, so an
                // unchanged asset is not re-downloaded next cycle.
                processed.Add(item.Asset.Id);
            }
            finally
            {
                DeleteQuietly(item.Path);
            }
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
            UpdateService.CheckAndInstallAsync(ApplicationContext).GetAwaiter().GetResult();
            return Result.InvokeSuccess();
        }
        catch (Exception e)
        {
            Log.Warn("FocusPhoneUpdate", $"Update check failed; will retry on next period: {e}");
            return Result.InvokeRetry();
        }
    }
}

[BroadcastReceiver(Exported = false)]
public class InstallResultReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var status = (PackageInstallStatus)(intent?.GetIntExtra(PackageInstaller.ExtraStatus, -1) ?? -1);
        var message = intent?.GetStringExtra(PackageInstaller.ExtraStatusMessage);
        Log.Info("FocusPhoneUpdate", $"Install session result: {status} {message}");
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
