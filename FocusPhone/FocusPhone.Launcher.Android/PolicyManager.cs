using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using FocusPhone.Domain.Policy;

namespace FocusPhone.Launcher;

/// <summary>
/// The single choke point that applies device-owner policy. All decisions are
/// made by the unit-tested PolicyPlanBuilder in Domain; this class only gathers
/// the device facts and applies the resulting plan verbatim.
/// </summary>
public static class PolicyManager
{
    private const string Tag = "FocusPhonePolicy";

    public static ComponentName AdminComponent(Context context) =>
        new(context, Java.Lang.Class.FromType(typeof(FocusDeviceAdminReceiver)));

    public static bool IsDeviceOwner(Context context)
    {
        var dpm = (DevicePolicyManager)context.GetSystemService(Context.DevicePolicyService)!;
        return dpm.IsDeviceOwnerApp(context.PackageName);
    }

    public static void ApplyCurrentPolicy(Context context)
    {
        if (!IsDeviceOwner(context))
        {
            Log.Warn(Tag, "Not device owner; policy not applied. This is expected during development.");
            return;
        }

        var store = new BlocklistStore(context);
        var policyContext = new PolicyContext(
            OwnPackages: PolicyContext.FocusPhonePackages,
            BrowserPackages: QueryBrowserPackages(context),
            OtherHomePackages: QueryOtherHomePackages(context),
            BlockedPackages: store.Load().BlockedPackages.ToHashSet(),
            DebuggingLockedDown: store.DebuggingLockedDown);

        Apply(context, PolicyPlanBuilder.Build(policyContext));
    }

    private static void Apply(Context context, PolicyPlan plan)
    {
        var dpm = (DevicePolicyManager)context.GetSystemService(Context.DevicePolicyService)!;
        var admin = AdminComponent(context);

        foreach (var restriction in plan.UserRestrictions)
        {
            dpm.AddUserRestriction(admin, restriction);
        }

        if (plan.EnforceAutoTime)
        {
            dpm.SetAutoTimeEnabled(admin, true);
            dpm.SetAutoTimeZoneEnabled(admin, true);
        }

        // Blocks the swipe-down notification shade and quick settings (and the
        // Settings shortcut inside them). Only the device owner may do this, so
        // it is a no-op until provisioning; the launcher re-applies it every
        // OnCreate so it re-asserts after any transient re-enable.
        if (!dpm.SetStatusBarDisabled(admin, plan.DisableStatusBar))
        {
            Log.Warn(Tag, "Could not disable the status bar; not permitted in the current state.");
        }

        try
        {
            dpm.SetGlobalPrivateDnsModeSpecifiedHost(admin, plan.PrivateDnsHost);
        }
        catch (Java.Lang.Exception e)
        {
            // Setting private DNS validates the resolver over the network; offline
            // application is retried on the next policy pass rather than failing
            // the whole lockdown.
            Log.Warn(Tag, $"Could not set private DNS to {plan.PrivateDnsHost} yet: {e.Message}");
        }

        var installed = plan.SuspendedPackages
            .Where(p => IsInstalled(context, p))
            .ToArray();
        if (installed.Length > 0)
        {
            var failed = dpm.SetPackagesSuspended(admin, installed, true);
            if (failed is { Length: > 0 })
            {
                Log.Warn(Tag, $"Could not suspend: {string.Join(", ", failed)}");
            }
        }

        // Actively un-suspend the cutover allowlist (curated apps + the login
        // browser). Removing them from the suspend set is not enough: the install
        // watcher may have already quarantined one, and nothing else releases it.
        var toRelease = plan.RunnablePackages
            .Where(p => IsInstalled(context, p))
            .ToArray();
        if (toRelease.Length > 0)
        {
            var failed = dpm.SetPackagesSuspended(admin, toRelease, false);
            if (failed is { Length: > 0 })
            {
                Log.Warn(Tag, $"Could not un-suspend: {string.Join(", ", failed)}");
            }
        }

        ApplyLoginBrowserConfig(context, dpm, admin, plan.LoginBrowser);
        GrantPermissions(dpm, admin, plan);
        PinHomeActivity(dpm, admin, context);
        PinRestrictedBrowser(dpm, admin, context);
        Log.Info(Tag, $"Policy applied: {plan.UserRestrictions.Count} restrictions, {installed.Length} packages suspended, {plan.PermissionGrants.Count} permission grants.");
    }

    /// <summary>
    /// Restarts the device. Only a device owner may reboot without user
    /// interaction, and only when there is no ongoing call (the platform throws
    /// otherwise) — callers surface that as an inline status.
    /// </summary>
    public static void Reboot(Context context)
    {
        var dpm = (DevicePolicyManager)context.GetSystemService(Context.DevicePolicyService)!;
        dpm.Reboot(AdminComponent(context));
    }

    /// <summary>
    /// Cutover finalization: irreversibly (short of a recovery-mode factory
    /// reset) disables debugging. Callable only from an active admin session.
    /// </summary>
    public static void LockDownDebugging(Context context)
    {
        var store = new BlocklistStore(context);
        store.DebuggingLockedDown = true;
        ApplyCurrentPolicy(context);
    }

    /// <summary>
    /// Force-grants the hard-restricted permissions the Communication app needs
    /// (READ_SMS, READ_CALL_LOG). The APK is whitelisted at install time, which
    /// only un-restricts these; the device-owner grant is what actually turns
    /// them on — with no runtime dialog, across silent reinstalls, and pinned so
    /// Android's unused-app auto-reset can't revoke them.
    /// </summary>
    private static void GrantPermissions(DevicePolicyManager dpm, ComponentName admin, PolicyPlan plan)
    {
        foreach (var grant in plan.PermissionGrants)
        {
            try
            {
                // Returns false (or throws) if the target app isn't installed yet
                // or the permission isn't whitelisted; the next policy pass (e.g.
                // after the update worker installs it) retries, so a single miss
                // must not abort the rest of the lockdown.
                if (!dpm.SetPermissionGrantState(admin, grant.Package, grant.Permission,
                        PermissionGrantState.Granted))
                {
                    Log.Warn(Tag, $"Could not grant {grant.Permission} to {grant.Package} yet; will retry next policy pass.");
                }
            }
            catch (Java.Lang.Exception e)
            {
                Log.Warn(Tag, $"Could not grant {grant.Permission} to {grant.Package} yet: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Locks the login browser (Chrome) to the identity-provider hosts via its
    /// enterprise managed configuration: block every URL, allow only the auth
    /// domains, and disable incognito. This is what makes "very limited use for
    /// login" real — without it, the un-suspended browser is an open door (the
    /// Custom Tab's "Open in Chrome" overflow and on-page links both reach the
    /// open internet). Applied through the app-restrictions channel the device
    /// owner already controls; a no-op until Chrome is installed.
    /// </summary>
    private static void ApplyLoginBrowserConfig(
        Context context, DevicePolicyManager dpm, ComponentName admin, LoginBrowserPolicy login)
    {
        if (!IsInstalled(context, login.Package))
        {
            return;
        }

        var restrictions = new Bundle();
        // "*" blocks everything; the allowlist carves the exceptions back out.
        // Chrome gives the allowlist precedence over the blocklist.
        restrictions.PutStringArray("URLBlocklist", ["*"]);
        restrictions.PutStringArray("URLAllowlist", login.AllowedUrlPatterns.ToArray());
        // 1 = incognito disabled, so the filter can't be sidestepped in a new
        // incognito tab.
        restrictions.PutInt("IncognitoModeAvailability", 1);

        try
        {
            dpm.SetApplicationRestrictions(admin, login.Package, restrictions);
        }
        catch (Java.Lang.Exception e)
        {
            Log.Warn(Tag, $"Could not lock down the login browser {login.Package}: {e.Message}");
        }
    }

    private static void PinHomeActivity(DevicePolicyManager dpm, ComponentName admin, Context context)
    {
        var filter = new IntentFilter(Intent.ActionMain);
        filter.AddCategory(Intent.CategoryHome);
        filter.AddCategory(Intent.CategoryDefault);
        dpm.AddPersistentPreferredActivity(admin, filter,
            new ComponentName(context, Java.Lang.Class.FromType(typeof(HomeActivity))));
    }

    private static void PinRestrictedBrowser(DevicePolicyManager dpm, ComponentName admin, Context context)
    {
        var browser = new ComponentName(context, Java.Lang.Class.FromType(typeof(RestrictedBrowserActivity)));
        foreach (var scheme in new[] { "http", "https" })
        {
            var filter = new IntentFilter(Intent.ActionView);
            filter.AddCategory(Intent.CategoryDefault);
            filter.AddCategory(Intent.CategoryBrowsable);
            filter.AddDataScheme(scheme);
            dpm.AddPersistentPreferredActivity(admin, filter, browser);
        }
    }

    private static IReadOnlySet<string> QueryBrowserPackages(Context context)
    {
        var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse("https://example.com"));
        intent.AddCategory(Intent.CategoryBrowsable);
        return QueryPackages(context, intent);
    }

    private static IReadOnlySet<string> QueryOtherHomePackages(Context context)
    {
        var intent = new Intent(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryHome);
        var homes = QueryPackages(context, intent);
        homes.Remove(context.PackageName!);
        return homes;
    }

    private static HashSet<string> QueryPackages(Context context, Intent intent)
    {
        return context.PackageManager!
            .QueryIntentActivities(intent, PackageInfoFlags.MatchAll)
            .Select(resolveInfo => resolveInfo.ActivityInfo!.PackageName!)
            .ToHashSet();
    }

    private static bool IsInstalled(Context context, string packageName)
    {
        try
        {
            context.PackageManager!.GetPackageInfo(packageName, (PackageInfoFlags)0);
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }
}
