using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Codex.Domain.Policy;

namespace Flyleaf;

/// <summary>
/// The single choke point that applies device-owner policy. All decisions are
/// made by the unit-tested PolicyPlanBuilder in Domain; this class only gathers
/// the device facts and applies the resulting plan verbatim.
/// </summary>
public static class PolicyManager
{
    private const string Tag = "FlyleafPolicy";

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
            new PolicyAuditStore(context).Record(PolicyEventKind.NotDeviceOwner,
                "Policy not applied: the app is not the device owner.");
            return;
        }

        var store = new BlocklistStore(context);
        var policyContext = new PolicyContext(
            OwnPackages: PolicyContext.FocusPhonePackages,
            BrowserPackages: QueryBrowserPackages(context),
            OtherHomePackages: QueryOtherHomePackages(context),
            BlockedPackages: store.Load().BlockedPackages.ToHashSet(),
            DebuggingLockedDown: store.DebuggingLockedDown,
            NeverVisiblePackages: QueryNeverVisiblePackages(context));

        Apply(context, PolicyPlanBuilder.Build(policyContext));
    }

    private static void Apply(Context context, PolicyPlan plan)
    {
        var dpm = (DevicePolicyManager)context.GetSystemService(Context.DevicePolicyService)!;
        var admin = AdminComponent(context);
        var events = new List<PolicyEvent>();

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
            events.Add(Event(PolicyEventKind.ConfigFailed, "Could not disable the status bar (not permitted in the current state)."));
        }

        try
        {
            // Returns a PrivateDnsSet result (it does not throw for a non-serving host): NoError,
            // ErrorFailureSetting, or ErrorHostNotServing. The old code ignored it, so a silent
            // HostNotServing looked like success while the family DNS never applied (adult/SafeSearch
            // went unenforced). Record the specific outcome so a real on-device failure is diagnosable.
            var result = dpm.SetGlobalPrivateDnsModeSpecifiedHost(admin, plan.PrivateDnsHost);
            if (result != PrivateDnsSet.NoError)
            {
                var reason = result == PrivateDnsSet.ErrorHostNotServing
                    ? "host is not serving DNS-over-TLS (unreachable now)"
                    : "the platform rejected the setting";
                Log.Warn(Tag, $"Private DNS not set to {plan.PrivateDnsHost}: {reason} ({result}).");
                events.Add(Event(PolicyEventKind.ConfigFailed,
                    $"Private DNS not set to {plan.PrivateDnsHost}: {reason} ({result})."));
            }
        }
        catch (Java.Lang.Exception e)
        {
            // A thrown exception (vs the result code above) is usually an invalid host or a transient
            // state; retried on the next pass. Record the type when the message is blank so it's not a
            // mystery (the earlier log showed an empty message).
            var detail = string.IsNullOrEmpty(e.Message) ? e.GetType().Name : e.Message;
            Log.Warn(Tag, $"Could not set private DNS to {plan.PrivateDnsHost} yet: {detail}");
            events.Add(Event(PolicyEventKind.ConfigFailed, $"Could not set private DNS to {plan.PrivateDnsHost} yet: {detail}"));
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
                events.Add(Event(PolicyEventKind.SuspendFailed, $"Could not suspend: {string.Join(", ", failed)}"));
            }
        }

        // Visibility IS runnability: actively un-suspend EVERY launchable app the plan doesn't
        // suspend (plus the allowlisted login browser, which is hidden and so not launchable) —
        // not merely leave them alone. A prior pass or the install watcher may have quarantined a
        // visible app (this was why Gemini/Messages showed "blocked by your administrator"); nothing
        // else releases it. Only the never-visible set + the owner's blocks stay suspended.
        var release = new HashSet<string>(
            PolicyReconciler.PackagesToRelease(QueryLaunchablePackages(context), plan.SuspendedPackages));
        release.UnionWith(plan.RunnablePackages);
        release.ExceptWith(plan.SuspendedPackages);
        var toRelease = release.Where(p => IsInstalled(context, p)).ToArray();
        if (toRelease.Length > 0)
        {
            var failed = dpm.SetPackagesSuspended(admin, toRelease, false);
            if (failed is { Length: > 0 })
            {
                Log.Warn(Tag, $"Could not un-suspend: {string.Join(", ", failed)}");
                events.Add(Event(PolicyEventKind.ReleaseFailed, $"Could not un-suspend (kept quarantined): {string.Join(", ", failed)}"));
            }
        }

        ApplyLoginBrowserConfig(context, dpm, admin, plan.LoginBrowser, events);
        GrantPermissions(dpm, admin, plan, events);
        PinHomeActivity(dpm, admin, context);
        PinRestrictedBrowser(dpm, admin, context);
        Log.Info(Tag, $"Policy applied: {plan.UserRestrictions.Count} restrictions, {installed.Length} packages suspended, {plan.PermissionGrants.Count} permission grants.");

        // A rolling audit line per pass; consecutive-identical lines collapse, so this records once
        // and re-records only when the counts change (a newly suspended app, a resolved failure).
        events.Add(Event(PolicyEventKind.Applied,
            $"{plan.UserRestrictions.Count} restrictions · {installed.Length} suspended · {toRelease.Length} released · {plan.PermissionGrants.Count} grants"));
        new PolicyAuditStore(context).Append(events);
    }

    private static PolicyEvent Event(PolicyEventKind kind, string summary) =>
        new(DateTimeOffset.UtcNow, kind, summary);

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
        new PolicyAuditStore(context).Record(PolicyEventKind.CutoverFinalized,
            "Cutover finalized: debugging locked down irreversibly.");
        ApplyCurrentPolicy(context);
    }

    /// <summary>
    /// Force-grants the hard-restricted permissions the Communication app needs
    /// (READ_SMS, READ_CALL_LOG). The APK is whitelisted at install time, which
    /// only un-restricts these; the device-owner grant is what actually turns
    /// them on — with no runtime dialog, across silent reinstalls, and pinned so
    /// Android's unused-app auto-reset can't revoke them.
    /// </summary>
    private static void GrantPermissions(DevicePolicyManager dpm, ComponentName admin, PolicyPlan plan, List<PolicyEvent> events)
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
                    events.Add(Event(PolicyEventKind.GrantFailed, $"Could not grant {grant.Permission} to {grant.Package} yet (will retry)."));
                }
            }
            catch (Java.Lang.Exception e)
            {
                Log.Warn(Tag, $"Could not grant {grant.Permission} to {grant.Package} yet: {e.Message}");
                events.Add(Event(PolicyEventKind.GrantFailed, $"Could not grant {grant.Permission} to {grant.Package} yet: {e.Message}"));
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
        Context context, DevicePolicyManager dpm, ComponentName admin, LoginBrowserPolicy login, List<PolicyEvent> events)
    {
        if (!IsInstalled(context, login.Package))
        {
            return;
        }

        var restrictions = new Bundle();
        // Incognito is always disabled (1) so nothing sidesteps the managed config.
        restrictions.PutInt("IncognitoModeAvailability", 1);
        if (login.BlockNonAllowlisted)
        {
            // The old lock-to-identity-hosts behavior: "*" blocks everything, the allowlist
            // carves the exceptions back out (Chrome gives the allowlist precedence).
            restrictions.PutStringArray("URLBlocklist", ["*"]);
            restrictions.PutStringArray("URLAllowlist", login.AllowedUrlPatterns.ToArray());
        }
        // Otherwise (the default now): no URL filter, so an app's OAuth Custom Tab can complete its
        // cross-domain login. Chrome is still hidden + non-default, our RestrictedBrowser contains
        // ordinary links, and the filtering DNS blocks adult/social — so this is not a browsing door.

        try
        {
            dpm.SetApplicationRestrictions(admin, login.Package, restrictions);
        }
        catch (Java.Lang.Exception e)
        {
            Log.Warn(Tag, $"Could not lock down the login browser {login.Package}: {e.Message}");
            events.Add(Event(PolicyEventKind.ConfigFailed, $"Could not lock down the login browser {login.Package}: {e.Message}"));
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

    // The never-visible categories (games / social / dating / gambling / short-form): every launchable
    // app whose declared category or curated id falls in one, hard-suspended by the plan. The pure
    // HardBlock rule is unit-tested; this only supplies the installed (package, category) facts.
    private static IReadOnlySet<string> QueryNeverVisiblePackages(Context context)
    {
        var launcher = new Intent(Intent.ActionMain);
        launcher.AddCategory(Intent.CategoryLauncher);
        var installed = context.PackageManager!
            .QueryIntentActivities(launcher, PackageInfoFlags.MatchAll)
            .Select(r => (r.ActivityInfo!.PackageName!,
                (int)(r.ActivityInfo.ApplicationInfo?.Category ?? ApplicationCategories.Undefined)))
            .ToList();
        return HardBlock.Detect(installed);
    }

    // Every package with a launcher entry — the apps the owner can see. Everything here that the plan
    // does not suspend is actively released (visibility IS runnability).
    private static IReadOnlyList<string> QueryLaunchablePackages(Context context)
    {
        var launcher = new Intent(Intent.ActionMain);
        launcher.AddCategory(Intent.CategoryLauncher);
        return context.PackageManager!
            .QueryIntentActivities(launcher, PackageInfoFlags.MatchAll)
            .Select(r => r.ActivityInfo!.PackageName!)
            .Distinct()
            .ToList();
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
