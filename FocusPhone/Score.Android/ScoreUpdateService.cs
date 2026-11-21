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
using Codex.Ui.Http;

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
            HeadLog.Info(Tag, "A custodian looks after updates on this device; nothing to do.");
            return UpdateRunResult.Ran([]);
        }

        var token = await TokenAsync(context);
        if (string.IsNullOrEmpty(token))
        {
            return UpdateRunResult.NoToken();
        }

        var http = HeadHttp.Shared;
        ReleaseInfo release;
        try
        {
            // The phone's own release, by its tag: Wave, Score's watch half, is published to the watch's (release A
            // fix round).
            release = await new GitHubReleaseClient(http, Repo, token).GetReleaseAsync(ReleaseChannels.Phone);
        }
        catch (HttpRequestException e)
        {
            HeadLog.Warn(Tag, $"Release fetch failed: {e.Message}");
            return UpdateRunResult.FetchFailed(e.Message);
        }

        var results = new List<AppInstallResult>();
        foreach (var asset in release.ApkAssets)
        {
            // Every asset here is downloaded in full to be read, so one named as a watch APK is passed over by its
            // name before a byte is fetched; its manifest would refuse it anyway, below.
            if (ApkForms.FromAssetName(asset.Name) != ApkForm.Phone)
            {
                continue;
            }

            var path = System.IO.Path.Combine(context.CacheDir!.AbsolutePath!, $"score-update-{asset.Id}.apk");
            try
            {
                await DownloadAsync(http, asset, token, path);
                if (ApkArchive.Read(context, path) is not { } read)
                {
                    results.Add(new AppInstallResult(asset.Name, 0, InstallOutcome.Unreadable));
                    continue;
                }

                // Wave is Score's watch half and carries Score's package at a higher
                // versionCode band, so by package and version it is Score's newest build.
                // The APK's own manifest and its asset's name must both say phone (P14 W-3.1.1).
                var candidate = read with { Form = ApkForms.Agree(read.Form, ApkForms.FromAssetName(asset.Name)) };

                // Self only, and phone only. Everything else on the release is somebody
                // else's business, including the other apps on this very phone.
                var wanted = UpdateScope.Decide(
                    UpdateRole.SelfOnly,
                    context.PackageName!,
                    ApkForm.Phone,
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
                HeadLog.Warn(Tag, $"{asset.Name}: {e.Message}");
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
    // Android, and neither is worth it for three short methods with no decisions in them.
    // Reading what an APK is does decide something (its form), so that one is shared:
    // ApkArchive, in the kit.
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
            HeadLog.Warn(Tag, $"Could not delete {path}: {e.Message}");
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
                new Score.Google.DeviceAccountTokenProvider().GetAccessToken, "Score");
            var vault = await access.Service.LoadAsync(CancellationToken.None);
            return vault.Status == SecretsVaultStatus.Loaded ? vault.Vault.GitHubUpdateToken : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            HeadLog.Warn(Tag, $"Could not read the update token: {e.Message}");
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

    // A run stopped before it returned leaves a line, not a lone "started" (audit 08 §20).
    public override void OnStopped() => HeadDiagnostics.Stopped(nameof(ScoreUpdateWorker), StopReason);

    public override Result DoWork() => HeadDiagnostics.Worker(nameof(ScoreUpdateWorker), Work);


    private Result Work()

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
