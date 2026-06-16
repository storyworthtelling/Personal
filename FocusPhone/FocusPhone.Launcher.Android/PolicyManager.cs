using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
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

        PinHomeActivity(dpm, admin, context);
        PinRestrictedBrowser(dpm, admin, context);
        Log.Info(Tag, $"Policy applied: {plan.UserRestrictions.Count} restrictions, {installed.Length} packages suspended.");
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
