using FocusPhone.Domain.Policy;
using Xunit;

namespace FocusPhone.Tests.Policy;

public class PolicyPlanBuilderTests
{
    private static PolicyContext DefaultContext(
        bool finalized = false,
        IEnumerable<string>? browserPackages = null,
        IEnumerable<string>? otherHomePackages = null,
        IEnumerable<string>? blockedPackages = null) =>
        new(
            OwnPackages: PolicyContext.FocusPhonePackages,
            BrowserPackages: (browserPackages ?? ["com.android.chrome"]).ToHashSet(),
            OtherHomePackages: (otherHomePackages ?? ["com.google.android.apps.nexuslauncher"]).ToHashSet(),
            BlockedPackages: (blockedPackages ?? []).ToHashSet(),
            DebuggingLockedDown: finalized);

    [Fact]
    public void Plan_IncludesCoreRestrictions()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.Contains(AndroidUserRestrictions.NoSafeBoot, plan.UserRestrictions);
        Assert.Contains(AndroidUserRestrictions.NoUninstallApps, plan.UserRestrictions);
        Assert.Contains(AndroidUserRestrictions.NoInstallUnknownSources, plan.UserRestrictions);
        Assert.Contains(AndroidUserRestrictions.NoFactoryReset, plan.UserRestrictions);
        Assert.Contains(AndroidUserRestrictions.NoConfigDateTime, plan.UserRestrictions);
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
    public void BlockedPackages_AreSuspended()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(blockedPackages: ["com.example.game"]));

        Assert.Contains("com.example.game", plan.SuspendedPackages);
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
        var plan = PolicyPlanBuilder.Build(DefaultContext(blockedPackages: ["com.android.vending"]));

        Assert.DoesNotContain("com.android.vending", plan.SuspendedPackages);
    }

    [Fact]
    public void CutoverAllowlistedApps_AreNeverSuspended_EvenIfPreviouslyBlocked()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(
            blockedPackages: [PolicyContext.GitHubPackage, PolicyContext.MapsPackage]));

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
    public void LoginBrowser_IsNotSuspended_EvenThoughItIsABrowser()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext(
            browserPackages: [PolicyContext.LoginBrowserPackage, "org.mozilla.firefox"]));

        // The login browser stays runnable for OAuth Custom Tabs; other browsers
        // are still suspended.
        Assert.DoesNotContain(PolicyContext.LoginBrowserPackage, plan.SuspendedPackages);
        Assert.Contains(PolicyContext.LoginBrowserPackage, plan.RunnablePackages);
        Assert.Contains("org.mozilla.firefox", plan.SuspendedPackages);
    }

    [Fact]
    public void LoginBrowser_IsLockedToTheIdentityProviderHosts()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.Equal(PolicyContext.LoginBrowserPackage, plan.LoginBrowser.Package);
        // A representative host from each provider is on the allowlist.
        Assert.Contains("accounts.google.com", plan.LoginBrowser.AllowedUrlPatterns);
        Assert.Contains("github.com", plan.LoginBrowser.AllowedUrlPatterns);
        Assert.Contains("remarkable.com", plan.LoginBrowser.AllowedUrlPatterns);
    }

    [Fact]
    public void LoginBrowserAllowlist_KeepsGeneralBrowsingClosed()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        // Deliberately narrow: no bare google.com, so search and general browsing
        // stay blocked when everything-else is denied by the "*" blocklist.
        Assert.DoesNotContain("google.com", plan.LoginBrowser.AllowedUrlPatterns);
        Assert.DoesNotContain("*", plan.LoginBrowser.AllowedUrlPatterns);
    }

    [Fact]
    public void Plan_EnforcesAutoTimeAndFamilyDns()
    {
        var plan = PolicyPlanBuilder.Build(DefaultContext());

        Assert.True(plan.EnforceAutoTime);
        Assert.Equal("family.cloudflare-dns.com", plan.PrivateDnsHost);
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
