namespace FocusPhone.Domain.Policy;

public record PolicyPlan(
    IReadOnlySet<string> UserRestrictions,
    IReadOnlySet<string> SuspendedPackages,
    bool EnforceAutoTime,
    string PrivateDnsHost);
