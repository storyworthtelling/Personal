using Codex.Ui;
using Android.Content;
using Android.Content.PM;
using Android.Util;

namespace Flyleaf;

/// <summary>
/// Keeps the device-owner policy current as apps come and go. The model is <b>visibility IS
/// runnability</b> (owner's directive): a newly-installed app is NOT quarantined — it simply runs and
/// shows, unless it falls in a never-visible category (games/social/dating/gambling/short-form or a
/// non-Flyleaf browser/launcher), which the policy plan hard-suspends. So this watcher just re-applies
/// the policy whenever the installed set changes: <see cref="PolicyManager.ApplyCurrentPolicy"/>
/// recomputes the never-visible set over the live install list and releases everything else.
/// <para>
/// This replaces the earlier fail-closed quarantine (suspend-every-new-app then release only what a
/// Play-rating categorization positively cleared), which left visible, wanted apps — Gemini, Google
/// Messages — stuck behind a "blocked by your administrator" wall when categorization couldn't clear them.
/// </para>
/// </summary>
public class PackageWatcher
{
    private const string Tag = "FlyleafWatcher";

    private readonly Context context;

    public PackageWatcher(Context context)
    {
        this.context = context;
    }

    public void Start()
    {
        var launcherApps = (LauncherApps)context.GetSystemService(Context.LauncherAppsService)!;
        launcherApps.RegisterCallback(new InstallCallback(this));
        // Catch anything installed/removed while the launcher was dead.
        Reapply();
    }

    private void Reapply()
    {
        try
        {
            PolicyManager.ApplyCurrentPolicy(context);
        }
        catch (Java.Lang.Exception e)
        {
            HeadLog.Warn(Tag, $"Could not re-apply policy after an install change: {e.Message}");
        }
    }

    private class InstallCallback : LauncherApps.Callback
    {
        private readonly PackageWatcher watcher;

        public InstallCallback(PackageWatcher watcher)
        {
            this.watcher = watcher;
        }

        // A new install may be a never-visible app to suspend, or an ordinary app to leave running;
        // either way the pure plan decides, so re-apply. A removal can free the never-visible set, so
        // re-apply there too.
        public override void OnPackageAdded(string? packageName, Android.OS.UserHandle? user) => watcher.Reapply();
        public override void OnPackageRemoved(string? packageName, Android.OS.UserHandle? user) => watcher.Reapply();
        public override void OnPackageChanged(string? packageName, Android.OS.UserHandle? user) { }
        public override void OnPackagesAvailable(string[]? packageNames, Android.OS.UserHandle? user, bool replacing) { }
        public override void OnPackagesUnavailable(string[]? packageNames, Android.OS.UserHandle? user, bool replacing) { }
    }
}
