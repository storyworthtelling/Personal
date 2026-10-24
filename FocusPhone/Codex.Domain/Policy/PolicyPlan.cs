namespace Codex.Domain.Policy;

public record PolicyPlan(
    IReadOnlySet<string> UserRestrictions,
    IReadOnlySet<string> SuspendedPackages,
    // Hidden rather than suspended: a hidden app cannot be launched, but it keeps running its
    // background work. Applied with DevicePolicyManager.setApplicationHidden.
    IReadOnlySet<string> HiddenPackages,
    IReadOnlySet<string> RunnablePackages,
    IReadOnlyList<PermissionGrant> PermissionGrants,
    bool EnforceAutoTime,
    string PrivateDnsHost,
    bool DisableStatusBar,
    LoginBrowserPolicy LoginBrowser);

/// <summary>
/// Chrome's managed configuration for the login browser. Incognito is always disabled. When
/// <see cref="BlockNonAllowlisted"/> is true, every URL except <see cref="AllowedUrlPatterns"/>
/// is blocked. PolicyPlanBuilder sets it to match <c>PolicyContext.AdminModeActive</c>: Chrome is
/// suspended entirely outside an Admin session, so the flag has no effect then; while Admin mode is
/// open — the one window Chrome is un-suspended for OAuth Custom Tabs — this locks it to the
/// identity-provider hosts so it stays a sign-in surface rather than becoming free general browsing.
/// </summary>
public record LoginBrowserPolicy(
    string Package, IReadOnlyList<string> AllowedUrlPatterns, bool BlockNonAllowlisted);
