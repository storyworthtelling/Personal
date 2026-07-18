namespace FocusPhone.Domain.Policy;

/// <summary>
/// Composes the full device-owner policy from observed device state. The
/// PolicyManager in the launcher applies the result verbatim; all decisions
/// live here where they can be unit tested.
/// </summary>
public static class PolicyPlanBuilder
{
    public const string FamilyDnsHost = "family.cloudflare-dns.com";

    public static PolicyPlan Build(PolicyContext context)
    {
        var restrictions = new HashSet<string>
        {
            AndroidUserRestrictions.NoSafeBoot,
            AndroidUserRestrictions.NoUninstallApps,
            AndroidUserRestrictions.NoInstallUnknownSources,
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
        suspended.ExceptWith(context.OwnPackages);
        // Suspending the Play Store would break background auto-updates; it is
        // kept runnable but never surfaced in the launcher.
        suspended.Remove(PolicyContext.PlayStorePackage);

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
            restrictions, suspended, permissionGrants,
            EnforceAutoTime: true, FamilyDnsHost, DisableStatusBar: true);
    }
}
