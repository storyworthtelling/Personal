namespace Codex.Domain.Policy;

public record PolicyPlan(
    IReadOnlySet<string> UserRestrictions,
    IReadOnlySet<string> SuspendedPackages,
    // Packages to show again with DevicePolicyManager.setApplicationHidden(false). A hide is stored
    // on the device and outlives the build that set it, so a package an earlier build hid has to be
    // released by an explicit, repeated instruction. A hidden package also reads as "not installed",
    // so this is applied without the usual installed check.
    IReadOnlySet<string> UnhiddenPackages,
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
