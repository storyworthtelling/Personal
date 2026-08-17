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
/// Chrome's managed configuration for the login-only browser: block every URL
/// except <see cref="AllowedUrlPatterns"/> (the identity-provider hosts) and
/// disable incognito, so neither the Custom Tab's "Open in Chrome" overflow nor
/// a link on a sign-in page can reach the open internet.
/// </summary>
public record LoginBrowserPolicy(string Package, IReadOnlyList<string> AllowedUrlPatterns);
