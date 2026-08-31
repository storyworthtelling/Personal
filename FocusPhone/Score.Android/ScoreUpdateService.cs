using Android.App.Admin;
using Android.Content;
using Android.Util;
using AndroidX.Work;
using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Codex.Infrastructure.Updates;
using Codex.Ui;
using Codex.Ui.Voice;
using Storyworthtelling.Updates;
using Storyworthtelling.Voice.Secrets;

namespace Score;

/// <summary>
/// Keeps Score current on a phone that has no custodian.
///
/// The locked phone does not need this: Flyleaf is the device owner there and installs
/// for the whole suite, because it is the only thing that can install at all. An
/// ordinary Android phone has no device owner and no launcher of ours, so nothing
/// would ever update Score unless Score did it itself — which is the whole of B8's
/// update story.
///
/// The role is decided rather than assumed. <see cref="InstallDuty"/> reads the device:
/// where a custodian holds device owner, this resolves to <see cref="UpdateRole.Deferred"/>
/// and the run stops before it downloads anything. That matters because the same APK
/// ships to both phones — the build cannot know which one it landed on, and a Score
/// that tried to install itself on the locked phone would fail every time and report a
/// permanent error on a device whose updates are working perfectly.
///
/// It installs only itself. Sibling apps on her phone look after themselves the same
/// way; nothing here has the standing to install anything else, and
/// <see cref="UpdateScope"/> enforces that rather than this remembering to.
/// </summary>
public static class ScoreUpdateService
{
    private const string Tag = "ScoreUpdate";
    private const string WorkName = "score-update-check";
    private const string Repo = "storyworthtelling/Personal";

    /// <summary>The daily check. Network-constrained, and Keep so a relaunch does not
    /// reset a pending run.</summary>
    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected!)!
            .Build()!;
        var request = new PeriodicWorkRequest.Builder(typeof(ScoreUpdateWorker), TimeSpan.FromHours(24))
            .SetConstraints(constraints)!
            .AddTag(WorkName)!
            .Build();

        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(WorkName, ExistingPeriodicWorkPolicy.Keep!, (PeriodicWorkRequest)request);
    }

    public static async Task<UpdateRunResult> CheckAndInstallAsync(Context context)
    {
        var result = await RunAsync(context);
        new ScoreUpdateStatusStore(context).Record(result.Summarize());
        return result;
    }

    private static async Task<UpdateRunResult> RunAsync(Context context)
    {
        // Asked first, before a token is read or a byte is fetched: on the locked
        // phone the honest answer is that this is not Score's job.
        if (Duty(context) != UpdateRole.SelfOnly)
        {
            Log.Info(Tag, "A custodian looks after updates on this device; nothing to do.");
            return UpdateRunResult.Ran([]);
        }

        var token = await TokenAsync(context);
        if (string.IsNullOrEmpty(token))
        {
            return UpdateRunResult.NoToken();
        }

        using var http = new HttpClient();
        ReleaseInfo release;
        try
        {
            release = await new GitHubReleaseClient(http, Repo, token).GetLatestReleaseAsync();
        }
        catch (HttpRequestException e)
        {
            Log.Warn(Tag, $"Release fetch failed: {e.Message}");
            return UpdateRunResult.FetchFailed(e.Message);
        }

        var results = new List<AppInstallResult>();
        foreach (var asset in release.ApkAssets)
        {
            var path = System.IO.Path.Combine(context.CacheDir!.AbsolutePath!, $"score-update-{asset.Id}.apk");
            try
            {
                await DownloadAsync(http, asset, token, path);
                var candidate = ReadCandidate(context, path);
                if (candidate is null)
                {
                    results.Add(new AppInstallResult(asset.Name, 0, InstallOutcome.Unreadable));
                    continue;
                }

                // Self only. Everything else on the release is somebody else's business,
                // including the other apps on this very phone.
                var wanted = UpdateScope.Decide(
                    UpdateRole.SelfOnly,
                    context.PackageName!,
                    [candidate],
                    InstalledVersionCodes(context, [candidate.Package]));
                if (wanted.Count == 0)
                {
                    continue;
                }

                using var apk = File.OpenRead(path);
                await ApkInstaller.InstallAsync(context, apk);
                results.Add(new AppInstallResult(candidate.Package, candidate.VersionCode, InstallOutcome.Installed));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Left unprocessed on purpose so the next run retries — the confirmed
                // installer's commonest failure is nobody being near the phone.
                Log.Warn(Tag, $"{asset.Name}: {e.Message}");
                results.Add(new AppInstallResult(asset.Name, 0, InstallOutcome.Failed, e.Message));
            }
            finally
            {
                DeleteQuietly(path);
            }
        }

        return UpdateRunResult.Ran(results);
    }

    // The mechanical bits, kept here rather than shared with the launcher's copies.
    // Sharing them would need either Codex.Ui.Android to depend on Codex.Infrastructure
    // — a boundary the UI kit deliberately keeps — or Infrastructure to multi-target
    // Android, and neither is worth it for four short methods with no decisions in them.
    private static async Task DownloadAsync(HttpClient http, ReleaseAsset asset, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd("application/octet-stream");

        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            // Named rather than bare: there is no adb here, so this message is the
            // whole diagnostic.
            throw new HttpRequestException(
                $"Downloading {asset.Name} failed with {(int)response.StatusCode} {response.StatusCode}.");
        }

        using var output = File.Create(path);
        await response.Content.CopyToAsync(output);
    }

    private static InstallCandidate? ReadCandidate(Context context, string path)
    {
        var info = context.PackageManager!.GetPackageArchiveInfo(
            path, (Android.Content.PM.PackageInfoFlags)0);
        return info?.PackageName is null ? null : new InstallCandidate(info.PackageName, info.LongVersionCode);
    }

    // Absent and version 0 mean different things to the update decision, so a package
    // that is not installed is left out rather than recorded as old.
    private static Dictionary<string, long> InstalledVersionCodes(
        Context context, IEnumerable<string> packages)
    {
        var installed = new Dictionary<string, long>();
        foreach (var package in packages.Distinct())
        {
            try
            {
                var info = context.PackageManager!.GetPackageInfo(
                    package, (Android.Content.PM.PackageInfoFlags)0);
                if (info is not null)
                {
                    installed[package] = info.LongVersionCode;
                }
            }
            catch (Android.Content.PM.PackageManager.NameNotFoundException)
            {
                // Not installed. Left out on purpose.
            }
        }

        return installed;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e)
        {
            Log.Warn(Tag, $"Could not delete {path}: {e.Message}");
        }
    }

    // Both halves come from the device rather than from a setting: whether this app is
    // the device owner, and whether the launcher is. On her phone neither is true.
    private static UpdateRole Duty(Context context)
    {
        var policy = (DevicePolicyManager?)context.GetSystemService(Context.DevicePolicyService);
        if (policy is null)
        {
            return UpdateRole.SelfOnly;
        }

        return InstallDuty.Decide(
            isSelfDeviceOwner: policy.IsDeviceOwnerApp(context.PackageName),
            custodianIsPresent: policy.IsDeviceOwnerApp(PolicyContext.LauncherPackage));
    }

    // The same token the launcher uses, from the shared vault — it is a household
    // credential, and her install reads the shared vault even though it never writes it.
    private static async Task<string?> TokenAsync(Context context)
    {
        try
        {
            var access = new SecretsVaultAccess(
                new Score.Google.DeviceAccountTokenProvider().GetAccessToken, "Score",
                () => ScoreIdentity.Account);
            var vault = await access.Service.LoadAsync(CancellationToken.None);
            return vault.Status == SecretsVaultStatus.Loaded ? vault.Vault.GitHubUpdateToken : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warn(Tag, $"Could not read the update token: {e.Message}");
            return null;
        }
    }
}

[global::Android.Runtime.Register("com.storyworthtelling.focusphone.audio.ScoreUpdateWorker")]
public class ScoreUpdateWorker : Worker
{
    public ScoreUpdateWorker(Context context, WorkerParameters parameters) : base(context, parameters)
    {
    }

    public override Result DoWork()
    {
        ScoreUpdateService.CheckAndInstallAsync(ApplicationContext!).GetAwaiter().GetResult();
        return Result.InvokeSuccess();
    }
}

/// <summary>
/// The last run, as a sentence for the Settings screen. Its own store rather than the
/// launcher's: two apps writing one status would each overwrite the other's, and on
/// her phone the launcher is not installed at all.
/// </summary>
public class ScoreUpdateStatusStore(Context context)
{
    private const string PreferencesName = "scoreUpdateStatus";
    private const string SummaryKey = "lastSummary";
    private const string TimeKey = "lastRunUtcTicks";

    private ISharedPreferences Preferences =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public string? LastSummary => Preferences.GetString(SummaryKey, null);

    public DateTime? LastRunUtc
    {
        get
        {
            var ticks = Preferences.GetLong(TimeKey, 0);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public void Record(string summary) => Preferences.Edit()!
        .PutString(SummaryKey, summary)!
        .PutLong(TimeKey, DateTime.UtcNow.Ticks)!
        .Apply();
}
