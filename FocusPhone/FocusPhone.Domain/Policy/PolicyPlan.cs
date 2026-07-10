namespace FocusPhone.Domain.Policy;

public record PolicyPlan(
    IReadOnlySet<string> UserRestrictions,
    IReadOnlySet<string> SuspendedPackages,
    IReadOnlyList<PermissionGrant> PermissionGrants,
    bool EnforceAutoTime,
    string PrivateDnsHost);
