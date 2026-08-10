using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Util;
using AndroidX.Work;
using FocusPhone.Infrastructure.Updates;
using FocusPhone.Ui.Voice;
using Storyworthtelling.Voice.Health;

namespace FocusPhone.Launcher;

/// <summary>
/// The post-lockdown update path: a periodic worker polls this repo's GitHub
/// Releases and device-owner-silent-installs newer signed APKs. Every install
/// session re-whitelists the hard-restricted READ_CALL_LOG and READ_SMS
/// permissions so the Communication app keeps call-log and text-history
/// access across updates.
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

    public static async Task CheckAndInstallAsync(Context context)
    {
        var settings = new UpdateSettingsStore(context);
        var token = settings.GitHubToken;
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

        var installed = context.PackageManager!
            .GetPackageInfo(context.PackageName!, (PackageInfoFlags)0)!
            .VersionName!;
        if (!release.IsNewerThan(installed))
        {
            Log.Info(Tag, $"Installed {installed} is current (latest release {release.TagName}).");
            return;
        }

        foreach (var asset in release.ApkAssets)
        {
            Log.Info(Tag, $"Installing {asset.Name} from {release.TagName}.");
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            request.Headers.Authorization = new("Bearer", token);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Downloading {asset.Name} failed with {(int)response.StatusCode} {response.StatusCode}.");
            }
            await InstallApkAsync(context, await response.Content.ReadAsStreamAsync());
        }
    }

    private static async Task InstallApkAsync(Context context, Stream apk)
    {
        var installer = context.PackageManager!.PackageInstaller;
        var sessionParams = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        sessionParams.SetWhitelistedRestrictedPermissions(
            [
                Android.Manifest.Permission.ReadCallLog,
                Android.Manifest.Permission.ReadSms,
                Android.Manifest.Permission.WriteCallLog,
            ]);

        var sessionId = installer.CreateSession(sessionParams);
        using var session = installer.OpenSession(sessionId);
        using (var output = session.OpenWrite("focusphone.apk", 0, -1))
        {
            await apk.CopyToAsync(output);
            session.Fsync(output);
        }

        var statusIntent = new Intent(context, typeof(InstallResultReceiver));
        var pending = PendingIntent.GetBroadcast(
            context, sessionId, statusIntent, PendingIntentFlags.Mutable)!;
        session.Commit(pending.IntentSender);
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
