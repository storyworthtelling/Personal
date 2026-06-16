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

        return new PolicyPlan(restrictions, suspended, EnforceAutoTime: true, FamilyDnsHost);
    }
}
