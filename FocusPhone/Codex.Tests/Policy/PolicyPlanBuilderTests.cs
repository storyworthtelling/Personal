using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

public class PolicyPlanBuilderTests
{
    private static PolicyContext DefaultContext(
        bool finalized = false,
        IEnumerable<string>? browserPackages = null,
        IEnumerable<string>? otherHomePackages = null,
        IEnumerable<string>? neverVisiblePackages = null,
        bool adminModeActive = false) =>
        new(
            OwnPackages: PolicyContext.FocusPhonePackages,
            BrowserPackages: (browserPackages ?? ["com.android.chrome"]).ToHashSet(),
            OtherHomePackages: (otherHomePackages ?? ["com.google.android.apps.nexuslauncher"]).ToHashSet(),
            DebuggingLockedDown: finalized,
            NeverVisiblePackages: (neverVisiblePackages ?? []).ToHashSet(),
            AdminModeActive: adminModeActive);

    [Fact]
    public void Plan_IncludesCoreRestrictions()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        // Block escaping the lockdown; keep time automatic.
        Assert.Contains(AndroidUserRestrictions.NoSafeBoot, plan.UserRestrictions);
        Assert.Contains(AndroidUserRestrictions.NoFactoryReset, plan.UserRestrictions);
        Assert.Contains(AndroidUserRestrictions.NoConfigDateTime, plan.UserRestrictions);
    }

    [Fact]
    public void Plan_AllowsSideloadingAndUninstalling()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        // The owner may sideload a build and uninstall any app — these are NOT restricted.
        Assert.DoesNotContain(AndroidUserRestrictions.NoInstallUnknownSources, plan.UserRestrictions);
        Assert.DoesNotContain(AndroidUserRestrictions.NoUninstallApps, plan.UserRestrictions);
    }

    [Fact]
    public void RestrictionsToClear_IncludesRetiredUninstallBlock_ThatAnEarlierBuildMayHaveLeftSet()
    {
        // AddUserRestriction persists on the device until cleared, so a restriction dropped from the
        // plan stays in force forever unless something removes it (silent uninstall was refused with
        // DELETE_FAILED_USER_RESTRICTED for exactly this reason).
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        var toClear = AndroidUserRestrictions.ToClear(plan.UserRestrictions);

        Assert.Contains(AndroidUserRestrictions.NoUninstallApps, toClear);
        Assert.Contains(AndroidUserRestrictions.NoControlApps, toClear);
        Assert.DoesNotContain(AndroidUserRestrictions.NoSafeBoot, toClear);
    }

    [Fact]
    public void RestrictionsToClear_NeverClearsAPlannedRestriction()
    {
        var toClear = AndroidUserRestrictions.ToClear([AndroidUserRestrictions.NoUninstallApps]);

        Assert.DoesNotContain(AndroidUserRestrictions.NoUninstallApps, toClear);
    }

    [Fact]
    public void NeverVisiblePackages_AreSuspended_AndNotReleasedByTheAllowlist()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(
            neverVisiblePackages: ["com.tinder", "com.king.candycrushsaga"]));

        Assert.Contains("com.tinder", plan.SuspendedPackages);
        Assert.Contains("com.king.candycrushsaga", plan.SuspendedPackages);
    }

    [Fact]
    public void Plan_NeverRestrictsAccountModification()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(finalized: true));

        Assert.DoesNotContain(AndroidUserRestrictions.NoModifyAccounts, plan.UserRestrictions);
    }

    [Fact]
    public void DebuggingRestriction_AbsentBeforeCutoverFinalization()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(finalized: false));

        Assert.DoesNotContain(AndroidUserRestrictions.NoDebuggingFeatures, plan.UserRestrictions);
    }

    [Fact]
    public void DebuggingRestriction_PresentAfterCutoverFinalization()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(finalized: true));

        Assert.Contains(AndroidUserRestrictions.NoDebuggingFeatures, plan.UserRestrictions);
    }

    [Fact]
    public void BrowserPackages_AreSuspended()
    {
        // Every browser except the login browser (kept for OAuth Custom Tabs).
        var plan = PolicyPlanBuilder.Build(DefaultContext(browserPackages: ["org.mozilla.firefox", "com.brave.browser"]));

        Assert.Contains("org.mozilla.firefox", plan.SuspendedPackages);
        Assert.Contains("com.brave.browser", plan.SuspendedPackages);
    }

    [Fact]
    public void OtherHomePackages_AreSuspended()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.Contains("com.google.android.apps.nexuslauncher", plan.SuspendedPackages);
    }

    [Fact]
    public void OwnPackages_AreNeverSuspended_EvenWhenDetectedAsBrowserOrHome()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(
            browserPackages: ["com.android.chrome", PolicyContext.LauncherPackage],
            otherHomePackages: [PolicyContext.LauncherPackage]));

        Assert.DoesNotContain(PolicyContext.LauncherPackage, plan.SuspendedPackages);
    }

    [Fact]
    public void PlayStore_IsNotSuspended()
    {
        // Suspending the Play Store would break background auto-updates, so it is excluded from
        // the suspend candidate set regardless of how it was detected.
        var plan = PolicyPlanBuilder.Build(DefaultContext(otherHomePackages: ["com.android.vending"]));

        Assert.DoesNotContain("com.android.vending", plan.SuspendedPackages);
    }

    [Fact]
    public void SettingsPackage_IsNotSuspended_EvenIfDetectedAsABrowser()
    {
        // The platform refuses to suspend it regardless, and it can surface in the
        // browser-resolving package query on some OEM builds via a deep-link handler.
        var plan = PolicyPlanBuilder.Build(DefaultContext(
            browserPackages: [PolicyContext.SettingsPackage, "com.android.chrome"]));

        Assert.DoesNotContain(PolicyContext.SettingsPackage, plan.SuspendedPackages);
    }

    [Fact]
    public void CutoverAllowlistedApps_AreNeverSuspended_EvenIfDetectedAsBrowsers()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(
            browserPackages: [PolicyContext.GitHubPackage, PolicyContext.MapsPackage]));

        Assert.DoesNotContain(PolicyContext.GitHubPackage, plan.SuspendedPackages);
        Assert.DoesNotContain(PolicyContext.GeminiPackage, plan.SuspendedPackages);
        Assert.DoesNotContain(PolicyContext.MapsPackage, plan.SuspendedPackages);
        Assert.DoesNotContain(PolicyContext.RemarkablePackage, plan.SuspendedPackages);
    }

    [Fact]
    public void CutoverAllowlistedApps_AreCarriedAsRunnable_SoAQuarantinedAppIsReleased()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.Contains(PolicyContext.GitHubPackage, plan.RunnablePackages);
        Assert.Contains(PolicyContext.GeminiPackage, plan.RunnablePackages);
        Assert.Contains(PolicyContext.MapsPackage, plan.RunnablePackages);
        Assert.Contains(PolicyContext.RemarkablePackage, plan.RunnablePackages);
    }

    [Fact]
    public void LoginBrowser_IsSuspended_OutsideAdminMode_LikeAnyOtherBrowser()
    {
        // An allowlisted app (Maps, GitHub, Gemini, reMarkable) must not be able to reach Chrome for
        // an ordinary in-app link just because it's on the cutover allowlist — Chrome is suspended
        // like every other browser unless the owner has deliberately opened an Admin session.
        var plan = PolicyPlanBuilder.Build(DefaultContext(
            browserPackages: [PolicyContext.LoginBrowserPackage, "org.mozilla.firefox"],
            adminModeActive: false));

        Assert.Contains(PolicyContext.LoginBrowserPackage, plan.SuspendedPackages);
        Assert.DoesNotContain(PolicyContext.LoginBrowserPackage, plan.RunnablePackages);
        Assert.Contains("org.mozilla.firefox", plan.SuspendedPackages);
    }

    [Fact]
    public void LoginBrowser_IsRunnable_OnlyWhileAdminModeIsActive()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(
            browserPackages: [PolicyContext.LoginBrowserPackage, "org.mozilla.firefox"],
            adminModeActive: true));

        // The login browser is un-suspended for OAuth Custom Tabs during the owner's Admin session;
        // other browsers stay suspended even then.
        Assert.DoesNotContain(PolicyContext.LoginBrowserPackage, plan.SuspendedPackages);
        Assert.Contains(PolicyContext.LoginBrowserPackage, plan.RunnablePackages);
        Assert.Contains("org.mozilla.firefox", plan.SuspendedPackages);
    }

    [Fact]
    public void LoginBrowser_DoesNotBlockNonAllowlistedUrls_OutsideAdminMode()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(adminModeActive: false));

        Assert.Equal(PolicyContext.LoginBrowserPackage, plan.LoginBrowser.Package);
        // Moot in practice (Chrome is suspended outside Admin mode regardless), but the flag itself
        // should still track AdminModeActive rather than being unconditionally on or off.
        Assert.False(plan.LoginBrowser.BlockNonAllowlisted);
    }

    [Fact]
    public void LoginBrowser_BlocksNonAllowlistedUrls_WhileAdminModeIsActive()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(adminModeActive: true));

        Assert.Equal(PolicyContext.LoginBrowserPackage, plan.LoginBrowser.Package);
        // The one window Chrome actually runs in is locked to the identity-provider hosts, so it
        // stays a sign-in surface rather than becoming free general browsing.
        Assert.True(plan.LoginBrowser.BlockNonAllowlisted);
    }

    [Fact]
    public void Plan_EnforcesAutoTimeAndTheFamilyFilterDns()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.True(plan.EnforceAutoTime);
        // CleanBrowsing family filter — the DNS half of the adult/social block.
        Assert.Equal("family-filter-dns.cleanbrowsing.org", plan.PrivateDnsHost);
    }

    [Fact]
    public void Plan_DisablesStatusBar_ToBlockTheSwipeDownShade()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.True(plan.DisableStatusBar);
    }

    [Fact]
    public void StatusBarDisable_SurvivesCutoverFinalization()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(finalized: true));

        Assert.True(plan.DisableStatusBar);
    }

    [Fact]
    public void Plan_GrantsHardRestrictedSmsAndCallLogToCommunicationApp()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.Contains(
            new PermissionGrant(PolicyContext.CommunicationPackage, PolicyContext.ReadSmsPermission),
            plan.PermissionGrants);
        Assert.Contains(
            new PermissionGrant(PolicyContext.CommunicationPackage, PolicyContext.ReadCallLogPermission),
            plan.PermissionGrants);
    }

    [Fact]
    public void Plan_GrantsWriteCallLogAndWriteContactsForDestructiveInboxWrites()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.Contains(
            new PermissionGrant(PolicyContext.CommunicationPackage, PolicyContext.WriteCallLogPermission),
            plan.PermissionGrants);
        Assert.Contains(
            new PermissionGrant(PolicyContext.CommunicationPackage, PolicyContext.WriteContactsPermission),
            plan.PermissionGrants);
    }

    // Post reads this phone's own number to leave it out of a group's members; Android
    // refuses the read without READ_PHONE_NUMBERS, even to the default SMS app.
    [Fact]
    public void Plan_GrantsReadPhoneNumbersSoGroupsLeaveThisPhoneOut()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.Contains(
            new PermissionGrant(PolicyContext.CommunicationPackage, PolicyContext.ReadPhoneNumbersPermission),
            plan.PermissionGrants);
    }

    [Fact]
    public void Plan_GrantsAreScopedToTheCommunicationApp()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.All(plan.PermissionGrants,
            grant => Assert.Equal(PolicyContext.CommunicationPackage, grant.Package));
    }

    [Fact]
    public void Plan_GrantsSurviveCutoverFinalization()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(finalized: true));

        Assert.Contains(
            new PermissionGrant(PolicyContext.CommunicationPackage, PolicyContext.ReadSmsPermission),
            plan.PermissionGrants);
    }
}
