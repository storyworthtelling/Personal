namespace Codex.Domain.Policy;

public record PolicyPlan(
    IReadOnlySet<string> UserRestrictions,
    IReadOnlySet<string> SuspendedPackages,
    IReadOnlySet<string> RunnablePackages,
    IReadOnlyList<PermissionGrant> PermissionGrants,
    bool EnforceAutoTime,
    string PrivateDnsHost,
    bool DisableStatusBar,
    LoginBrowserPolicy LoginBrowser);

/// <summary>
/// Chrome's managed configuration for the login browser. Incognito is always disabled. When
/// <see cref="BlockNonAllowlisted"/> is true, every URL except <see cref="AllowedUrlPatterns"/>
/// is blocked (the old lock-to-identity-hosts behavior). It is now <b>false</b>: locking Chrome to
/// a tiny allowlist broke legitimate app logins (an OAuth Custom Tab redirects across the app's own
/// and third-party auth hosts), so the login Custom Tab is left unrestricted and the general-browsing
/// wall is enforced elsewhere instead — Chrome stays hidden and non-default, our RestrictedBrowser
/// contains ordinary links to their origin, and the filtering DNS blocks adult/social network-wide.
/// </summary>
public record LoginBrowserPolicy(
    string Package, IReadOnlyList<string> AllowedUrlPatterns, bool BlockNonAllowlisted);
