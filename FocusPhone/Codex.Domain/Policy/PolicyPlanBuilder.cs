namespace Codex.Domain.Policy;

/// <summary>
/// Composes the full device-owner policy from observed device state. The
/// PolicyManager in the launcher applies the result verbatim; all decisions
/// live here where they can be unit tested.
/// </summary>
public static class PolicyPlanBuilder
{
    // The enforced Private DNS host: CleanBrowsing's family filter, which blocks adult content
    // (and proxies/VPN bypasses) network-wide — the DNS half of the adult/social block.
    public const string FamilyDnsHost = "family-filter-dns.cleanbrowsing.org";

    public static PolicyPlan Build(PolicyContext context)
    {
        // Block escaping the lockdown (safe-boot bypasses the launcher; factory-reset wipes it) and
        // keep time automatic. Sideloading (unknown sources) and uninstalling apps are deliberately
        // NOT restricted — the owner may sideload a build and remove any app.
        var restrictions = new HashSet<string>
        {
            AndroidUserRestrictions.NoSafeBoot,
            AndroidUserRestrictions.NoFactoryReset,
            AndroidUserRestrictions.NoConfigDateTime,
        };
        if (context.DebuggingLockedDown)
        {
            restrictions.Add(AndroidUserRestrictions.NoDebuggingFeatures);
        }

        var suspended = new HashSet<string>(context.BrowserPackages);
        suspended.UnionWith(context.OtherHomePackages);
        suspended.UnionWith(context.BlockedPackages);
        // The never-visible categories (games/social/dating/gambling/short-form) are hard-suspended
        // and never released — not by the Admin blocklist, not by the allowlist below.
        suspended.UnionWith(context.NeverVisiblePackages);
        suspended.ExceptWith(context.OwnPackages);
        // Suspending the Play Store would break background auto-updates; it is
        // kept runnable but never surfaced in the launcher.
        suspended.Remove(PolicyContext.PlayStorePackage);

        // The cutover allowlist stays runnable: the curated third-party apps
        // (GitHub, Gemini, Maps, reMarkable) plus the system browser they need
        // for an OAuth Custom Tab sign-in. Removed from the suspend set here, and
        // carried out as RunnablePackages below so a package already quarantined
        // by the install watcher is actively released on the next policy pass —
        // omitting it from the suspend set alone would not un-suspend it.
        var runnable = new HashSet<string>(PolicyContext.AllowlistedPackages);
        runnable.UnionWith(PolicyContext.LoginBrowserPackages);
        suspended.ExceptWith(runnable);

        // READ_SMS and READ_CALL_LOG are hard-restricted: the installer only
        // whitelists them (un-restricts), so they still have to be granted. A
        // device-owner grant is the only reliable path — it needs no runtime
        // dialog (unusable on the kiosk), survives a fresh reinstall through the
        // silent-update channel (which arrives whitelisted but not granted), and
        // pins the grant against Android auto-resetting unused-app permissions.
        var permissionGrants = new List<PermissionGrant>
        {
            new(PolicyContext.CommunicationPackage, PolicyContext.ReadSmsPermission),
            new(PolicyContext.CommunicationPackage, PolicyContext.ReadCallLogPermission),
            // Deleting a call/voicemail from the log, and saving/linking a sender
            // into Contacts, are the unified inbox's destructive-CRUD writes.
            new(PolicyContext.CommunicationPackage, PolicyContext.WriteCallLogPermission),
            new(PolicyContext.CommunicationPackage, PolicyContext.WriteContactsPermission),
        };

        // Disable the status bar so its swipe-down shade — notifications, quick
        // settings, and the shortcut into system Settings — can't be pulled onto
        // the kiosk. NotificationListenerService still receives notifications
        // (tile tints and message capture are unaffected); only the overlay UI is
        // blocked. Device-owner only, so this takes effect at provisioning.
        return new PolicyPlan(
            restrictions, suspended, runnable, permissionGrants,
            EnforceAutoTime: true, FamilyDnsHost, DisableStatusBar: true,
            // Do NOT lock Chrome to the allowlist: that blocked legitimate app logins (e.g. an
            // OAuth Custom Tab redirecting through an app's own auth host). Logins run unrestricted;
            // Chrome stays hidden + non-default, RestrictedBrowser contains ordinary links, and the
            // filtering DNS blocks adult/social. The allowlist is kept for reference / a future
            // re-tightening but not enforced (BlockNonAllowlisted: false).
            LoginBrowser: new LoginBrowserPolicy(
                PolicyContext.LoginBrowserPackage, PolicyContext.LoginBrowserUrlAllowlist,
                BlockNonAllowlisted: false));
    }
}
