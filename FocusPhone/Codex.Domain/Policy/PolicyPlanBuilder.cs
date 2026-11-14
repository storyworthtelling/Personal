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

        // Accounts are added and removed only inside the owner's Admin session (owner's ruling, audit W7).
        // A token refresh or a consent screen for an existing account is not a modification, so the apps'
        // sign-ins keep working; adding a new account waits for Admin mode.
        if (!context.AdminModeActive)
        {
            restrictions.Add(AndroidUserRestrictions.NoModifyAccounts);
        }

        var suspended = new HashSet<string>(context.BrowserPackages);
        suspended.UnionWith(context.OtherHomePackages);
        // The never-visible categories (games/social/dating/gambling/short-form) are hard-suspended
        // and never released — not by the Admin blocklist, not by the allowlist below. The owner's
        // manual per-app block is deliberately NOT suspended here: a blocked app is only kept off
        // the shelf (ShelfSceneBuilder filters it out of the tray), so a block never interrupts a
        // background function (sync, alarms, notifications) the owner still relies on.
        suspended.UnionWith(context.NeverVisiblePackages);
        suspended.ExceptWith(context.OwnPackages);
        // Suspending the Play Store would break background auto-updates, and the owner installs and
        // updates apps from it, so it is neither suspended nor hidden. An earlier build hid it
        // (setApplicationHidden) and that persists on the device, so it is un-hidden on every pass.
        suspended.Remove(PolicyContext.PlayStorePackage);
        var unhidden = new HashSet<string> { PolicyContext.PlayStorePackage };
        // The platform refuses to suspend this one regardless of what the plan asks for; asking
        // anyway just repeats a permanent, expected failure in the policy log every pass.
        suspended.Remove(PolicyContext.SettingsPackage);

        // The cutover allowlist stays runnable: the curated third-party apps
        // (GitHub, Gemini, Maps, reMarkable). Removed from the suspend set here, and
        // carried out as RunnablePackages below so a package already quarantined
        // by the install watcher is actively released on the next policy pass —
        // omitting it from the suspend set alone would not un-suspend it.
        var runnable = new HashSet<string>(PolicyContext.AllowlistedPackages);
        // The login browser is NOT part of that always-on allowlist: keeping it runnable for the
        // lifetime of the allowlisted apps let any of them open Chrome for an ordinary in-app link
        // (Maps opening a business's website), not just an OAuth sign-in. It is un-suspended only
        // while the owner has deliberately opened an Admin session, and stays suspended like every
        // other browser the rest of the time.
        if (context.AdminModeActive)
        {
            runnable.UnionWith(PolicyContext.LoginBrowserPackages);
        }
        // The recents provider (Pixel Launcher on a Pixel) runs the swipe-up gesture and the
        // back-to-home animation even though Flyleaf is the pinned home. Suspended with the other
        // homes, leaving any app showed "blocked by your admin" rather than the shelf. It is kept
        // runnable — Flyleaf stays home through the persistent preferred activity — unless it is a
        // never-visible app, which nothing releases.
        if (context.SystemRecentsPackage is { } recents && !context.NeverVisiblePackages.Contains(recents))
        {
            runnable.Add(recents);
        }
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
            new(PolicyContext.CommunicationPackage, PolicyContext.ReadPhoneNumbersPermission),
            new(PolicyContext.CommunicationPackage, PolicyContext.SendSmsPermission),
        };

        // Disable the status bar so its swipe-down shade — notifications, quick
        // settings, and the shortcut into system Settings — can't be pulled onto
        // the kiosk. NotificationListenerService still receives notifications
        // (tile tints and message capture are unaffected); only the overlay UI is
        // blocked. Device-owner only, so this takes effect at provisioning.
        return new PolicyPlan(
            restrictions, suspended, unhidden, runnable, permissionGrants,
            EnforceAutoTime: true, FamilyDnsHost, DisableStatusBar: true,
            // Chrome is only ever runnable during an Admin session (above), so the URL lock only
            // ever matters there — but it does matter there: without it, an Admin-mode sign-in
            // Custom Tab could be steered to any site, not just the identity provider. Lock it to
            // LoginBrowserUrlAllowlist whenever Chrome is allowed to run at all.
            LoginBrowser: new LoginBrowserPolicy(
                PolicyContext.LoginBrowserPackage, PolicyContext.LoginBrowserUrlAllowlist,
                BlockNonAllowlisted: context.AdminModeActive));
    }
}
