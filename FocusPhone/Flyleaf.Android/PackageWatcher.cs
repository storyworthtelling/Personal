using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.Util;
using Codex.Domain.Apps;
using Codex.Infrastructure.Play;

namespace Flyleaf;

/// <summary>
/// Fail-closed quarantine of new installs. Every package never seen before is
/// suspended the moment it appears and only released once categorization
/// positively clears it (not a browser, not a game, Play rating below
/// Mature). Live installs arrive via LauncherApps callbacks; a sweep at every
/// process start catches anything installed while the launcher was dead.
/// </summary>
public class PackageWatcher
{
    private const string Tag = "FocusPhoneWatcher";
    private const string PreferencesName = "packageWatcher";
    private const string KnownKey = "knownPackages";

    private readonly Context context;
    private readonly ISharedPreferences preferences;
    private readonly PlayRatingClient ratingClient = new(new HttpClient());

    public PackageWatcher(Context context)
    {
        this.context = context;
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public void Start()
    {
        var launcherApps = (LauncherApps)context.GetSystemService(Context.LauncherAppsService)!;
        launcherApps.RegisterCallback(new InstallCallback(this));
        _ = SweepAsync();
    }

    private async Task SweepAsync()
    {
        var known = KnownPackages();
        var installed = context.PackageManager!
            .GetInstalledApplications(PackageInfoFlags.MatchAll)
            .Select(app => app.PackageName!)
            .ToList();

        foreach (var packageName in installed.Where(p => !known.Contains(p)))
        {
            await HandleNewPackageAsync(packageName);
        }
    }

    private async Task HandleNewPackageAsync(string packageName)
    {
        if (PolicyContextPackages.Contains(packageName) || KnownPackages().Contains(packageName))
        {
            return;
        }
        MarkKnown(packageName);

        if (IsSystemApp(packageName))
        {
            // Preinstalled system packages are governed by PolicyManager's
            // browser/launcher suspension, not the install quarantine.
            return;
        }

        Suspend(packageName, true);
        Log.Info(Tag, $"Quarantined new package {packageName}; categorizing.");

        var store = new BlocklistStore(context);
        var categorizer = new AppCategorizer(store.Load());
        var rating = await ratingClient.GetContentRatingAsync(packageName);
        var verdict = categorizer.Categorize(BuildAppInfo(packageName), rating);

        if (verdict == AppVerdict.Allowed)
        {
            Suspend(packageName, false);
        }
        Log.Info(Tag, $"{packageName}: rating {rating}, verdict {verdict}.");
    }

    private AppInfo BuildAppInfo(string packageName)
    {
        var viewIntent = new Intent(Intent.ActionView, Android.Net.Uri.Parse("https://example.com"));
        viewIntent.SetPackage(packageName);
        var handlesWeb = context.PackageManager!
            .QueryIntentActivities(viewIntent, PackageInfoFlags.MatchAll).Count > 0;

        var isGame = false;
        try
        {
            var applicationInfo = context.PackageManager.GetApplicationInfo(packageName, (PackageInfoFlags)0);
            isGame = applicationInfo.Category == ApplicationCategories.Game;
        }
        catch (PackageManager.NameNotFoundException)
        {
            // Uninstalled between callback and categorization; verdict is moot.
        }

        return new AppInfo(packageName, handlesWeb, isGame);
    }

    private void Suspend(string packageName, bool suspended)
    {
        if (!PolicyManager.IsDeviceOwner(context))
        {
            return;
        }
        var dpm = (DevicePolicyManager)context.GetSystemService(Context.DevicePolicyService)!;
        var failed = dpm.SetPackagesSuspended(
            PolicyManager.AdminComponent(context), [packageName], suspended);
        if (failed is { Length: > 0 })
        {
            Log.Warn(Tag, $"Could not change suspension of {packageName}.");
        }
    }

    private bool IsSystemApp(string packageName)
    {
        try
        {
            var applicationInfo = context.PackageManager!.GetApplicationInfo(packageName, (PackageInfoFlags)0);
            return applicationInfo.Flags.HasFlag(ApplicationInfoFlags.System);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    private HashSet<string> KnownPackages() =>
        preferences.GetStringSet(KnownKey, [])!.ToHashSet();

    private void MarkKnown(string packageName)
    {
        var known = KnownPackages();
        known.Add(packageName);
        preferences.Edit()!.PutStringSet(KnownKey, known)!.Apply();
    }

    private static readonly IReadOnlySet<string> PolicyContextPackages =
        Codex.Domain.Policy.PolicyContext.FocusPhonePackages;

    private class InstallCallback : LauncherApps.Callback
    {
        private readonly PackageWatcher watcher;

        public InstallCallback(PackageWatcher watcher)
        {
            this.watcher = watcher;
        }

        public override void OnPackageAdded(string? packageName, Android.OS.UserHandle? user)
        {
            if (packageName is not null)
            {
                _ = watcher.HandleNewPackageAsync(packageName);
            }
        }

        public override void OnPackageChanged(string? packageName, Android.OS.UserHandle? user) { }
        public override void OnPackageRemoved(string? packageName, Android.OS.UserHandle? user) { }
        public override void OnPackagesAvailable(string[]? packageNames, Android.OS.UserHandle? user, bool replacing) { }
        public override void OnPackagesUnavailable(string[]? packageNames, Android.OS.UserHandle? user, bool replacing) { }
    }
}
